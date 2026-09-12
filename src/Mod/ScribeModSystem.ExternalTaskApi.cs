using System;
using System.Collections.Generic;
using Scribe.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Scribe;

// Server-side entry point for another installed mod to create a task on a player's behalf
// (add-external-mod-task-api) — Notice Board is the first caller. A direct in-process call, no
// networking: the caller already has the server-side ScribeModSystem instance (an optional soft
// dependency) and calls TryCreateExternalTask straight away. Resolves the target the same
// silent, no-picker way the Handbook "Add to Scribe" flow does (ResolveWriteableCarriedSlot in
// ScribeModSystem.Handbook.cs), just server-side and keyed off a per-player last-opened tracker
// instead of the client-only lastOpenedScribeItemDocId field.
public sealed partial class ScribeModSystem
{
    /// <summary>Server-side, in-memory, per-player last-opened Scribe item tracker — the server-side
    /// counterpart of the client-only <see cref="lastOpenedScribeItemDocId"/>, populated by
    /// <see cref="NoteServerScribeItemOpened"/> from the existing <see cref="OnServerReceivedNotebookOpened"/>
    /// handler (already sent by all three Scribe item types on dialog-open). Never persisted to the save
    /// file and starts empty on server boot: a stale/wrong entry only affects a convenience default and
    /// falls back to the first writeable carried item, exactly like the Handbook flow already tolerates
    /// for the same reason. Null on a pure client.</summary>
    private Dictionary<string, Guid>? lastOpenedDocIdByPlayer;

    /// <summary>Records that <paramref name="playerUid"/> just opened the Scribe item carrying
    /// <paramref name="docId"/>, so a later <see cref="TryCreateExternalTask"/> call on their behalf prefers
    /// that same book. Called from <see cref="OnServerReceivedNotebookOpened"/>. <c>internal</c> (not
    /// <c>private</c>) solely so the integration suite can simulate an item-open directly rather than
    /// round-tripping a real network packet, via the project's own <c>InternalsVisibleTo("Integration.Tests")</c> —
    /// matching the same pattern <see cref="OnServerReceivedNotebookSave"/> already uses.</summary>
    internal void NoteServerScribeItemOpened(string playerUid, Guid docId)
    {
        lastOpenedDocIdByPlayer ??= new Dictionary<string, Guid>();
        lastOpenedDocIdByPlayer[playerUid] = docId;
    }

    /// <summary>Outcome of <see cref="ResolveExternalTaskTargetSlot"/> — distinguishes the two ways
    /// resolution can fail from the resolved-slot success case, since <see cref="TryCreateExternalTask"/>
    /// reports a different failure notice for each.</summary>
    private enum ExternalTaskTargetResolution
    {
        Resolved,
        NoScribeItem,
        AllLocked,
    }

    /// <summary>Public, server-side entry point any other mod can call to create a task on
    /// <paramref name="player"/>'s behalf (add-external-mod-task-api) — the whole public surface Scribe
    /// exposes to external mods. Direct in-process call: no network round trip, so a caller that only
    /// holds Scribe as an optional soft dependency (<c>IsModEnabled</c>/null-checked) can call this straight
    /// after resolving Scribe's mod system. Requires no code from Scribe to support a caller's absence —
    /// the method's existence is the only contract.
    ///
    /// <para><paramref name="title"/> becomes a top-level checkbox task; <paramref name="bodyText"/>, when
    /// also present, becomes a non-checkbox note nested beneath it (or promoted to stand alone at the top
    /// level when <paramref name="title"/> is absent) — see <see cref="ScribeExternalTaskMapper"/> for the
    /// exact mapping. Both are clipped, never rejected, when over Scribe's existing per-kind length caps.
    /// <paramref name="extraInfo"/> is an opaque, caller-composed string Scribe never interprets: when
    /// non-empty it attaches to the resulting top-level block and renders as hoverable detail on the Read
    /// view, Editor, and Pin Tab (never the HUD).</para>
    ///
    /// <para>The target is resolved silently (no picker, since this call has no client leg to show one in):
    /// the player's last-opened writeable carried Scribe item, else their first writeable carried Scribe
    /// item, else this fails. On failure the target player is notified directly of the reason (no Scribe
    /// item carried, every carried item locked, or the resolved target is full) via
    /// <see cref="IServerPlayer.SendIngameError"/> — the calling mod needs no failure UI of its own. The
    /// returned <c>bool</c> exists only for the calling mod's own control flow.</para>
    /// </summary>
    public bool TryCreateExternalTask(IServerPlayer player, string? title, string? bodyText, string? extraInfo)
    {
        if (sapi is null) return false;

        var newBlocks = ScribeExternalTaskMapper.MapToBlocks(title, bodyText, extraInfo);
        if (newBlocks.Count == 0) return true; // both empty: nothing meaningful to create, not a failure

        var resolution = ResolveExternalTaskTargetSlot(player, out var slot);
        if (resolution != ExternalTaskTargetResolution.Resolved
            || slot?.Itemstack?.Collectible is not IScribeDocumentItem item)
        {
            NotifyExternalTaskFailure(player, resolution);
            return false;
        }

        var stack = slot.Itemstack!;
        var doc = ScribeDocumentAttributes.TryReadFrom(stack, out var existing) && existing is not null
            ? existing
            : new ScribeDocument();

        var policy = item.DocumentPolicy(slot);
        if (!policy.CanHold(doc.BlockCount + newBlocks.Count))
        {
            player.SendIngameError("scribe-external-target-full", Lang.Get("scribe:scribe-external-target-full"));
            return false;
        }

        foreach (var block in newBlocks)
            doc.AppendExternalBlock(block); // verbatim append; the mapper already minted fresh TaskIds

        ScribeDocumentAttributes.WriteTo(stack, doc);
        slot.MarkDirty();

        if (pinStore is { } store)
            PushPinsTo(store.ReconcileSnapshotsForActor(player.PlayerUID, doc.DocId, doc));

        sapi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeNotebookSaveMessage
        {
            DocIdBytes = doc.DocId.ToByteArray(),
            DocumentBytes = ScribeDocumentCodec.Serialize(doc),
        }, player);

        return true;
    }

    /// <summary>Server-side counterpart of <see cref="ResolveWriteableCarriedSlot"/>, keyed off
    /// <paramref name="player"/>'s tracked <see cref="lastOpenedDocIdByPlayer"/> entry instead of the
    /// client-only <see cref="lastOpenedScribeItemDocId"/> field. Prefers that last-opened item if it is
    /// currently carried and writeable, else the first writeable carried Scribe item, else fails —
    /// distinguishing "no Scribe item at all" from "every carried item is locked" so
    /// <see cref="TryCreateExternalTask"/> can notify the player with the right reason. Scans only the
    /// player's own hotbar + backpack via <see cref="EnumerateCarriedSlots"/> (ground/chest/creative are
    /// never considered).</summary>
    private ExternalTaskTargetResolution ResolveExternalTaskTargetSlot(IServerPlayer player, out ItemSlot? slot)
    {
        slot = null;
        Guid? wanted = lastOpenedDocIdByPlayer is { } tracked && tracked.TryGetValue(player.PlayerUID, out var docId)
            ? docId
            : null;

        bool anyScribeItem = false;
        ItemSlot? firstWriteable = null;
        foreach (var candidate in EnumerateCarriedSlots(player))
        {
            if (candidate.Itemstack?.Collectible is not IScribeDocumentItem item) continue;
            anyScribeItem = true;

            // Skip a read-only item (hardened/fired tablet); it can't take the append.
            if (!item.IsSlotWriteable(candidate)) continue;
            firstWriteable ??= candidate;

            // The last-opened book wins immediately — but only when it's the writeable candidate we just
            // vetted (a hardened last-opened tablet falls through to the next writeable item above).
            if (wanted is { } w
                && ScribeDocumentAttributes.TryReadFrom(candidate.Itemstack!, out var doc)
                && doc is not null
                && doc.DocId == w)
            {
                slot = candidate;
                return ExternalTaskTargetResolution.Resolved;
            }
        }

        if (firstWriteable is not null)
        {
            slot = firstWriteable;
            return ExternalTaskTargetResolution.Resolved;
        }

        return anyScribeItem ? ExternalTaskTargetResolution.AllLocked : ExternalTaskTargetResolution.NoScribeItem;
    }

    /// <summary>Notifies <paramref name="player"/> why <see cref="TryCreateExternalTask"/> could not place
    /// their task, distinguishing the "no Scribe item"/"all locked" reasons (the "target full" reason is
    /// notified separately, once capacity is actually checked against a resolved target). Uses
    /// <see cref="IServerPlayer.SendIngameError"/> directly — this call has no client leg, so the usual
    /// client-only <c>TriggerIngameError</c> path is unreachable; this is the minimal server-only
    /// equivalent, requiring no new network message.</summary>
    private static void NotifyExternalTaskFailure(IServerPlayer player, ExternalTaskTargetResolution resolution)
    {
        switch (resolution)
        {
            case ExternalTaskTargetResolution.NoScribeItem:
                player.SendIngameError("scribe-external-no-item", Lang.Get("scribe:scribe-external-no-item"));
                break;
            case ExternalTaskTargetResolution.AllLocked:
                player.SendIngameError("scribe-external-all-locked", Lang.Get("scribe:scribe-external-all-locked"));
                break;
        }
    }
}
