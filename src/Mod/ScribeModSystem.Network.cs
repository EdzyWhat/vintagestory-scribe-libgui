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
    /// Pin/unpin by (DocId, TaskId); see <see cref="SetPinForPlayer"/>. Only the affected player is re-pushed.
    /// </summary>
    private void OnServerReceivedSetPin(IServerPlayer fromPlayer, ScribeSetPinMessage message)
    {
        if (!TryReadGuid(message.DocId, out var docId) || !TryReadGuid(message.TaskId, out var taskId))
        {
            Trace("set-pin from {0}: MALFORMED packet (docId/taskId not 16 bytes) — ignored", fromPlayer.PlayerName);
            return;
        }
        Trace("set-pin received from {0}: pinned={1} doc={2} task={3}", fromPlayer.PlayerName, message.Pinned, docId, taskId);
        var insertEdge = ScribePlayerSettings.NormalizePinInsert((Scribe.Core.ScribePinInsert)message.PinInsert);
        SetPinForPlayer(fromPlayer, docId, taskId, message.Pinned, message.SnapshotText, message.SnapshotDone,
            (Scribe.Core.ScribeBlockKind)message.SnapshotKind, message.SnapshotLinkTarget,
            message.SnapshotTargetItemCode, message.SnapshotTargetQuantity, message.SnapshotCurrentQuantity,
            message.SnapshotLinkLabel, message.SnapshotDepth, insertEdge);
    }

    private void OnServerReceivedCompleteTask(IServerPlayer fromPlayer, ScribeCompleteTaskMessage message)
    {
        if (!TryReadGuid(message.DocId, out var docId) || !TryReadGuid(message.TaskId, out var taskId))
        {
            Trace("complete-task from {0}: MALFORMED packet (docId/taskId not 16 bytes) — ignored", fromPlayer.PlayerName);
            return;
        }
        // The completion policy is a client preference carried in the packet; an unknown/hostile byte normalizes to Sink.
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

    private void OnServerReceivedSetQuestObjectiveProgress(IServerPlayer fromPlayer, ScribeSetQuestObjectiveProgressMessage message)
    {
        if (!TryReadGuid(message.DocId, out var docId) || !TryReadGuid(message.TaskId, out var taskId))
        {
            Trace("set-questobjective-progress from {0}: MALFORMED packet (docId/taskId not 16 bytes) — ignored", fromPlayer.PlayerName);
            return;
        }
        SetQuestObjectiveProgressForPlayer(fromPlayer, docId, taskId, message.Quantity);
    }

    /// <summary>Client → server: persist a Read View filter pill + subtask-group collapse change by DocId. A malformed DocId is a silent no-op; malformed collapsed-group ids are dropped individually.</summary>
    private void OnServerReceivedSetReadViewState(IServerPlayer fromPlayer, ScribeSetReadViewStateMessage message)
    {
        if (!TryReadGuid(message.DocId, out var docId))
        {
            Trace("set-read-view-state from {0}: MALFORMED packet (docId not 16 bytes) — ignored", fromPlayer.PlayerName);
            return;
        }
        var collapsedIds = new List<Guid>();
        if (message.CollapsedGroupIds is not null)
        {
            foreach (var idBytes in message.CollapsedGroupIds)
            {
                if (TryReadGuid(idBytes, out var id)) collapsedIds.Add(id);
            }
        }
        SetReadViewStateForPlayer(fromPlayer, docId, message.FilterCategory, collapsedIds);
    }

    private void OnServerReceivedReorderPins(IServerPlayer fromPlayer, ScribeReorderPinsMessage message)
    {
        // Both lists must be present, equal length, and bounded so a hostile payload can't drive an
        // unbounded permute; malformed pairs are dropped.
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

    /// <summary>Client → server: Transcribe copy within a Scriptorium. Server-authoritative: clones the Original
    /// slot's document with a FRESH identity (so the two items never collide on pins/doc resolution) onto the
    /// Duplicate slot. Rejects bad slot indices, non-document slots, and an overwrite of a non-empty target
    /// without <see cref="ScribeTranscribeCopyMessage.AllowOverwrite"/> (the client's two-press confirm is only UX).</summary>
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

        if (sourceSlot.Itemstack is null || targetSlot.Itemstack is null) return;
        if (!ScribeDocumentAttributes.TryReadFrom(sourceSlot.Itemstack, out var sourceDoc) || sourceDoc is null)
            return;

        // Needed for the overwrite gate and for append apply/capacity.
        ScribeDocumentAttributes.TryReadFrom(targetSlot.Itemstack, out var targetDoc);

        // Overwrite gate (overwrite mode only; append is non-destructive): a non-empty target needs the confirming press.
        if (!message.Append
            && !message.AllowOverwrite
            && targetDoc is not null && targetDoc.CompletableCount > 0)
        {
            Trace("transcribe-copy from {0}: target has {1} tasks and overwrite not allowed — no-op",
                fromPlayer.PlayerName, targetDoc.CompletableCount);
            return;
        }

        // Target must be writeable (not a hardened/fired tablet) and able to hold the RESULTING block count
        // (overwrite: source's; append: target's plus source's; a wet tablet caps at 10). CanHold also denies
        // read-only policies. Mirrors the client's BuildSealButton gate but does not trust it.
        int resultingBlocks = message.Append
            ? (targetDoc?.BlockCount ?? 0) + sourceDoc.BlockCount
            : sourceDoc.BlockCount;
        // A non-document target can't reach here (Scribe-only slots), so a missing item is treated as uncapped/writeable.
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
            // Keep the target's document (identity, title, tasks) and append fresh-identity copies; no document yet starts empty.
            result = targetDoc ?? new ScribeDocument();
            result.AppendClonedBlocksFrom(sourceDoc);
        }
        else
        {
            // Replace the target with a clone that has a fresh DocId and per-block TaskIds, so the copy is independent.
            result = sourceDoc.CloneWithNewIdentity();
        }
        ScribeDocumentAttributes.WriteTo(targetSlot.Itemstack, result);
        targetSlot.MarkDirty();
        scriptorium.MarkDirty(true);
        // Replay the COPIED flourish on every OTHER open dialog on this block; the actor already stamped locally.
        BroadcastTranscribeStamp(pos, message.TargetSlot, imported: false, exceptPlayer: fromPlayer);
        Trace("transcribe-copy from {0}: {1} doc onto slot {2} ({3} tasks total)",
            fromPlayer.PlayerName, message.Append ? "appended" : "copied", message.TargetSlot, result.CompletableCount);
    }

    /// <summary>Client → server: import a client-parsed JSON document onto the Scriptorium's Import/Export slot.
    /// Sibling of <see cref="OnServerReceivedTranscribeCopy"/> with the same addressing and Overwrite/Append
    /// gating, but the source is a JSON string.
    /// <para><b>Never pins.</b> <see cref="Scribe.Core.ScribeDocumentJsonCodec.TryDeserialize"/> mints fresh
    /// TaskIds (and a fresh DocId; append keeps the target's own), so no pin can be created or resurrected, and
    /// this handler makes no pin-store write.</para></summary>
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

        // The codec is the real guard: it enforces block/text caps, degrades unknown kinds, and mints fresh
        // ids; a bad payload fails to parse and the import is a no-op.
        if (!ScribeDocumentJsonCodec.TryDeserialize(message.DocumentJson, out var incoming) || incoming is null)
        {
            Trace("transcribe-import from {0}: payload did not parse as a Scribe document — ignored", fromPlayer.PlayerName);
            return;
        }

        // Needed for the overwrite gate and for append apply/capacity.
        ScribeDocumentAttributes.TryReadFrom(targetSlot.Itemstack, out var targetDoc);

        // Overwrite gate, as in the copy path.
        if (!message.Append
            && !message.AllowOverwrite
            && targetDoc is not null && targetDoc.CompletableCount > 0)
        {
            Trace("transcribe-import from {0}: target has {1} tasks and overwrite not allowed — no-op",
                fromPlayer.PlayerName, targetDoc.CompletableCount);
            return;
        }

        // Valid-destination check, as in the copy path (overwrite → incoming's blocks; append → target's + incoming's).
        int resultingBlocks = message.Append
            ? (targetDoc?.BlockCount ?? 0) + incoming.BlockCount
            : incoming.BlockCount;
        // A non-document target can't reach here (Scribe-only slots), so a missing item is treated as uncapped/writeable.
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
            // Keep the target's document and append the imported tasks (AppendClonedBlocksFrom mints fresh TaskIds); empty target → plain import.
            result = targetDoc ?? new ScribeDocument();
            result.AppendClonedBlocksFrom(incoming);
        }
        else
        {
            // Replace the target; the deserialized document already has a fresh DocId and TaskIds.
            result = incoming;
        }
        ScribeDocumentAttributes.WriteTo(targetSlot.Itemstack, result);
        targetSlot.MarkDirty();
        scriptorium.MarkDirty(true);
        // Replay the IMPORTED flourish on every OTHER open dialog.
        BroadcastTranscribeStamp(pos, message.TargetSlot, imported: true, exceptPlayer: fromPlayer);
        Trace("transcribe-import from {0}: {1} document onto slot {2} ({3} tasks total)",
            fromPlayer.PlayerName, message.Append ? "appended" : "imported", message.TargetSlot, result.CompletableCount);
    }

    /// <summary>Broadcasts a watcher-stamp cue to every online player except the actor; called only after the write + <c>MarkDirty</c> committed, so each stamp matches a real change.</summary>
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

    /// <summary>Resolves the slot an item-hosted Scribe packet targets, decided once on the client: prefers the
    /// slot identity stamped on the packet (the Handbook add flow may target a carried book not in hand), falling
    /// back to the active hand for legacy packets. Null unless the slot holds an <see cref="IScribeDocumentItem"/>.
    /// Does NOT check writeability (open/history targets read-only tablets too); mutating callers add that guard.</summary>
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

    /// <summary><c>internal</c> solely so the integration suite (InternalsVisibleTo) can simulate an editor autosave flush without a real packet.</summary>
    internal void OnServerReceivedNotebookSave(IServerPlayer fromPlayer, ScribeNotebookSaveMessage message)
    {
        if (sapi is null || !TryReadGuid(message.DocIdBytes, out var docId)) return;
        // Write to the exact slot the dialog edited, not the active hand: a Handbook "Add to Scribe" add can
        // open a carried book that isn't in hand, and the hand might hold a read-only tablet.
        // Notebook, Clockmaker's Notebook and Tablet all flush through here.
        var slot = ResolveItemPacketSlot(fromPlayer, message.TargetInventoryId, message.TargetSlotId);
        if (slot?.Itemstack?.Collectible is not IScribeDocumentItem item) return;
        // Never write onto a read-only (hardened/fired) tablet, even via the legacy active-hand path.
        if (!item.IsSlotWriteable(slot))
        {
            Trace("notebook-save from {0}: target slot holds a read-only Scribe item ({1}) — refusing write",
                fromPlayer.PlayerName, slot.Itemstack!.Collectible.Code);
            return;
        }
        // The packet's DocId must match the stack's existing document; a fresh stack has none, so allow it.
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
            // The Editor's checkbox has no completion message; this whole-document flush persists Done, so derive assignment Completed here.
            NotifyDoneAssignmentsInDocument(doc);
        }

        // Echo back so the dialog's HandleServerReply can update the client's authoritative copy.
        sapi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeNotebookSaveMessage
        {
            DocIdBytes = message.DocIdBytes,
            DocumentBytes = message.DocumentBytes,
        }, fromPlayer);
    }

    /// <summary>Client → server: the player opened a Notebook. Records their one-time PickedUp entry
    /// (deduplicated per actor in <c>HistoryStore.TryAddEntry</c>, skipped for the crafter). Opening is otherwise
    /// client-only, so without this no PickedUp entry exists.
    ///
    /// Resolves the book by the packet's slot identity, falling back to the active hand for legacy packets.
    /// Deliberately NOT by DocId: a freshly picked-up notebook has no synced document, so the DocId is only a
    /// loose hint. Recording is history-only (<see cref="NotebookHost.TryRecordPickedUpOnSlot"/>) so we never
    /// stamp a server-random document that <see cref="OnServerReceivedNotebookSave"/> would later reject the
    /// owner's edits against.</summary>
    private void OnServerReceivedNotebookOpened(IServerPlayer fromPlayer, ScribeNotebookOpenedMessage message)
    {
        if (sapi is null) return;
        var slot = ResolveItemPacketSlot(fromPlayer, message.TargetInventoryId, message.TargetSlotId);
        if (slot is null) return;

        // Last-opened tracker; independent of whether a history entry is recorded below.
        if (TryReadGuid(message.DocIdBytes, out var openedDocId))
            NoteServerScribeItemOpened(fromPlayer.PlayerUID, openedDocId);

        var historyBytes = NotebookHost.TryRecordPickedUpOnSlot(sapi, slot, fromPlayer);
        if (historyBytes is null) return; // crafter, or this player already has a PickedUp entry

        // Push the new history so an open dialog refreshes its History tab (null DocumentBytes leaves the document untouched).
        sapi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeNotebookSaveMessage
        {
            DocIdBytes   = message.DocIdBytes,
            HistoryBytes = historyBytes,
        }, fromPlayer);
    }

    /// <summary>Resolves the slot a manual-history-entry packet targets and attaches a server-bound
    /// <see cref="NotebookHost"/>, but only for the Notebook and Clockmaker's Notebook. The Tablet is excluded
    /// (custom History entries are Notebook-family only); a legitimate client never sends these for a Tablet,
    /// so this guards against one that tries.</summary>
    private NotebookHost? ResolveManualEntryHost(IServerPlayer fromPlayer, string? targetInventoryId, int targetSlotId)
    {
        if (sapi is null) return null;
        var slot = ResolveItemPacketSlot(fromPlayer, targetInventoryId, targetSlotId);
        if (slot?.Itemstack?.Collectible is not (ItemScribeNotebook or ItemClockmakerNotebook)) return null;

        var host = new NotebookHost(slot);
        host.AttachServerContext(sapi, fromPlayer);
        return host;
    }

    private void OnServerReceivedAddHistoryEntry(IServerPlayer fromPlayer, ScribeAddHistoryEntryMessage message)
    {
        if (!TryReadGuid(message.EntryId, out var entryId)) return;
        var host = ResolveManualEntryHost(fromPlayer, message.TargetInventoryId, message.TargetSlotId);
        host?.AddManualEntry(sapi!, fromPlayer, entryId, (message.Text ?? "").Trim());
    }

    private void OnServerReceivedSetHistoryEntryText(IServerPlayer fromPlayer, ScribeSetHistoryEntryTextMessage message)
    {
        if (!TryReadGuid(message.EntryId, out var entryId)) return;
        var host = ResolveManualEntryHost(fromPlayer, message.TargetInventoryId, message.TargetSlotId);
        host?.SetManualEntryText(fromPlayer, entryId, (message.Text ?? "").Trim());
    }

    private void OnServerReceivedDeleteHistoryEntry(IServerPlayer fromPlayer, ScribeDeleteHistoryEntryMessage message)
    {
        if (!TryReadGuid(message.EntryId, out var entryId)) return;
        var host = ResolveManualEntryHost(fromPlayer, message.TargetInventoryId, message.TargetSlotId);
        host?.DeleteManualEntry(fromPlayer, entryId);
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
                if (capi.Gui.OpenedGuis.OfType<GuiDialogScribeNotebook>()
                        .FirstOrDefault(d => d.IsOpened()) is { } dialog)
                    dialog.RefreshHistoryView();
            }
        }
    }

    /// <summary>Server → client: another player's copy/import wrote to a Scriptorium; if our own dialog is open
    /// on that block, replay the IMPRINT flourish. The slot item itself arrives via the standard inventory
    /// resync. No-op when not watching.</summary>
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
