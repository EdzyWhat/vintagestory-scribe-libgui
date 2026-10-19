using System;
using System.Collections.Generic;
using System.Linq;
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
    // ── Assignment ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Client → server: send a multi-item assignment batch. Thin shell over <see cref="SendAssignmentBatch"/>.</summary>
    private void OnServerReceivedSendAssignmentBatch(IServerPlayer fromPlayer, ScribeSendAssignmentBatchMessage message)
        => SendAssignmentBatch(fromPlayer, message);

    /// <summary>Send a multi-item assignment batch. Creates one independent assignment per row via
    /// <see cref="ScribeAssignmentStore.TryCreate"/>; a malformed or store-rejected row is skipped, not fatal.
    /// With <see cref="ScribeSendAssignmentBatchMessage.DeleteFromSource"/>, created rows are then removed from
    /// the source document.
    ///
    /// <para>The server re-derives whether a Task Notice is required from its own <c>DeliveryMode</c>
    /// (<see cref="ScribeDeliveryPolicy.RequiresNotice"/>), never trusting
    /// <see cref="ScribeSendAssignmentBatchMessage.DeliveryChoice"/> alone. When required, no store record is
    /// created yet (the notice item is the pending record until Accept); every row is sealed as a
    /// <see cref="ScribeBlock"/> carrying an Unaccepted <see cref="ScribeAssignment"/> onto one notice.</para>
    ///
    /// <para>Public so the integration suite can drive it directly (as with <c>SetPinForPlayer</c>).</para></summary>
    public void SendAssignmentBatch(IServerPlayer fromPlayer, ScribeSendAssignmentBatchMessage message)
    {
        if (sapi is null || assignmentStore is null) return;

        // Online target first; otherwise the persisted known-players registry accepts an offline known player.
        string? targetUid = message.TargetPlayerUid;
        if (string.IsNullOrWhiteSpace(targetUid)
            || (sapi.World.PlayerByUid(targetUid) is null && !(knownPlayersStore?.Contains(targetUid) ?? false)))
        {
            Trace("send-assignment-batch from {0}: unknown target player uid — ignored", fromPlayer.PlayerName);
            return;
        }

        var rows = message.Rows;
        if (rows is null || rows.Count == 0)
        {
            Trace("send-assignment-batch from {0}: empty batch — ignored", fromPlayer.PlayerName);
            return;
        }

        string date = NotebookHost.FormatDate(sapi);
        double timestamp = sapi.World.Calendar.TotalDays;
        // One id per send call, shared by its rows (`date` alone would collide for two sends on one day).
        var batchId = Guid.NewGuid();

        var deliveryMode = ScribeDeliveryConfig.ReadMode(sapi);
        var deliveryChoice = (ScribeDeliveryChoice)message.DeliveryChoice;
        bool viaNotice = ScribeDeliveryPolicy.RequiresNotice(deliveryMode, deliveryChoice);

        int createdCount = viaNotice
            ? SendBatchViaNotice(fromPlayer, message, rows, targetUid, date, timestamp, batchId)
            : SendBatchViaLocalInboxes(fromPlayer, message, rows, targetUid, date, timestamp, batchId);

        if (createdCount == 0) return;
        Trace("send-assignment-batch from {0}: created {1} row(s) -> {2} (via {3})",
            fromPlayer.PlayerName, createdCount, targetUid, viaNotice ? "notice" : "local inbox");
    }

    /// <summary>Local Inboxes path: one Unaccepted store record per row, then pushes both parties' views.</summary>
    private int SendBatchViaLocalInboxes(IServerPlayer fromPlayer, ScribeSendAssignmentBatchMessage message,
        List<ScribeAssignmentBatchRow> rows, string targetUid, string date, double timestamp, Guid batchId)
    {
        if (assignmentStore is null) return 0;
        var sentSourceTaskIds = new List<Guid>();
        int createdCount = 0;

        foreach (var row in rows)
        {
            if (!TryReadGuid(row.AssignmentId, out var assignmentId))
            {
                Trace("send-assignment-batch from {0}: MALFORMED row (assignmentId not 16 bytes) — skipped", fromPlayer.PlayerName);
                continue;
            }

            if (!assignmentStore.TryCreate(assignmentId, fromPlayer.PlayerUID, targetUid, row.Text ?? "", date, out _,
                    kind: (ScribeBlockKind)row.Kind, targetItemCode: row.TargetItemCode, targetQuantity: row.TargetQuantity,
                    linkTarget: row.LinkTarget, linkLabel: row.LinkLabel, linkDescription: row.LinkDescription,
                    recipeSignature: row.RecipeSignature, depth: row.Depth, batchId: batchId,
                    assignedTimestamp: timestamp))
            {
                Trace("send-assignment-batch from {0}: row rejected (duplicate id, blank text, or store full)", fromPlayer.PlayerName);
                continue;
            }

            createdCount++;
            if (message.DeleteFromSource && TryReadGuid(row.SourceTaskId, out var sourceTaskId))
                sentSourceTaskIds.Add(sourceTaskId);
        }

        if (createdCount == 0) return 0;
        RemoveSentSourceRows(message, sentSourceTaskIds);

        PushAssignmentsTo(fromPlayer);
        if (sapi!.World.PlayerByUid(targetUid) is IServerPlayer targetPlayer) PushAssignmentsTo(targetPlayer);
        return createdCount;
    }

    /// <summary>Physical-delivery path: seals every row into one notice consumed from
    /// <see cref="BlockEntityAssignmentDesk.NoticeSupplySlotIndex"/> and placed in
    /// <see cref="BlockEntityAssignmentDesk.NoticeOutputSlotIndex"/>. Refuses the whole batch if the Desk is
    /// unresolved, the supply slot has no blank notice, or the output slot is occupied (the authoritative
    /// re-check of the client gate).</summary>
    private int SendBatchViaNotice(IServerPlayer fromPlayer, ScribeSendAssignmentBatchMessage message,
        List<ScribeAssignmentBatchRow> rows, string targetUid, string date, double timestamp, Guid batchId)
    {
        if (assignmentStore is null) return 0;
        var pos = new Vintagestory.API.MathTools.BlockPos(message.X, message.Y, message.Z);
        if (sapi!.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityAssignmentDesk desk)
        {
            Trace("send-assignment-batch from {0}: no Assignment Desk at {1} for notice delivery — ignored", fromPlayer.PlayerName, pos);
            return 0;
        }

        var supplySlot = desk.Inventory[BlockEntityAssignmentDesk.NoticeSupplySlotIndex];
        var outputSlot = desk.Inventory[BlockEntityAssignmentDesk.NoticeOutputSlotIndex];
        if (supplySlot.Itemstack?.Collectible is not ItemScribeTaskNotice)
        {
            Trace("send-assignment-batch from {0}: no blank Task Notice loaded — ignored", fromPlayer.PlayerName);
            return 0;
        }
        if (outputSlot.Itemstack is not null)
        {
            Trace("send-assignment-batch from {0}: notice output slot already occupied — ignored", fromPlayer.PlayerName);
            return 0;
        }

        var sealedDoc = new ScribeDocument();
        var sentSourceTaskIds = new List<Guid>();
        int createdCount = 0;

        foreach (var row in rows)
        {
            if (!TryReadGuid(row.AssignmentId, out var assignmentId))
            {
                Trace("send-assignment-batch from {0}: MALFORMED row (assignmentId not 16 bytes) — skipped", fromPlayer.PlayerName);
                continue;
            }
            bool isItemKind = (ScribeBlockKind)row.Kind is ScribeBlockKind.Tracker or ScribeBlockKind.Link or ScribeBlockKind.Craft;
            if (!isItemKind && string.IsNullOrWhiteSpace(row.Text)) continue;

            var assignment = new ScribeAssignment(fromPlayer.PlayerUID, date, ScribeAssignmentState.Unaccepted,
                seen: false, targetPlayerUid: targetUid, batchId: batchId)
            {
                AssignedTimestamp = timestamp,
            };
            sealedDoc.AppendAssignedBlock(new ScribeBlock((ScribeBlockKind)row.Kind, row.Text ?? "", depth: row.Depth,
                taskId: assignmentId, targetItemCode: row.TargetItemCode, targetQuantity: row.TargetQuantity,
                linkTarget: row.LinkTarget, linkLabel: row.LinkLabel, recipeSignature: row.RecipeSignature,
                linkDescription: row.LinkDescription, assignment: assignment));

            // Parallel Sent-state record with the sealed block's id: visible to the Assigner now, hidden from the
            // Assignee's Inbox until the notice reaches them (OnTaskNoticeProximityTick -> TryMarkReceived).
            assignmentStore.TryCreateSent(assignmentId, fromPlayer.PlayerUID, targetUid, row.Text ?? "", date, out _,
                kind: (ScribeBlockKind)row.Kind, targetItemCode: row.TargetItemCode, targetQuantity: row.TargetQuantity,
                linkTarget: row.LinkTarget, linkLabel: row.LinkLabel, linkDescription: row.LinkDescription,
                recipeSignature: row.RecipeSignature, depth: row.Depth, batchId: batchId,
                assignedTimestamp: timestamp);

            createdCount++;
            if (message.DeleteFromSource && TryReadGuid(row.SourceTaskId, out var sourceTaskId))
                sentSourceTaskIds.Add(sourceTaskId);
        }

        if (createdCount == 0) return 0;
        RemoveSentSourceRows(message, sentSourceTaskIds);

        // Capture the collectible before TakeOut, which nulls Itemstack when the count reaches zero.
        var noticeCollectible = supplySlot.Itemstack!.Collectible;
        supplySlot.TakeOut(1);
        supplySlot.MarkDirty();
        var notice = new ItemStack(noticeCollectible, 1);
        ScribeDocumentAttributes.WriteTo(notice, sealedDoc);
        outputSlot.Itemstack = notice;
        outputSlot.MarkDirty();
        desk.MarkDirty(true);
        // Gate for the proximity heartbeat: one notice, however many rows it carries.
        AdjustOutstandingNoticeCount(targetUid, +1);
        // Only the Assigner is pushed; the Assignee's Inbox stays silent until receipt.
        PushAssignmentsTo(fromPlayer);
        return createdCount;
    }

    /// <summary>Delete-from-source routing shared by both delivery paths.</summary>
    private void RemoveSentSourceRows(ScribeSendAssignmentBatchMessage message, List<Guid> sentSourceTaskIds)
    {
        if (!message.DeleteFromSource || sentSourceTaskIds.Count == 0) return;
        if (message.SourceIsDeskDocument)
            TryRemoveDeskOwnRows(message.X, message.Y, message.Z, sentSourceTaskIds);
        else
            TryRemoveStagedRows(message.X, message.Y, message.Z, message.StagingSlot, sentSourceTaskIds);
    }

    /// <summary>Removes the sent rows (by TaskId) from the Desk's staged item and re-syncs the slot. A no-op if
    /// anything can no longer be resolved; the assignments already exist, so only the cleanup is lost.</summary>
    private void TryRemoveStagedRows(int x, int y, int z, int stagingSlot, List<Guid> sourceTaskIdsToRemove)
    {
        if (sapi is null) return;
        var pos = new Vintagestory.API.MathTools.BlockPos(x, y, z);
        if (sapi.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityAssignmentDesk desk) return;

        var inv = desk.Inventory;
        if (stagingSlot < 0 || stagingSlot >= inv.Count) return;
        var slot = inv[stagingSlot];
        if (slot.Itemstack is null) return;
        if (!ScribeDocumentAttributes.TryReadFrom(slot.Itemstack, out var doc) || doc is null) return;

        var remaining = doc.Blocks.Where(b => !sourceTaskIdsToRemove.Contains(b.TaskId)).ToList();
        if (remaining.Count == doc.Blocks.Count) return; // nothing in this batch actually matched

        doc.ReplaceBlocks(remaining);
        ScribeDocumentAttributes.WriteTo(slot.Itemstack, doc);
        slot.MarkDirty();
    }

    /// <summary>Sibling of <see cref="TryRemoveStagedRows"/> for the Desk block entity's own document, via
    /// <see cref="BlockEntityScribeWritingStation.DeleteTaskFromReader"/>. A TaskId already deleted (e.g. by
    /// another player) is skipped, and an unresolvable block is a no-op.</summary>
    private void TryRemoveDeskOwnRows(int x, int y, int z, List<Guid> sourceTaskIdsToRemove)
    {
        if (sapi is null) return;
        var pos = new Vintagestory.API.MathTools.BlockPos(x, y, z);
        if (sapi.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityAssignmentDesk desk) return;

        foreach (var taskId in sourceTaskIdsToRemove)
            desk.DeleteTaskFromReader(taskId);
    }

    /// <summary>Client → server: request an Accept/Decline/Cancel/Discard transition. Thin shell over <see cref="ApplyAssignmentAction"/>.</summary>
    private void OnServerReceivedAssignmentAction(IServerPlayer fromPlayer, ScribeAssignmentActionMessage message)
        => ApplyAssignmentAction(fromPlayer, message);

    /// <summary>The generic transition path used by the Inbox tab on any surface (as opposed to a Task Notice's
    /// own dialog, <see cref="ApplyTaskNoticeAction"/>). The actor is derived from the authenticated sender and
    /// the transition re-validated by <see cref="ScribeAssignmentStore.TryApplyAction"/>; the wire action byte
    /// is never trusted as authorization. A successful Accept also runs
    /// <see cref="TryPlaceAcceptedAssignment"/>.
    ///
    /// <para>Public so the integration suite can drive it directly (Atlas does not round-trip network packets).</para></summary>
    public void ApplyAssignmentAction(IServerPlayer fromPlayer, ScribeAssignmentActionMessage message)
    {
        if (sapi is null || assignmentStore is null) return;
        if (!TryReadGuid(message.AssignmentId, out var assignmentId))
        {
            Trace("assignment-action from {0}: MALFORMED packet (assignmentId not 16 bytes) — ignored", fromPlayer.PlayerName);
            return;
        }

        var record = assignmentStore.TryGet(assignmentId);
        if (record?.Assignment is null)
        {
            Trace("assignment-action from {0}: unknown assignment {1} — ignored", fromPlayer.PlayerName, assignmentId);
            return;
        }
        string assignerUid = record.Assignment.AssignerUid;
        string targetUid = record.Assignment.TargetPlayerUid;

        var action = (ScribeAssignmentAction)message.Action;
        if (!assignmentStore.TryApplyAction(assignmentId, fromPlayer.PlayerUID, action))
        {
            Trace("assignment-action from {0}: illegal transition {1} on {2} — ignored", fromPlayer.PlayerName, action, assignmentId);
            return;
        }
        StampTransitionDate(record.Assignment, NotebookHost.FormatDate(sapi), sapi.World.Calendar.TotalDays);

        if (action == ScribeAssignmentAction.Accept) TryPlaceAcceptedAssignment(fromPlayer, record, message);

        // This path doesn't know whether the record arrived as a physical notice; consume any still-live one
        // so it isn't left stale and interactive.
        ConsumeMatchingTaskNotice(assignmentId, targetUid);

        Trace("assignment-action from {0}: {1} on {2} -> {3}", fromPlayer.PlayerName, action, assignmentId, record.Assignment.State);
        if (sapi.World.PlayerByUid(assignerUid) is IServerPlayer assigner) PushAssignmentsTo(assigner);
        if (sapi.World.PlayerByUid(targetUid) is IServerPlayer assignee) PushAssignmentsTo(assignee);
    }

    /// <summary>Client → server: delete the sender's own side of a terminal-state record, re-validated by
    /// <see cref="ScribeAssignmentStore.TryDelete"/> (unknown id, non-terminal state, or an unheld side is
    /// silently ignored). The other side's view is unaffected. On success both parties are re-synced.</summary>
    private void OnServerReceivedDeleteAssignment(IServerPlayer fromPlayer, ScribeDeleteAssignmentMessage message)
    {
        if (sapi is null || assignmentStore is null) return;
        if (!TryReadGuid(message.AssignmentId, out var assignmentId))
        {
            Trace("delete-assignment from {0}: MALFORMED packet (assignmentId not 16 bytes) — ignored", fromPlayer.PlayerName);
            return;
        }

        var record = assignmentStore.TryGet(assignmentId);
        if (record?.Assignment is null)
        {
            Trace("delete-assignment from {0}: unknown assignment {1} — ignored", fromPlayer.PlayerName, assignmentId);
            return;
        }
        // Capture before deleting: TryGet returns null once both sides are purged.
        string assignerUid = record.Assignment.AssignerUid;
        string targetUid = record.Assignment.TargetPlayerUid;
        var side = (ScribeAssignmentActor)message.Side;

        if (!assignmentStore.TryDelete(assignmentId, fromPlayer.PlayerUID, side))
        {
            Trace("delete-assignment from {0}: rejected (non-terminal state or claimed side {1} not held) on {2} — ignored", fromPlayer.PlayerName, side, assignmentId);
            return;
        }

        Trace("delete-assignment from {0}: deleted their {1} side of {2}", fromPlayer.PlayerName, side, assignmentId);
        PushAssignmentSyncToBothParties(assignerUid, targetUid);
    }

    /// <summary>Client → server: mark the sender's unseen received assignments as seen (sent when the Inbox tab
    /// activates, <see cref="ScribeDialogBase.OnClickSwitchToInbox"/>). Re-pushes only if something flipped.</summary>
    private void OnServerReceivedMarkAssignmentsSeen(IServerPlayer fromPlayer, ScribeMarkAssignmentsSeenMessage message)
    {
        if (sapi is null || assignmentStore is null) return;
        if (assignmentStore.MarkAllSeen(fromPlayer.PlayerUID)) PushAssignmentsTo(fromPlayer);
    }

    /// <summary>Moves the now-Accepted record into the Assignee's document on the slot the client resolved
    /// (<see cref="ScribeAssignmentActionMessage.TargetInventoryId"/>/<see cref="ScribeAssignmentActionMessage.TargetSlotId"/>),
    /// falling back to the active hand like <c>ResolveItemPacketSlot</c>. The placed block keeps the store
    /// record's <see cref="ScribeBlock.TaskId"/> so <see cref="NotifyAssignmentDoneChanged"/> and
    /// <see cref="NotifyAssignmentDiscardOnDelete"/> can find the canonical record.
    ///
    /// An ineligible slot or a full document is a defensive no-op (the assignment stays Accepted but
    /// unplaced); the client already disables Accept in those cases.</summary>
    private void TryPlaceAcceptedAssignment(IServerPlayer assignee, ScribeBlock record, ScribeAssignmentActionMessage message)
    {
        if (sapi is null) return;
        var slot = ResolveItemPacketSlot(assignee, message.TargetInventoryId, message.TargetSlotId);
        if (slot?.Itemstack?.Collectible is not IScribeDocumentItem item || !item.IsSlotWriteable(slot))
        {
            Trace("assignment-action from {0}: Accept placement target unresolvable/read-only — assignment stays Accepted but unplaced", assignee.PlayerName);
            return;
        }

        if (!ScribeDocumentAttributes.TryReadFrom(slot.Itemstack!, out var doc) || doc is null)
            doc = new ScribeDocument();

        if (!item.DocumentPolicy(slot).CanHold(doc.BlockCount + 1))
        {
            Trace("assignment-action from {0}: Accept placement target has no capacity — assignment stays Accepted but unplaced", assignee.PlayerName);
            return;
        }

        // Only once placement will succeed; an unplaced assignment has no destination to name.
        record.Assignment!.AcceptedIntoLabel = ScribeAssignmentDestinationLabel.Format(slot.Itemstack!);

        // Carry every kind-specific field: a Tracker/Link/Craft block's content lives on
        // TargetItemCode/LinkTarget/RecipeSignature (its Text is blank).
        var placed = new ScribeBlock(record.Kind, record.Text, depth: record.Depth, taskId: record.TaskId,
            targetItemCode: record.TargetItemCode, targetQuantity: record.TargetQuantity,
            currentQuantity: record.CurrentQuantity, linkTarget: record.LinkTarget, linkLabel: record.LinkLabel,
            recipeSignature: record.RecipeSignature, linkDescription: record.LinkDescription,
            assignment: record.Assignment!.Clone());
        // The accepting player's New Task Insert preference arrives as a wire byte (it is client-local).
        // A placed sibling from the same batch wins over Top/Bottom so subtasks stay with their parent.
        doc.InsertAssignedBlock(
            doc.InsertIndexForBatch(placed.Assignment!.BatchId, (ScribeNewTaskInsert)message.NewTaskInsert),
            placed);
        ScribeDocumentAttributes.WriteTo(slot.Itemstack!, doc);
        slot.MarkDirty();

        Trace("assignment-action from {0}: Accept placed onto {1} (doc {2} \"{3}\") at inv={4} slot={5}",
            assignee.PlayerName, slot.Itemstack!.Collectible.Code, doc.DocId, doc.Title,
            message.TargetInventoryId, message.TargetSlotId);

        // Best-effort refresh of an open dialog on this item; the stack sync already persists the placement.
        sapi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeNotebookSaveMessage
        {
            DocIdBytes = doc.DocId.ToByteArray(),
            DocumentBytes = ScribeDocumentCodec.Serialize(doc),
        }, assignee);
    }

    /// <summary>Derives Completed from a task's completion flag (never a manual transition). Called after any
    /// server-side Done→true toggle (<see cref="ScribeModSystem.CompleteTaskForPlayer"/> and
    /// <see cref="ScribeModSystem.CompleteUnpinnedTaskAtSource"/> are the choke points), unconditionally, since
    /// the store record is addressed by <paramref name="taskId"/> alone and needs no document.
    ///
    /// Gates on the canonical store record's state, not <paramref name="assignmentOnBlock"/>'s, which can be
    /// stale: a manual Discard (<see cref="ApplyAssignmentAction"/>) transitions only the store record, and
    /// checking off that task must stay a no-op. If <paramref name="assignmentOnBlock"/> is Accepted it is also
    /// mirrored to Completed, because <see cref="Scribe.Core.ScribeCompletion.ApplyLeaf"/> sets only <c>Done</c>.</summary>
    public void NotifyAssignmentDoneChanged(Guid taskId, bool nowDone, ScribeAssignment? assignmentOnBlock)
    {
        if (!nowDone) return;
        if (sapi is null || assignmentStore is null)
        {
            Trace("  assignment-done: task {0} — server not ready (sapi/store null), derivation skipped", taskId);
            return;
        }
        if (assignmentStore.TryGet(taskId)?.Assignment is not { State: ScribeAssignmentState.Accepted } storeAssignment)
        {
            Trace("  assignment-done: task {0} has no Accepted canonical record — nothing to derive", taskId);
            return;
        }

        string date = NotebookHost.FormatDate(sapi);
        double timestamp = sapi.World.Calendar.TotalDays;
        if (assignmentOnBlock is { State: ScribeAssignmentState.Accepted } assignment)
        {
            ScribeAssignmentTransitions.TryMarkCompleted(assignment, true);
            StampTransitionDate(assignment, date, timestamp);
        }
        ScribeAssignmentTransitions.TryMarkCompleted(storeAssignment, true);
        StampTransitionDate(storeAssignment, date, timestamp);

        PushAssignmentSyncToBothParties(storeAssignment.AssignerUid, storeAssignment.TargetPlayerUid);
    }

    /// <summary>Whole-document-flush hook: the Editor's checkbox sets <c>Done</c> locally and persists through
    /// autosave, so the flush handlers (<see cref="BlockEntityScribeWritingStation.ApplyEdit"/>,
    /// <see cref="OnServerReceivedNotebookSave"/>) call this to give every completed assigned task the
    /// derivation of <see cref="NotifyAssignmentDoneChanged"/>.
    ///
    /// <para>Walks the saved document without diffing; <see cref="NotifyAssignmentDoneChanged"/> gates on the
    /// store state and is a cheap no-op otherwise.</para></summary>
    public void NotifyDoneAssignmentsInDocument(ScribeDocument doc)
    {
        foreach (var block in doc.Blocks)
        {
            if (block.IsCompletable && block.Done && block.Assignment is { } assignment)
                NotifyAssignmentDoneChanged(block.TaskId, true, assignment);
        }
    }

    /// <summary>Deleting an Accepted assigned task performs the Discard transition: applied to the store record
    /// through the same actor-validated path as a wire Discard, then both parties are re-synced.</summary>
    public void NotifyAssignmentDiscardOnDelete(Guid taskId, ScribeAssignment? assignmentOnBlock, string actingPlayerUid)
    {
        if (sapi is null || assignmentStore is null) return;
        if (assignmentOnBlock is not { State: ScribeAssignmentState.Accepted }) return;
        if (!assignmentStore.TryApplyAction(taskId, actingPlayerUid, ScribeAssignmentAction.Discard)) return;
        if (assignmentStore.TryGet(taskId)?.Assignment is { } storeAssignment)
            StampTransitionDate(storeAssignment, NotebookHost.FormatDate(sapi), sapi.World.Calendar.TotalDays);

        PushAssignmentSyncToBothParties(assignmentOnBlock.AssignerUid, assignmentOnBlock.TargetPlayerUid);
    }

    /// <summary>Stamps the date and in-game timestamp for <paramref name="assignment"/>'s current state; call only
    /// right after a successful transition. Core has no calendar, so this is the only place they are set.</summary>
    private static void StampTransitionDate(ScribeAssignment assignment, string date, double timestamp)
    {
        switch (assignment.State)
        {
            case ScribeAssignmentState.Accepted:
                assignment.AcceptedDate = date;
                assignment.AcceptedTimestamp = timestamp;
                break;
            case ScribeAssignmentState.Declined:
                assignment.DeclinedDate = date;
                assignment.DeclinedTimestamp = timestamp;
                break;
            case ScribeAssignmentState.Cancelled:
                assignment.CancelledDate = date;
                assignment.CancelledTimestamp = timestamp;
                break;
            case ScribeAssignmentState.Discarded:
                assignment.DiscardedDate = date;
                assignment.DiscardedTimestamp = timestamp;
                break;
            case ScribeAssignmentState.Completed:
                assignment.CompletedDate = date;
                assignment.CompletedTimestamp = timestamp;
                break;
        }
    }

    private void PushAssignmentSyncToBothParties(string assignerUid, string assigneeUid)
    {
        if (sapi is null) return;
        if (sapi.World.PlayerByUid(assignerUid) is IServerPlayer assigner) PushAssignmentsTo(assigner);
        if (sapi.World.PlayerByUid(assigneeUid) is IServerPlayer assignee) PushAssignmentsTo(assignee);
    }

    private void OnClientReceivedAssignmentSync(ScribeAssignmentSyncMessage message)
    {
        if (capi is null) return;
        ScribeAssignmentStore.TryDeserializeList(message.SentBytes, out var sent);
        ScribeAssignmentStore.TryDeserializeList(message.ReceivedBytes, out var received);
        mySentAssignments = sent ?? new List<ScribeBlock>();
        myReceivedAssignments = received ?? new List<ScribeBlock>();
        MyAssignmentsChanged?.Invoke();
    }

    /// <summary>Re-push a player their own Sent/Received views (server → client), on join and after store
    /// changes affecting them. Never sends another player's assignments.</summary>
    public void PushAssignmentsTo(IServerPlayer player)
    {
        if (sapi is null || assignmentStore is null) return;
        sapi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeAssignmentSyncMessage
        {
            SentBytes = ScribeAssignmentStore.SerializeList(assignmentStore.Sent(player.PlayerUID)),
            ReceivedBytes = ScribeAssignmentStore.SerializeList(assignmentStore.Received(player.PlayerUID)),
        }, player);
    }
}
