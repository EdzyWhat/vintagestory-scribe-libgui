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
        if (TryResolveHost(message.DocIdBytes) is BlockEntityScribeLectern lectern)
        {
            lectern.HandleServerReply(message);
        }
    }

    private void OnServerReceivedEdit(IServerPlayer fromPlayer, ScribeEditDocumentMessage message)
    {
        if (sapi is null) return;
        if (TryResolveHost(message.DocIdBytes) is BlockEntityScribeLectern lectern)
        {
            if (!lectern.ApplyEdit(fromPlayer, message.DocumentBytes))
            {
                lectern.SendSaveFailedAck(sapi, fromPlayer);
            }
        }
    }

    private void OnServerReceivedReleaseLock(IServerPlayer fromPlayer, ScribeReleaseLockMessage message)
    {
        if (sapi is null) return;
        if (TryResolveHost(message.DocIdBytes) is BlockEntityScribeLectern lectern)
        {
            lectern.ReleaseLock(fromPlayer.PlayerUID);
        }
    }

    private void OnServerReceivedRequestAccess(IServerPlayer fromPlayer, ScribeRequestAccessMessage message)
    {
        if (sapi is null) return;
        if (TryResolveHost(message.DocIdBytes) is BlockEntityScribeLectern lectern)
        {
            lectern.OnRequestAccess(fromPlayer, message.WantEditor, message.QuickAdd);
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
        SetPinForPlayer(fromPlayer, docId, taskId, message.Pinned, message.SnapshotText, message.SnapshotDone);
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
        Trace("complete-task received from {0}: doc={1} task={2} policy={3}", fromPlayer.PlayerName, docId, taskId, policy);
        CompleteTaskForPlayer(fromPlayer, docId, taskId, policy);
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
        Trace("delete-task received from {0}: doc={1} task={2}", fromPlayer.PlayerName, docId, taskId);
        DeleteTaskForPlayer(fromPlayer, docId, taskId);
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

    private void OnServerReceivedRecordVisitor(IServerPlayer fromPlayer, ScribeRecordVisitorMessage message)
    {
        if (sapi is null) return;
        if (TryResolveHost(message.DocIdBytes) is BlockEntityScribeLectern lectern)
            lectern.RecordVisitor(sapi, fromPlayer);
    }

    private void OnServerReceivedEditGuestbookNote(IServerPlayer fromPlayer, ScribeEditGuestbookNoteMessage message)
    {
        if (sapi is null) return;
        if (TryResolveHost(message.DocIdBytes) is BlockEntityScribeLectern lectern)
            lectern.UpdateGuestbookNote(sapi, fromPlayer, message.InGameDate ?? "", message.Note ?? "");
    }

    private void OnClientReceivedGuestbookSync(ScribeGuestbookSyncMessage message)
    {
        if (capi is null) return;
        if (TryResolveHost(message.DocIdBytes) is BlockEntityScribeLectern lectern)
            lectern.ApplyGuestbookSync(message.GuestbookBytes);
    }

    private void OnServerReceivedNotebookSave(IServerPlayer fromPlayer, ScribeNotebookSaveMessage message)
    {
        if (sapi is null || !TryReadGuid(message.DocIdBytes, out var docId)) return;
        // The dialog closes (and flushes) the moment the notebook leaves the active hand slot,
        // so by the time this packet arrives the item should still be there. Both notebook item
        // classes flush through this handler, so accept the Clockmaker's Notebook too — otherwise
        // its task/note edits are silently dropped server-side.
        var slot = fromPlayer.Entity?.ActiveHandItemSlot;
        if (slot?.Itemstack?.Collectible is not IScribeDocumentItem) return;
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
    /// Resolves the notebook by the ACTIVE-HAND slot (the slot the player right-clicked to open), NOT by
    /// DocId: a freshly picked-up notebook has never synced a document, so the server stack carries no
    /// DocId to match against — the message's DocId is only a loose hint here. Recording is history-only
    /// (<see cref="NotebookHost.TryRecordPickedUpOnSlot"/>) so we never stamp a server-random document
    /// that <see cref="OnServerReceivedNotebookSave"/> would later reject the owner's edits against.</summary>
    private void OnServerReceivedNotebookOpened(IServerPlayer fromPlayer, ScribeNotebookOpenedMessage message)
    {
        if (sapi is null) return;
        var slot = fromPlayer.Entity?.ActiveHandItemSlot;
        if (slot?.Itemstack?.Collectible is not IScribeDocumentItem) return;

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

}
