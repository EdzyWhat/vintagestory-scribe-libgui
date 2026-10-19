using System;
using System.Collections.Generic;
using Scribe.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Scribe;

// Server-side entry point for another installed mod (Notice Board is one caller) to create a task on a player's behalf.
// A direct in-process call, no networking. Resolves the target silently, like the Handbook "Add to Scribe" flow
// (ResolveWriteableCarriedSlot), but keyed off a per-player last-opened tracker instead of the client-only lastOpenedScribeItemDocId.
public sealed partial class ScribeModSystem
{
    /// <summary>Server-side, in-memory, per-player last-opened Scribe item tracker (counterpart of the client-only
    /// <see cref="lastOpenedScribeItemDocId"/>), fed by <see cref="NoteServerScribeItemOpened"/>. Not persisted; a stale entry only
    /// affects a convenience default and falls back to the first writeable carried item. Null on a pure client.</summary>
    private Dictionary<string, Guid>? lastOpenedDocIdByPlayer;

    /// <summary>Records that <paramref name="playerUid"/> just opened the item carrying <paramref name="docId"/>, so a later
    /// <see cref="TryCreateExternalTask"/> prefers that book. <c>internal</c> so the integration suite can simulate an item-open
    /// without a network packet.</summary>
    internal void NoteServerScribeItemOpened(string playerUid, Guid docId)
    {
        lastOpenedDocIdByPlayer ??= new Dictionary<string, Guid>();
        lastOpenedDocIdByPlayer[playerUid] = docId;
    }

    /// <summary>Outcome of <see cref="ResolveExternalTaskTargetSlot"/>; each failure case gets a different notice.</summary>
    private enum ExternalTaskTargetResolution
    {
        Resolved,
        NoScribeItem,
        AllLocked,
    }

    /// <summary>
    /// Public, server-side entry point any other mod can call to create a task on <paramref name="player"/>'s behalf; the whole
    /// public surface Scribe exposes to external mods. A direct in-process call, so a caller holding Scribe as an optional soft
    /// dependency can call it after null-checking the mod system.
    ///
    /// <para><paramref name="title"/> becomes a top-level checkbox task; <paramref name="bodyText"/> becomes a non-checkbox note
    /// nested beneath it (or standing alone when <paramref name="title"/> is absent); see <see cref="ScribeExternalTaskMapper"/>.
    /// Both are clipped, never rejected, at Scribe's length caps. <paramref name="extraInfo"/> is an opaque string attached to the
    /// top-level block and shown as hover detail on the Read view, Editor, and Pin Tab (never the HUD).</para>
    ///
    /// <para>The target is resolved silently (no picker): the last-opened writeable carried Scribe item, else the first writeable one,
    /// else failure. On failure the player is notified directly via <see cref="IServerPlayer.SendIngameError"/>; the returned
    /// <c>bool</c> is only for the caller's control flow.</para>
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

    /// <summary>Server-side counterpart of <see cref="ResolveWriteableCarriedSlot"/>: prefers the player's last-opened item if carried
    /// and writeable, else the first writeable carried Scribe item. Distinguishes "no Scribe item" from "every carried item locked".
    /// Scans only hotbar + backpack via <see cref="EnumerateCarriedSlots"/>.</summary>
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

            // Skip a read-only item (hardened/fired tablet).
            if (!item.IsSlotWriteable(candidate)) continue;
            firstWriteable ??= candidate;

            // The last-opened book wins only if it is the writeable candidate just vetted.
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

    /// <summary>Notifies <paramref name="player"/> why <see cref="TryCreateExternalTask"/> could not place their task ("no Scribe item" /
    /// "all locked"; "target full" is notified separately). Uses <see cref="IServerPlayer.SendIngameError"/> because this call has
    /// no client leg for <c>TriggerIngameError</c>.</summary>
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
