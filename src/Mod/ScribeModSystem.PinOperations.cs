using System;
using System.Collections.Generic;
using Gui.Rendering;             // SkiaAssetLoader
using Gui.Rendering.Text;        // FontRegistry, FontWeight
using Gui.Sound;                 // ISoundPlayer, SoundPlayer (UI click sound)
using Scribe.Core;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Scribe;

public sealed partial class ScribeModSystem
{
    /// <summary>
    /// Server-side pin/unpin, addressed by (DocId, TaskId). An UNPIN removes straight from the store
    /// with no block resolution, so it works when the owning lectern is broken or its chunk is
    /// unloaded. A PIN resolves the owning block via the live index only to snapshot the task's
    /// text/done from the server's own authoritative document (never a client-supplied snapshot); if
    /// the document can't be resolved right now the pin is still recorded with an empty snapshot.
    /// Lock-free throughout. Re-pushes the player when their set changed. Public so the block-entity
    /// layer and the integration suite drive the exact production path, not a copy of it.
    /// </summary>
    public void SetPinForPlayer(IServerPlayer player, Guid docId, Guid taskId, bool pinned,
        string? fallbackText = null, bool fallbackDone = false,
        ScribeBlockKind fallbackKind = ScribeBlockKind.Task, string? fallbackLinkTarget = null)
    {
        if (sapi is null || pinStore is null) return;

        bool changed;
        if (pinned)
        {
            string text = fallbackText ?? "";
            bool done = fallbackDone;
            ScribeBlockKind kind = fallbackKind;
            string? linkTarget = fallbackLinkTarget;
            // Prefer the server's own authoritative document when available; fall back to the
            // client-supplied snapshot for items whose host is not registered server-side (e.g. Notebooks).
            if (_hostRegistry.TryGetValue(docId, out var host)
                && host.Document.FindByTaskId(taskId) is { } block)
            {
                text = block.Text;
                done = block.Done;
                kind = block.Kind;
                linkTarget = block.LinkTarget;
            }
            changed = pinStore.SetPin(player.PlayerUID, docId, taskId, sapi.World.Calendar.TotalHours, text, done, kind, linkTarget);
        }
        else
        {
            changed = pinStore.RemovePin(player.PlayerUID, docId, taskId);
        }

        if (changed) PushPinsTo(player);
    }

    /// <summary>
    /// Server-side complete-a-task by identity (the read-view checkbox / HUD checkbox), for a task the
    /// player has pinned. The per-player pin store is authoritative for a pinned task's done-state, so
    /// the flow is store-first with write-through:
    /// <list type="number">
    /// <item><b>Toggle in the store</b> — flip the acting player's pin's done-state (the authoritative
    /// value), so completion works even when the source is unresolvable/destroyed.</item>
    /// <item><b>Write through to the source</b> — when the owning document resolves, set its task's done
    /// to match (reconciling ONLY the acting player; other players' pins are their own copies).</item>
    /// <item><b>Apply the completion policy</b> — <c>Sink</c> keeps the (now-done) pin; <c>Unpin</c>
    /// removes the pin; <c>Delete</c> removes the task from the source (when resolvable) and the pin.
    /// Removal/unpin fires only on a transition INTO done, so unchecking a done task never removes it.</item>
    /// </list>
    /// The <paramref name="policy"/> is the acting player's client-local completion preference, carried
    /// in the completion request and already normalized by the caller; it is no longer server-side
    /// state. Re-pushes the acting player once at the end when their set changed. Public for the same
    /// reason as <see cref="SetPinForPlayer"/> — the block-entity layer and the integration suite drive
    /// the exact production path.
    /// </summary>
    public void CompleteTaskForPlayer(IServerPlayer player, Guid docId, Guid taskId,
        ScribeCompletionPolicy policy = ScribeCompletionPolicy.Sink)
    {
        if (sapi is null || pinStore is null) return;

        // The store owns the pinned task's done-state; toggle from there (not the possibly-gone source).
        bool? current = pinStore.GetPinDone(player.PlayerUID, docId, taskId);
        if (current is null)
        {
            // Not pinned by this player — a plain checkbox on an unpinned document task. Toggle the
            // shared document directly and apply the policy there too (the policy is not limited to
            // pinned tasks — scribe-lectern-view-consistency): Sink→bottom, Delete→remove.
            CompleteUnpinnedTaskAtSource(player, docId, taskId, policy);
            return;
        }
        bool nowDone = !current.Value;

        bool changed = pinStore.SetPinDone(player.PlayerUID, docId, taskId, nowDone);
        Trace("  complete: {0}'s pin on task {1} done {2} -> {3}", player.PlayerName, taskId, current.Value, nowDone);

        // Write through to the shared source document when it resolves (best-effort; a gone source just
        // skips this). Reconciles only the acting player — other pinners keep their own copies.
        bool resolved = TryResolveDocHost(docId, out var docHost, player);
        if (resolved) docHost!.SetTaskDoneFromReader(taskId, nowDone);

        // On a read-only (hard/fired) tablet, the source can't be reordered or have tasks removed, and
        // firing must never strand a pin on the HUD. Every document-mutating policy therefore collapses
        // to a plain Unpin (zero-point-three-fixes §7.5 / D8) — so completing simply clears the pin.
        policy = CollapsePolicyForReadOnlySource(policy, resolved ? docHost : null);

        // Apply the completion policy — only on a transition INTO done (unchecking never removes). The
        // Delete/Sink/Unpin mapping lives in the shared Core decision (reconcile-animating-surfaces D9);
        // here we DISPATCH it through the persistence-aware write-through (MoveTaskToBottomFromReader /
        // DeleteTaskFromReader mark the source dirty and resync) and the pin store, rather than re-deriving
        // the switch. The pin removal is applied here regardless of whether the source resolves (the pin is
        // store-authoritative), while the document action is best-effort (a gone source just skips it).
        var decision = ScribeCompletion.Decide(nowDone, policy);
        switch (decision.DocAction)
        {
            case ScribeCompletionDocAction.SinkToBottom:
                if (resolved && docHost!.MoveTaskToBottomFromReader(taskId))
                    Trace("  policy {0}: moved task {1} to bottom of source doc {2}", policy, taskId, docId);
                break;
            case ScribeCompletionDocAction.Delete:
                if (resolved && docHost!.DeleteTaskFromReader(taskId))
                    Trace("  policy Delete: removed task {0} from source doc {1}", taskId, docId);
                else
                    Trace("  policy Delete: source unresolvable for task {0} — pin removed only", taskId);
                break;
        }
        if (decision.ShouldRemovePin)
        {
            changed |= pinStore.RemovePin(player.PlayerUID, docId, taskId);
            Trace("  policy {0}: removed {1}'s pin on task {2}", policy, player.PlayerName, taskId);
        }

        if (changed) PushPinsTo(player);
    }

    /// <summary>
    /// Server-side edit-a-pinned-task's-text by identity (the Pin Tab's inline text edit), for a task the
    /// player has pinned. Best-effort write-through, mirroring <see cref="CompleteTaskForPlayer"/>:
    /// <list type="number">
    /// <item><b>Write through to the source</b> — when the owning document resolves, set its task's text
    /// via the lock-free <see cref="BlockEntityScribeLectern.SetTaskTextFromReader"/> (which rejects a
    /// blank edit and reconciles nothing else).</item>
    /// <item><b>Update the pin snapshot</b> — refresh the acting player's pin's last-known text so the edit
    /// is reflected even when the source is unresolvable (snapshot-only degrade).</item>
    /// </list>
    /// Only the acting player is touched (their own pin is their own copy — grief-proof). A blank/
    /// whitespace-only edit is rejected end-to-end and changes nothing. Re-pushes the acting player when
    /// their set changed. Public so the integration suite drives the exact production path.
    /// </summary>
    public void EditPinnedTaskForPlayer(IServerPlayer player, Guid docId, Guid taskId, string text)
    {
        if (sapi is null || pinStore is null) return;
        if (string.IsNullOrWhiteSpace(text)) return; // reject blank/whitespace-only end-to-end

        // Only edit through pins the player actually holds — an edit is a pin action, not a document RPC.
        if (pinStore.GetPinDone(player.PlayerUID, docId, taskId) is null)
        {
            Trace("  edit: {0} has no pin on task {1} — ignored", player.PlayerName, taskId);
            return;
        }

        // Write through to the shared source document when it resolves (best-effort; a gone source just
        // skips this — the snapshot below still updates).
        if (TryResolveDocHost(docId, out var docHost, player)) docHost!.SetTaskTextFromReader(taskId, text);

        // Always refresh the acting player's pin snapshot so the edit shows even if the source is unloaded.
        bool changed = pinStore.SetPinText(player.PlayerUID, docId, taskId, text);
        if (changed) PushPinsTo(player);
    }

    /// <summary>
    /// Server-side standalone delete-a-task by identity (the Pin Tab's delete control), for a task the
    /// player has pinned. Mirrors the Delete completion policy's write-through, but as a first-class action
    /// independent of any policy: when the owning document resolves, remove the task lock-free via
    /// <see cref="BlockEntityScribeLectern.DeleteTaskFromReader"/>; always remove the acting player's pin
    /// (a safe no-op if it's already gone) and re-push. Snapshot/store-only when the source is unresolvable.
    /// Public so the integration suite drives the exact production path.
    /// </summary>
    public void DeleteTaskForPlayer(IServerPlayer player, Guid docId, Guid taskId)
    {
        if (sapi is null || pinStore is null) return;

        if (TryResolveDocHost(docId, out var docHost, player) && docHost!.DeleteTaskFromReader(taskId))
            Trace("  delete: removed task {0} from source doc {1}", taskId, docId);
        else
            Trace("  delete: source unresolvable for task {0} — pin removed only", taskId);

        bool changed = pinStore.RemovePin(player.PlayerUID, docId, taskId);
        if (changed) PushPinsTo(player);
    }

    /// <summary>
    /// Server-side reorder of the acting player's own pin list into a client-supplied order. Permutes ONLY
    /// that player's per-player list in <see cref="ScribePinStore"/> (unknown/duplicate ids ignored, omitted
    /// pins preserved), never any document's block order and never another player's list; the store already
    /// persists an ordered list under <c>scribe:pins:v1</c>, so persistence follows on the next world save.
    /// Re-pushes the acting player when the order actually changed. Public so the integration suite drives
    /// the exact production path.
    /// </summary>
    public void ReorderPinsForPlayer(IServerPlayer player, IReadOnlyList<(Guid DocId, Guid TaskId)> order)
    {
        if (sapi is null || pinStore is null) return;
        if (pinStore.ReorderPins(player.PlayerUID, order)) PushPinsTo(player);
    }

    /// <summary>
    /// Server-side write-through of a Tracker's live <c>CurrentQuantity</c> by identity, driven by a client
    /// count engine's <see cref="ScribeSetTrackerQuantityMessage"/> (add-tracker-link-tasks D5/4.3). Resolves
    /// the owning document (registry, or by scanning the acting player's inventory for an item host) and
    /// writes the clamped count lock-free via <see cref="IScribeDocumentHost.SetTrackerCurrentQuantityFromReader"/>,
    /// which marks the source dirty and resyncs every viewer. A best-effort no-op when the source is
    /// unresolvable, the task is gone, or it isn't a Tracker. No pin involvement — the count is not a pin
    /// action. Public so the integration suite can drive the exact production path.
    /// </summary>
    public void SetTrackerQuantityForPlayer(IServerPlayer player, Guid docId, Guid taskId, int quantity)
    {
        if (sapi is null) return;
        if (TryResolveDocHost(docId, out var docHost, player))
            docHost!.SetTrackerCurrentQuantityFromReader(taskId, quantity);
    }

    /// <summary>Collapses a completion policy to <see cref="ScribeCompletionPolicy.Unpin"/> when the target
    /// document is a read-only (hard/fired) tablet (zero-point-three-fixes §7.5 / D8). A read-only source can
    /// neither be reordered (<c>Sink</c>/<c>UnpinSink</c>) nor have tasks removed (<c>Delete</c>), and firing
    /// a tablet with a pinned task must not strand that pin on the HUD — so every document-mutating policy
    /// becomes a plain unpin. <c>Keep</c> is left alone (leaving the pin is a valid read-only outcome), and
    /// <c>Unpin</c> is already the target. An editable/uncapped source (<c>ReadOnly == false</c>, or an
    /// unresolvable one passed as null) keeps its policy verbatim. Read-only-ness is sourced from the host's
    /// own <see cref="ScribeDocumentPolicy.ReadOnly"/>, which <see cref="TabletHost"/> reports from the live
    /// stack's hard/fired state — no re-reading of tablet attributes here.</summary>
    private static ScribeCompletionPolicy CollapsePolicyForReadOnlySource(
        ScribeCompletionPolicy policy, IScribeDocumentHost? host)
    {
        if (host is null || !host.Policy.ReadOnly) return policy;
        return policy == ScribeCompletionPolicy.Keep ? policy : ScribeCompletionPolicy.Unpin;
    }

    /// <summary>Resolves a docId to an <see cref="IScribeDocumentHost"/>. Checks the registry first
    /// (covers Lecterns, which register server-side on Initialize). If not found, searches the acting
    /// player's inventory for a Notebook whose stored DocId matches and creates a transient host from
    /// that slot — Notebooks are only registered client-side, so the server must find them by scanning.</summary>
    private bool TryResolveDocHost(Guid docId, out IScribeDocumentHost? host,
        IServerPlayer? player = null)
    {
        if (_hostRegistry.TryGetValue(docId, out host)) return true;

        if (player is null || sapi is null) return false;

        foreach (var inv in player.InventoryManager.InventoriesOrdered)
        {
            IEnumerable<ItemSlot>? slots;
            try { slots = new List<ItemSlot>(inv); }
            catch { continue; }

            foreach (var slot in slots)
            {
                if (slot is null) continue;
                if (slot.Itemstack?.Collectible is not IScribeDocumentItem) continue;
                if (!ScribeDocumentAttributes.TryReadFrom(slot.Itemstack, out var doc) || doc is null) continue;
                if (doc.DocId != docId) continue;
                // Construct the tier-correct host (TabletHost for a tablet) so its policy/title apply
                // server-side too; both derive from NotebookHost, so the write-through path is identical.
                var nbHost = slot.Itemstack.Collectible is ItemScribeTablet
                    ? new TabletHost(slot)
                    : new NotebookHost(slot);
                nbHost.AttachServerContext(sapi, player);
                host = nbHost;
                return true;
            }
        }
        return false;
    }

    /// <summary>Completes (toggles) an UNPINNED document task straight on the shared source — a plain
    /// checkbox on a task nobody has pinned. There is no pin, so there is no store involvement, but the
    /// completion policy still applies to the document itself (scribe-lectern-view-consistency): on a
    /// transition INTO done, <c>Sink</c> moves the task to the document bottom and <c>Delete</c> removes
    /// it from the source; <c>Keep</c>/<c>Unpin</c> just toggle (there is nothing to unpin). A no-op when
    /// the source is unresolvable (nothing to toggle without a document).</summary>
    private void CompleteUnpinnedTaskAtSource(IServerPlayer player, Guid docId, Guid taskId,
        ScribeCompletionPolicy policy)
    {
        if (!TryResolveDocHost(docId, out var docHost, player))
        {
            Trace("  complete(unpinned): doc {0} unresolvable — nothing to toggle", docId);
            return;
        }
        var block = docHost!.Document.FindByTaskId(taskId);
        if (block is null || !block.IsCompletable)
        {
            Trace("  complete(unpinned): task {0} not found in doc {1}", taskId, docId);
            return;
        }
        bool nowDone = !block.Done;
        docHost.SetTaskDoneFromReader(taskId, nowDone);
        Trace("  complete(unpinned): task {0} toggled to done={1}", taskId, nowDone);

        // A read-only (hard/fired) tablet can't be reordered or have tasks removed; collapse every
        // document-mutating policy to Unpin (zero-point-three-fixes §7.5). With no pin here that leaves a
        // plain toggle, so a hardened tablet's checkbox flips done-state without disturbing the document.
        policy = CollapsePolicyForReadOnlySource(policy, docHost);

        // Apply the policy on the shared document — only on a transition INTO done (unchecking never moves
        // or removes). No pin to unpin here (Decision.ShouldRemovePin is irrelevant — there is no pin), so
        // only the document action matters; dispatch it through the write-through. Shared decision table
        // (reconcile-animating-surfaces D9), so this path can never drift from the pinned/editor paths.
        var decision = ScribeCompletion.Decide(nowDone, policy);
        switch (decision.DocAction)
        {
            case ScribeCompletionDocAction.SinkToBottom:
                if (docHost.MoveTaskToBottomFromReader(taskId))
                    Trace("  policy {0}(unpinned): moved task {1} to bottom of source doc {2}", policy, taskId, docId);
                break;
            case ScribeCompletionDocAction.Delete:
                if (docHost.DeleteTaskFromReader(taskId))
                    Trace("  policy Delete(unpinned): removed task {0} from source doc {1}", taskId, docId);
                break;
        }
    }

    /// <summary>Re-push a single player their own full pin set (server → client). Called on join and
    /// after any change to that player's set. Only ever sends a player their own pins.</summary>
    public void PushPinsTo(IServerPlayer player)
    {
        if (sapi is null || pinStore is null) return;
        var bytes = ScribePinCodec.SerializeList(pinStore.Get(player.PlayerUID));
        sapi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribePinnedSetMessage { PinnedRefBytes = bytes }, player);
    }

    /// <summary>Re-push each listed player their own pin set. The block entity calls this after a
    /// snapshot refresh / orphan sweep affecting several players. A uid that isn't a currently-online
    /// player is skipped (their set is already persisted and will sync on their next join).</summary>
    public void PushPinsTo(IReadOnlyList<string> playerUids)
    {
        if (sapi is null) return;
        foreach (var uid in playerUids)
        {
            if (sapi.World.PlayerByUid(uid) is IServerPlayer player) PushPinsTo(player);
        }
    }

}
