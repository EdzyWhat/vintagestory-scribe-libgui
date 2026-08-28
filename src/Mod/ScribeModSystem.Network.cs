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
    private void OnClientReceivedEditReply(ScribeEditDocumentMessage message)
    {
        if (capi is null) return;
        if (TryResolveHost(message.DocIdBytes) is BlockEntityScribeWritingStation station)
        {
            station.HandleServerReply(message);
        }
    }

    private void OnServerReceivedEdit(IServerPlayer fromPlayer, ScribeEditDocumentMessage message)
    {
        if (sapi is null) return;
        if (TryResolveHost(message.DocIdBytes) is BlockEntityScribeWritingStation station)
        {
            if (!station.ApplyEdit(fromPlayer, message.DocumentBytes))
            {
                station.SendSaveFailedAck(sapi, fromPlayer);
            }
        }
    }

    private void OnServerReceivedReleaseLock(IServerPlayer fromPlayer, ScribeReleaseLockMessage message)
    {
        if (sapi is null) return;
        if (TryResolveHost(message.DocIdBytes) is BlockEntityScribeWritingStation station)
        {
            station.ReleaseLock(fromPlayer.PlayerUID);
        }
    }

    private void OnServerReceivedRequestAccess(IServerPlayer fromPlayer, ScribeRequestAccessMessage message)
    {
        if (sapi is null) return;
        if (TryResolveHost(message.DocIdBytes) is BlockEntityScribeWritingStation station)
        {
            station.OnRequestAccess(fromPlayer, message.WantEditor, message.QuickAdd);
        }
    }

    /// <summary>
    /// Pin/unpin, addressed by (DocId, TaskId). An UNPIN removes straight from the store with no block
    /// resolution, so it works when the owning lectern is broken or its chunk is unloaded. A PIN
    /// resolves the owning block via the live index only to snapshot the task's text/done from the
    /// server's own authoritative document (never a client-supplied snapshot); if the document can't be
    /// resolved right now the pin is still recorded with an empty snapshot. Lock-free throughout. Only
    /// the affected player is re-pushed.
    /// </summary>
    private void OnServerReceivedSetPin(IServerPlayer fromPlayer, ScribeSetPinMessage message)
    {
        if (!TryReadGuid(message.DocId, out var docId) || !TryReadGuid(message.TaskId, out var taskId))
        {
            Trace("set-pin from {0}: MALFORMED packet (docId/taskId not 16 bytes) — ignored", fromPlayer.PlayerName);
            return;
        }
        Trace("set-pin received from {0}: pinned={1} doc={2} task={3}", fromPlayer.PlayerName, message.Pinned, docId, taskId);
        SetPinForPlayer(fromPlayer, docId, taskId, message.Pinned, message.SnapshotText, message.SnapshotDone,
            (Scribe.Core.ScribeBlockKind)message.SnapshotKind, message.SnapshotLinkTarget,
            message.SnapshotTargetItemCode, message.SnapshotTargetQuantity, message.SnapshotCurrentQuantity,
            message.SnapshotLinkLabel, message.SnapshotDepth);
    }

    private void OnServerReceivedCompleteTask(IServerPlayer fromPlayer, ScribeCompleteTaskMessage message)
    {
        if (!TryReadGuid(message.DocId, out var docId) || !TryReadGuid(message.TaskId, out var taskId))
        {
            Trace("complete-task from {0}: MALFORMED packet (docId/taskId not 16 bytes) — ignored", fromPlayer.PlayerName);
            return;
        }
        // The completion policy is a client-local preference carried in the packet; normalize an
        // unknown/hostile byte back to the safe default before applying (Sink).
        var policy = ScribePlayerSettings.NormalizePolicy((ScribeCompletionPolicy)message.Policy);
        var behavior = ScribePlayerSettings.NormalizeSubtaskBehavior((ScribeSubtaskBehavior)message.SubtaskBehavior);
        Trace("complete-task received from {0}: doc={1} task={2} policy={3} subtask={4}", fromPlayer.PlayerName, docId, taskId, policy, behavior);
        CompleteTaskForPlayer(fromPlayer, docId, taskId, policy, behavior);
    }

    private void OnServerReceivedEditPinnedTask(IServerPlayer fromPlayer, ScribeEditPinnedTaskMessage message)
    {
        if (!TryReadGuid(message.DocId, out var docId) || !TryReadGuid(message.TaskId, out var taskId))
        {
            Trace("edit-pinned from {0}: MALFORMED packet (docId/taskId not 16 bytes) — ignored", fromPlayer.PlayerName);
            return;
        }
        Trace("edit-pinned received from {0}: doc={1} task={2}", fromPlayer.PlayerName, docId, taskId);
        EditPinnedTaskForPlayer(fromPlayer, docId, taskId, message.Text ?? "");
    }

    private void OnServerReceivedDeleteTask(IServerPlayer fromPlayer, ScribeDeleteTaskMessage message)
    {
        if (!TryReadGuid(message.DocId, out var docId) || !TryReadGuid(message.TaskId, out var taskId))
        {
            Trace("delete-task from {0}: MALFORMED packet (docId/taskId not 16 bytes) — ignored", fromPlayer.PlayerName);
            return;
        }
        var behavior = ScribePlayerSettings.NormalizeSubtaskBehavior((ScribeSubtaskBehavior)message.SubtaskBehavior);
        Trace("delete-task received from {0}: doc={1} task={2} subtask={3}", fromPlayer.PlayerName, docId, taskId, behavior);
        DeleteTaskForPlayer(fromPlayer, docId, taskId, behavior);
    }

    private void OnServerReceivedSetTrackerQuantity(IServerPlayer fromPlayer, ScribeSetTrackerQuantityMessage message)
    {
        if (!TryReadGuid(message.DocId, out var docId) || !TryReadGuid(message.TaskId, out var taskId))
        {
            Trace("set-tracker-qty from {0}: MALFORMED packet (docId/taskId not 16 bytes) — ignored", fromPlayer.PlayerName);
            return;
        }
        SetTrackerQuantityForPlayer(fromPlayer, docId, taskId, message.Quantity);
    }

    private void OnServerReceivedReorderPins(IServerPlayer fromPlayer, ScribeReorderPinsMessage message)
    {
        // Validate the parallel id lists: both present, equal length, and bounded so a hostile/oversized
        // payload can't drive an unbounded permute. Each entry must be a well-formed 16-byte Guid pair;
        // any malformed/unknown entry is dropped by the store's reorder (unknown ids are ignored).
        var docIds = message.DocIds;
        var taskIds = message.TaskIds;
        if (docIds is null || taskIds is null || docIds.Count != taskIds.Count
            || docIds.Count > ScribePinCodec.MaxPinsPerPlayer)
        {
            Trace("reorder-pins from {0}: MALFORMED packet (null/mismatched/oversized id lists) — ignored", fromPlayer.PlayerName);
            return;
        }

        var order = new List<(Guid, Guid)>(docIds.Count);
        for (int i = 0; i < docIds.Count; i++)
        {
            if (TryReadGuid(docIds[i], out var docId) && TryReadGuid(taskIds[i], out var taskId))
            {
                order.Add((docId, taskId));
            }
        }
        Trace("reorder-pins received from {0}: {1} ordered ids", fromPlayer.PlayerName, order.Count);
        ReorderPinsForPlayer(fromPlayer, order);
    }

    /// <summary>Client → server: perform the Transcribe copy on a Scriptorium's inventory (D2). The copy is
    /// server-authoritative — the client only requests it. We resolve the Scriptorium at the packet's block
    /// position, read the Original (source) slot's document, clone it with a FRESH identity (§1.1) so the two
    /// items never collide on pins/block-doc resolution, and write it onto the Duplicate (target) item, letting
    /// the standard inventory sync propagate. Guards: valid slot indices, both slots hold a Scribe document, and
    /// — the defensive overwrite gate — if the target already has completable contents and
    /// <see cref="ScribeTranscribeCopyMessage.AllowOverwrite"/> is false, no copy is performed (the two-press
    /// confirm is a client UX; the server is the real gate).</summary>
    private void OnServerReceivedTranscribeCopy(IServerPlayer fromPlayer, ScribeTranscribeCopyMessage message)
    {
        if (sapi is null) return;

        var pos = new Vintagestory.API.MathTools.BlockPos(message.X, message.Y, message.Z);
        if (sapi.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityScriptorium scriptorium)
        {
            Trace("transcribe-copy from {0}: no Scriptorium at {1} — ignored", fromPlayer.PlayerName, pos);
            return;
        }

        var inv = scriptorium.Inventory;
        if (message.SourceSlot < 0 || message.SourceSlot >= inv.Count
            || message.TargetSlot < 0 || message.TargetSlot >= inv.Count
            || message.SourceSlot == message.TargetSlot)
        {
            Trace("transcribe-copy from {0}: bad slot indices {1}->{2} (count {3}) — ignored",
                fromPlayer.PlayerName, message.SourceSlot, message.TargetSlot, inv.Count);
            return;
        }

        var sourceSlot = inv[message.SourceSlot];
        var targetSlot = inv[message.TargetSlot];

        // Both slots must hold a Scribe item carrying a document to read/write.
        if (sourceSlot.Itemstack is null || targetSlot.Itemstack is null) return;
        if (!ScribeDocumentAttributes.TryReadFrom(sourceSlot.Itemstack, out var sourceDoc) || sourceDoc is null)
            return;

        // The target's current document (may be null/empty). Needed both for the overwrite gate (overwrite mode)
        // and for the append apply + append capacity (append mode).
        ScribeDocumentAttributes.TryReadFrom(targetSlot.Itemstack, out var targetDoc);

        // Defensive overwrite gate — OVERWRITE MODE ONLY: if the target already has tasks and this isn't the
        // confirming press, do nothing (independent of the client's two-press UX). Append is non-destructive, so
        // it has nothing to gate.
        if (!message.Append
            && !message.AllowOverwrite
            && targetDoc is not null && targetDoc.CompletableCount > 0)
        {
            Trace("transcribe-copy from {0}: target has {1} tasks and overwrite not allowed — no-op",
                fromPlayer.PlayerName, targetDoc.CompletableCount);
            return;
        }

        // Target must be a VALID destination (add-transcribe-copy-paste refinement): writeable (not a
        // hardened/fired tablet) AND able to hold the RESULTING task blocks (a wet tablet caps at 10).
        // Overwrite mode replaces, so the result is the source's block count; append mode adds, so the result is
        // the target's existing block count PLUS the source's. DocumentPolicy.CanHold encodes writeability too —
        // a read-only tablet's policy is ReadOnly, so CanHold denies regardless of count. Non-Scribe items can't
        // reach these Scribe-only slots, so a missing IScribeDocumentItem is treated as uncapped/writeable.
        // Server-authoritative: mirrors the client's BuildSealButton gate but does not trust it.
        int resultingBlocks = message.Append
            ? (targetDoc?.BlockCount ?? 0) + sourceDoc.BlockCount
            : sourceDoc.BlockCount;
        if (targetSlot.Itemstack.Collectible is IScribeDocumentItem targetItem
            && !targetItem.DocumentPolicy(targetSlot).CanHold(resultingBlocks))
        {
            Trace("transcribe-copy from {0}: target rejects {1} resulting blocks (read-only or over its cap) — no-op",
                fromPlayer.PlayerName, resultingBlocks);
            return;
        }

        ScribeDocument result;
        if (message.Append)
        {
            // Append mode: keep the target's own document (identity + title + existing tasks) and add
            // fresh-identity copies of the source's tasks onto the end. When the target has no document yet,
            // start from an empty one so append behaves like a plain copy.
            result = targetDoc ?? new ScribeDocument();
            result.AppendClonedBlocksFrom(sourceDoc);
        }
        else
        {
            // Overwrite mode: REPLACE the target document with a clone of the source that has a fresh DocId +
            // fresh TaskId per block, so the copy is fully independent (D1).
            result = sourceDoc.CloneWithNewIdentity();
        }
        ScribeDocumentAttributes.WriteTo(targetSlot.Itemstack, result);
        targetSlot.MarkDirty();
        scriptorium.MarkDirty(true);
        // Watcher-stamp-sync: the acting client already stamped locally; tell every OTHER open dialog on this
        // shared block to play the same COPIED flourish. Broadcast-except-sender keeps the actor's stamp the
        // snappy local one (no double play) and reaches only players who are actually watching.
        BroadcastTranscribeStamp(pos, message.TargetSlot, imported: false, exceptPlayer: fromPlayer);
        Trace("transcribe-copy from {0}: {1} doc onto slot {2} ({3} tasks total)",
            fromPlayer.PlayerName, message.Append ? "appended" : "copied", message.TargetSlot, result.CompletableCount);
    }

    /// <summary>Client → server: import a document (parsed + game-validated on the client, carried as a JSON
    /// payload) onto the Scriptorium's Import/Export slot (D6). The sibling of
    /// <see cref="OnServerReceivedTranscribeCopy"/> — same block-position addressing, same Overwrite/Append +
    /// overwrite-gate semantics — but the source is a JSON string rather than another slot.
    ///
    /// <para><b>Never pins.</b> The document is rebuilt from JSON by
    /// <see cref="Scribe.Core.ScribeDocumentJsonCodec.TryDeserialize"/>, which mints a FRESH
    /// <see cref="Scribe.Core.ScribeBlock.TaskId"/> for every block (the ctor default), and an overwrite gets a
    /// fresh <c>DocId</c> too (<see cref="Scribe.Core.ScribeDocument.CloneWithNewIdentity"/> is unnecessary — a
    /// freshly deserialized document already has a new DocId, and append keeps the target's own). Because pins
    /// are a separate per-player <c>(DocId, TaskId)</c> store and these ids never existed before, no pin can be
    /// created or resurrected by an import — and this handler makes no pin-store write at all.</para></summary>
    private void OnServerReceivedTranscribeImport(IServerPlayer fromPlayer, ScribeTranscribeImportMessage message)
    {
        if (sapi is null) return;

        var pos = new Vintagestory.API.MathTools.BlockPos(message.X, message.Y, message.Z);
        if (sapi.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityScriptorium scriptorium)
        {
            Trace("transcribe-import from {0}: no Scriptorium at {1} — ignored", fromPlayer.PlayerName, pos);
            return;
        }

        var inv = scriptorium.Inventory;
        if (message.TargetSlot < 0 || message.TargetSlot >= inv.Count)
        {
            Trace("transcribe-import from {0}: bad target slot {1} (count {2}) — ignored",
                fromPlayer.PlayerName, message.TargetSlot, inv.Count);
            return;
        }

        var targetSlot = inv[message.TargetSlot];
        if (targetSlot.Itemstack is null) return; // no item to write onto

        // Rebuild the incoming document from the JSON payload. The codec is the real guard: it enforces the
        // block/text caps, degrades unknown kinds, and mints fresh ids — a malformed/hostile payload simply
        // fails to parse and the import is a no-op.
        if (!ScribeDocumentJsonCodec.TryDeserialize(message.DocumentJson, out var incoming) || incoming is null)
        {
            Trace("transcribe-import from {0}: payload did not parse as a Scribe document — ignored", fromPlayer.PlayerName);
            return;
        }

        // The target's current document (may be null/empty) — needed for the overwrite gate (overwrite mode)
        // and for the append apply + append capacity (append mode).
        ScribeDocumentAttributes.TryReadFrom(targetSlot.Itemstack, out var targetDoc);

        // Defensive overwrite gate — OVERWRITE MODE ONLY: mirrors the copy path. If the target already has tasks
        // and this isn't the confirming press, do nothing. Append is non-destructive, so it has nothing to gate.
        if (!message.Append
            && !message.AllowOverwrite
            && targetDoc is not null && targetDoc.CompletableCount > 0)
        {
            Trace("transcribe-import from {0}: target has {1} tasks and overwrite not allowed — no-op",
                fromPlayer.PlayerName, targetDoc.CompletableCount);
            return;
        }

        // Valid-destination check, same as the copy path: writeable (not a hardened/fired tablet) AND able to
        // hold the RESULTING block count (overwrite → incoming's blocks; append → target's + incoming's).
        int resultingBlocks = message.Append
            ? (targetDoc?.BlockCount ?? 0) + incoming.BlockCount
            : incoming.BlockCount;
        if (targetSlot.Itemstack.Collectible is IScribeDocumentItem targetItem
            && !targetItem.DocumentPolicy(targetSlot).CanHold(resultingBlocks))
        {
            Trace("transcribe-import from {0}: target rejects {1} resulting blocks (read-only or over its cap) — no-op",
                fromPlayer.PlayerName, resultingBlocks);
            return;
        }

        ScribeDocument result;
        if (message.Append)
        {
            // Append mode: keep the target's own document (identity + title + existing tasks) and add the
            // imported tasks onto the end. AppendClonedBlocksFrom mints fresh TaskIds for the added blocks; the
            // incoming blocks already carry fresh ids, so either way nothing pins. Empty target → plain import.
            result = targetDoc ?? new ScribeDocument();
            result.AppendClonedBlocksFrom(incoming);
        }
        else
        {
            // Overwrite mode: REPLACE the target with the imported document. It already has a fresh DocId + fresh
            // per-block TaskIds from deserialization, so the result is fully independent and unpinned.
            result = incoming;
        }
        ScribeDocumentAttributes.WriteTo(targetSlot.Itemstack, result);
        targetSlot.MarkDirty();
        scriptorium.MarkDirty(true);
        // Watcher-stamp-sync: mirror the copy path — replay the IMPORTED flourish on every OTHER open dialog.
        BroadcastTranscribeStamp(pos, message.TargetSlot, imported: true, exceptPlayer: fromPlayer);
        Trace("transcribe-import from {0}: {1} document onto slot {2} ({3} tasks total)",
            fromPlayer.PlayerName, message.Append ? "appended" : "imported", message.TargetSlot, result.CompletableCount);
    }

    /// <summary>Broadcast a watcher-stamp cue to every online player EXCEPT the actor (whose client already
    /// stamped locally). Called only after a copy/import has committed its write + <c>MarkDirty</c>, so a sent
    /// stamp always corresponds to a real change (matching the client-side <c>PlayStamp</c> contract).</summary>
    private void BroadcastTranscribeStamp(Vintagestory.API.MathTools.BlockPos pos, int slot, bool imported, IServerPlayer exceptPlayer)
    {
        sapi?.Network.GetChannel(NetworkChannelName).BroadcastPacket(new ScribeTranscribeStampMessage
        {
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            Slot = slot,
            Imported = imported,
        }, exceptPlayer);
    }

    private void OnServerReceivedRecordVisitor(IServerPlayer fromPlayer, ScribeRecordVisitorMessage message)
    {
        if (sapi is null) return;
        if (TryResolveHost(message.DocIdBytes) is BlockEntityScribeWritingStation station)
            station.RecordVisitor(sapi, fromPlayer);
    }

    private void OnServerReceivedEditGuestbookNote(IServerPlayer fromPlayer, ScribeEditGuestbookNoteMessage message)
    {
        if (sapi is null) return;
        if (TryResolveHost(message.DocIdBytes) is BlockEntityScribeWritingStation station)
            station.UpdateGuestbookNote(sapi, fromPlayer, message.InGameDate ?? "", message.Note ?? "");
    }

    private void OnClientReceivedGuestbookSync(ScribeGuestbookSyncMessage message)
    {
        if (capi is null) return;
        if (TryResolveHost(message.DocIdBytes) is BlockEntityScribeWritingStation station)
            station.ApplyGuestbookSync(message.GuestbookBytes);
    }

    /// <summary>Resolve the exact <see cref="ItemSlot"/> an item-hosted Scribe packet targets, decided ONCE
    /// on the client and merely honored here. Prefers the slot identity the client stamped on the packet
    /// (inventory id + slot index — the surface the dialog was actually editing, which the Handbook add flow
    /// may have chosen over the active-hand item), and falls back to the active hand only for legacy packets
    /// that carry no identity. This is the single place the server answers "which slot does this item packet
    /// address"; before, <see cref="OnServerReceivedNotebookSave"/> re-derived it as the active hand and so
    /// misrouted a Handbook add onto a different in-hand item (add-tracker-link-tasks 7.16). Returns null
    /// unless the resolved slot holds an <see cref="IScribeDocumentItem"/>. Does NOT check writeability — the
    /// open/history path targets read-only tablets too; callers that mutate the document add that guard.</summary>
    private static ItemSlot? ResolveItemPacketSlot(IServerPlayer fromPlayer, string? inventoryId, int slotId)
    {
        ItemSlot? slot = null;
        if (inventoryId is not null)
        {
            var inv = fromPlayer.InventoryManager?.GetInventory(inventoryId);
            if (inv is not null && slotId >= 0 && slotId < inv.Count)
                slot = inv[slotId];
        }
        slot ??= fromPlayer.Entity?.ActiveHandItemSlot;
        return slot?.Itemstack?.Collectible is IScribeDocumentItem ? slot : null;
    }

    private void OnServerReceivedNotebookSave(IServerPlayer fromPlayer, ScribeNotebookSaveMessage message)
    {
        if (sapi is null || !TryReadGuid(message.DocIdBytes, out var docId)) return;
        // Write to the EXACT slot the client dialog was editing (its stamped identity), not the active hand:
        // a Handbook "Add to Scribe" add can open a carried book that is not in hand, and resolving by active
        // hand then wrote the document onto whatever the player happened to be holding — e.g. a read-only
        // tablet — corrupting it and dropping the task from the real target (add-tracker-link-tasks 7.16).
        // Both notebook item classes flush through this handler, so the Clockmaker's Notebook is accepted too.
        var slot = ResolveItemPacketSlot(fromPlayer, message.TargetInventoryId, message.TargetSlotId);
        if (slot?.Itemstack?.Collectible is not IScribeDocumentItem item) return;
        // A document must never land on a read-only (hardened/fired) tablet, no matter how the slot was
        // resolved — belt-and-suspenders that closes the corruption class even on the legacy active-hand path.
        if (!item.IsSlotWriteable(slot))
        {
            Trace("notebook-save from {0}: target slot holds a read-only Scribe item ({1}) — refusing write",
                fromPlayer.PlayerName, slot.Itemstack!.Collectible.Code);
            return;
        }
        // Verify the packet's DocId matches the document already in the stack (if any).
        // A fresh stack with no prior save has no stored DocId yet — allow that write.
        if (ScribeDocumentAttributes.TryReadFrom(slot.Itemstack, out var existing)
            && existing is not null && existing.DocId != docId)
            return;

        if (ScribeDocumentCodec.TryDeserialize(message.DocumentBytes, out var doc) && doc is not null)
        {
            ScribeDocumentAttributes.WriteTo(slot.Itemstack, doc);
            slot.MarkDirty();
            // Reconcile actor pins so pin snapshots stay fresh after a notebook edit.
            if (pinStore is { } store)
                PushPinsTo(store.ReconcileSnapshotsForActor(fromPlayer.PlayerUID, doc.DocId, doc));
        }

        // Echo back so the dialog's HandleServerReply can update the client's authoritative copy.
        sapi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeNotebookSaveMessage
        {
            DocIdBytes = message.DocIdBytes,
            DocumentBytes = message.DocumentBytes,
        }, fromPlayer);
    }

    /// <summary>Client → server: the player opened a Notebook. Records this player's one-time PickedUp
    /// entry — deduplicated per actor in <c>HistoryStore.TryAddEntry</c>, and skipped for the crafter,
    /// whose Crafted entry already stands in for their acquisition. Opening the dialog is otherwise a
    /// client-only action the server never sees, so without this signal no PickedUp entry is recorded
    /// (the historical gap: the recorder only ever ran on a task pin/complete round-trip or a death).
    ///
    /// Resolves the opened book by the slot identity the client stamped on the packet (the exact surface it
    /// opened — the Handbook flow can open a carried book that is not in hand), falling back to the active
    /// hand for legacy packets. Deliberately NOT by DocId: a freshly picked-up notebook has never synced a
    /// document, so the server stack carries no DocId to match against — the message's DocId is only a loose
    /// hint here. Recording is history-only (<see cref="NotebookHost.TryRecordPickedUpOnSlot"/>) so we never
    /// stamp a server-random document that <see cref="OnServerReceivedNotebookSave"/> would later reject the
    /// owner's edits against.</summary>
    private void OnServerReceivedNotebookOpened(IServerPlayer fromPlayer, ScribeNotebookOpenedMessage message)
    {
        if (sapi is null) return;
        var slot = ResolveItemPacketSlot(fromPlayer, message.TargetInventoryId, message.TargetSlotId);
        if (slot is null) return;

        var historyBytes = NotebookHost.TryRecordPickedUpOnSlot(sapi, slot, fromPlayer);
        if (historyBytes is null) return; // crafter, or this player already has a PickedUp entry

        // Push the new history to the opener's client so an open dialog refreshes its History tab
        // (DocumentBytes null → the client leaves the document untouched, updating history only).
        sapi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeNotebookSaveMessage
        {
            DocIdBytes   = message.DocIdBytes,
            HistoryBytes = historyBytes,
        }, fromPlayer);
    }

    private void OnClientReceivedNotebookSave(ScribeNotebookSaveMessage message)
    {
        if (capi is null || !TryReadGuid(message.DocIdBytes, out var docId)) return;
        if (_hostRegistry.TryGetValue(docId, out var host) && host is NotebookHost notebookHost)
        {
            if (message.DocumentBytes is not null
                && ScribeDocumentCodec.TryDeserialize(message.DocumentBytes, out var doc) && doc is not null)
                notebookHost.ApplyLocalOptimisticEdit(doc);
            if (message.HistoryBytes is not null)
            {
                notebookHost.ApplyHistoryUpdate(message.HistoryBytes);
                // Refresh the History tab if it's currently open.
                if (capi.Gui.OpenedGuis.OfType<GuiDialogScribeNotebook>()
                        .FirstOrDefault(d => d.IsOpened()) is { } dialog)
                    dialog.RefreshHistoryView();
            }
        }
    }

    /// <summary>Server → client (watcher-stamp-sync): a copy or import wrote to a Scriptorium that another
    /// player is also viewing. Find our OWN open dialog on that exact block and replay the IMPRINT flourish so
    /// the watching player sees what the acting player saw. A no-op if we have no Scriptorium dialog open on
    /// that position — the common case, since most players aren't watching. The slot item is updated separately
    /// by the standard inventory resync (the same <c>MarkDirty</c> that triggered this); the stamp is purely
    /// the visual cue over whatever the slot then shows.</summary>
    private void OnClientReceivedTranscribeStamp(ScribeTranscribeStampMessage message)
    {
        if (capi is null) return;
        if (capi.Gui.OpenedGuis.OfType<GuiDialogScribeScriptorium>()
                .FirstOrDefault(d => d.IsOpened()
                    && d.BlockPosition.X == message.X
                    && d.BlockPosition.Y == message.Y
                    && d.BlockPosition.Z == message.Z) is { } dialog)
            dialog.PlayWatcherStamp(message.Slot, message.Imported);
    }

}
