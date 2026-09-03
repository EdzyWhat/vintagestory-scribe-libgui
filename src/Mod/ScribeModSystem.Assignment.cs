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

    /// <summary>Client → server: send a multi-item assignment batch from the Create Assignments tab's
    /// staging slot. Thin network shell — see <see cref="SendAssignmentBatch"/> for the actual logic,
    /// which this delegates to unchanged.</summary>
    private void OnServerReceivedSendAssignmentBatch(IServerPlayer fromPlayer, ScribeSendAssignmentBatchMessage message)
        => SendAssignmentBatch(fromPlayer, message);

    /// <summary>Send a multi-item assignment batch from the Create Assignments tab's staging slot
    /// (assignment-multi-item-creation, design.md D8-D13). Creates one independent assignment per row via
    /// the D12-broadened <see cref="ScribeAssignmentStore.TryCreate"/> — a malformed or store-rejected row
    /// is skipped, not fatal to the rest of the batch (matches "each row behaves exactly like any other
    /// assignment" — one bad row shouldn't sink the others). When
    /// <see cref="ScribeSendAssignmentBatchMessage.DeleteFromSource"/> is set, every successfully-created
    /// row is also removed from the staged document afterward via <see cref="TryRemoveStagedRows"/>.
    ///
    /// <para>Delivery-mode branch (add-assignment-physical-delivery-mode, tasks.md 4.5): the server
    /// re-derives whether a Task Notice is required from its OWN current `DeliveryMode`
    /// (<see cref="ScribeDeliveryPolicy.RequiresNotice"/>), never trusting <see
    /// cref="ScribeSendAssignmentBatchMessage.DeliveryChoice"/> alone. When required, NO
    /// <see cref="ScribeAssignmentStore"/> record is created for any row yet — the notice item itself is
    /// the pending record until Accept (Core 2.2/2.3) — instead every row is sealed as one
    /// <see cref="ScribeBlock"/> (each carrying its own Unaccepted <see cref="ScribeAssignment"/>) onto a
    /// fresh notice consumed from the supply slot and placed in the output slot.</para>
    ///
    /// <para>Public — matches the <see cref="ScribeModSystem.PinOperations"/>-file precedent
    /// (<c>SetPinForPlayer</c>/<c>CompleteTaskForPlayer</c>) of a domain-named method the network handler
    /// delegates to and the integration suite drives directly (refine-task-notice-ux 2.5).</para></summary>
    public void SendAssignmentBatch(IServerPlayer fromPlayer, ScribeSendAssignmentBatchMessage message)
    {
        if (sapi is null || assignmentStore is null) return;

        string? targetUid = message.TargetPlayerUid;
        if (string.IsNullOrWhiteSpace(targetUid) || sapi.World.PlayerByUid(targetUid) is null)
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
        // One fresh id per SEND CALL, shared by every row it creates (refine-assignment-desk-inbox-ux
        // 12.2 root-cause fix) — see ScribeAssignment.BatchId's remarks on why `date` alone isn't a safe
        // batch-grouping key (two separate sends on the same in-game day would collide on it).
        var batchId = Guid.NewGuid();

        var deliveryMode = ScribeDeliveryConfig.ReadMode(sapi);
        var deliveryChoice = (ScribeDeliveryChoice)message.DeliveryChoice;
        bool viaNotice = ScribeDeliveryPolicy.RequiresNotice(deliveryMode, deliveryChoice);

        int createdCount = viaNotice
            ? SendBatchViaNotice(fromPlayer, message, rows, targetUid, date, batchId)
            : SendBatchViaLocalInboxes(fromPlayer, message, rows, targetUid, date, batchId);

        if (createdCount == 0) return;
        Trace("send-assignment-batch from {0}: created {1} row(s) -> {2} (via {3})",
            fromPlayer.PlayerName, createdCount, targetUid, viaNotice ? "notice" : "local inbox");
    }

    /// <summary>The pre-existing Local Inboxes path: creates one <see cref="ScribeAssignmentStore"/> record
    /// per row directly (Unaccepted), then pushes both parties' Inbox/Sent views.</summary>
    private int SendBatchViaLocalInboxes(IServerPlayer fromPlayer, ScribeSendAssignmentBatchMessage message,
        List<ScribeAssignmentBatchRow> rows, string targetUid, string date, Guid batchId)
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
                    recipeSignature: row.RecipeSignature, depth: row.Depth, batchId: batchId))
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

    /// <summary>The physical-delivery path (`task-notice-item` capability): seals every row into ONE
    /// notice document (no store record yet — see the caller's remarks), consumed from
    /// <see cref="BlockEntityAssignmentDesk.NoticeSupplySlotIndex"/> and placed into
    /// <see cref="BlockEntityAssignmentDesk.NoticeOutputSlotIndex"/>. Refuses the whole batch (rather than
    /// partially sealing it) if the Desk can't be resolved, the supply slot has no blank notice, or the
    /// output slot is already occupied — mirroring the client-side gate (task 4.5) but re-validated here as
    /// the actual authority.</summary>
    private int SendBatchViaNotice(IServerPlayer fromPlayer, ScribeSendAssignmentBatchMessage message,
        List<ScribeAssignmentBatchRow> rows, string targetUid, string date, Guid batchId)
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
                seen: false, targetPlayerUid: targetUid, batchId: batchId);
            sealedDoc.AppendAssignedBlock(new ScribeBlock((ScribeBlockKind)row.Kind, row.Text ?? "", depth: row.Depth,
                taskId: assignmentId, targetItemCode: row.TargetItemCode, targetQuantity: row.TargetQuantity,
                linkTarget: row.LinkTarget, linkLabel: row.LinkLabel, recipeSignature: row.RecipeSignature,
                linkDescription: row.LinkDescription, assignment: assignment));

            // A parallel Sent-state store record, same assignmentId as the block just sealed above
            // (refine-task-notice-ux — reverses the earlier "no record until Accept" behavior): visible to
            // the Assigner as "Sent" immediately, hidden from the Assignee's Inbox until the notice
            // physically reaches their inventory (OnTaskNoticeProximityTick -> TryMarkReceived).
            assignmentStore.TryCreateSent(assignmentId, fromPlayer.PlayerUID, targetUid, row.Text ?? "", date, out _,
                kind: (ScribeBlockKind)row.Kind, targetItemCode: row.TargetItemCode, targetQuantity: row.TargetQuantity,
                linkTarget: row.LinkTarget, linkLabel: row.LinkLabel, linkDescription: row.LinkDescription,
                recipeSignature: row.RecipeSignature, depth: row.Depth, batchId: batchId);

            createdCount++;
            if (message.DeleteFromSource && TryReadGuid(row.SourceTaskId, out var sourceTaskId))
                sentSourceTaskIds.Add(sourceTaskId);
        }

        if (createdCount == 0) return 0;
        RemoveSentSourceRows(message, sentSourceTaskIds);

        // Consume one blank notice, seal a fresh single-stack notice with the batch's document, and place
        // it in the output slot for the sender to collect and hand-deliver. Capture the collectible BEFORE
        // TakeOut, which nulls the slot's Itemstack once its count reaches zero.
        var noticeCollectible = supplySlot.Itemstack!.Collectible;
        supplySlot.TakeOut(1);
        supplySlot.MarkDirty();
        var notice = new ItemStack(noticeCollectible, 1);
        ScribeDocumentAttributes.WriteTo(notice, sealedDoc);
        outputSlot.Itemstack = notice;
        outputSlot.MarkDirty();
        desk.MarkDirty(true);
        // The proximity heartbeat's cheap "does this player have anything to scan for" gate
        // (task-notice-proximity-signal 5.1) — one notice sealed, regardless of how many rows it carries.
        AdjustOutstandingNoticeCount(targetUid, +1);
        // Refresh the Assigner's own Sent Assignment History so the just-created Sent-state rows show up
        // immediately (refine-task-notice-ux) — the Assignee gets no push here, since their Inbox stays
        // silent until receipt.
        PushAssignmentsTo(fromPlayer);
        return createdCount;
    }

    /// <summary>"Delete from source on send" routing shared by both delivery paths (design.md D13/D6).</summary>
    private void RemoveSentSourceRows(ScribeSendAssignmentBatchMessage message, List<Guid> sentSourceTaskIds)
    {
        if (!message.DeleteFromSource || sentSourceTaskIds.Count == 0) return;
        if (message.SourceIsDeskDocument)
            TryRemoveDeskOwnRows(message.X, message.Y, message.Z, sentSourceTaskIds);
        else
            TryRemoveStagedRows(message.X, message.Y, message.Z, message.StagingSlot, sentSourceTaskIds);
    }

    /// <summary>"Delete from source on send" (design.md D13): removes the sent rows (matched by their
    /// staged-document TaskId) from the Assignment Desk's staged item, then re-syncs the slot. A no-op if
    /// the block/slot/item/document can no longer be resolved — the assignments were already created
    /// server-authoritatively regardless, so a lost staging item never loses the send itself, only the
    /// cleanup.</summary>
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

    /// <summary>"Delete from source on send" (design.md D13), Desk-own-document sibling of
    /// <see cref="TryRemoveStagedRows"/> (add-assignment-desk-own-tasks design.md D6): removes the sent
    /// rows from the Assignment Desk block entity's OWN persisted document rather than a staged item's
    /// embedded one. Reuses <see cref="BlockEntityScribeWritingStation.DeleteTaskFromReader"/> — the same
    /// lock-free mutate-and-persist-and-sync path a normal viewer action (e.g. the Delete completion
    /// policy) already uses on this block entity, so this introduces no new persistence mechanism. Each
    /// call independently no-ops (and skips, never errors) a TaskId the document no longer contains —
    /// e.g. another player already deleted it via the Editor tab between this player's snapshot and the
    /// server processing this removal — mirroring <see cref="TryRemoveStagedRows"/>'s best-effort
    /// semantics. A no-op if the block itself can no longer be resolved.</summary>
    private void TryRemoveDeskOwnRows(int x, int y, int z, List<Guid> sourceTaskIdsToRemove)
    {
        if (sapi is null) return;
        var pos = new Vintagestory.API.MathTools.BlockPos(x, y, z);
        if (sapi.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityAssignmentDesk desk) return;

        foreach (var taskId in sourceTaskIdsToRemove)
            desk.DeleteTaskFromReader(taskId);
    }

    /// <summary>Client → server: request an Accept/Decline/Cancel/Discard transition. The server
    /// derives the actor from the authenticated sender and re-validates the transition through
    /// <see cref="ScribeAssignmentStore.TryApplyAction"/> — the action byte on the wire is never
    /// trusted as proof of authorization. On a successful Accept, also resolves Accept-time placement
    /// (<see cref="TryPlaceAcceptedAssignment"/>) — moving the assigned content into the Assignee's own
    /// document, per the `assignment-state-machine` capability's placement requirement.</summary>
    private void OnServerReceivedAssignmentAction(IServerPlayer fromPlayer, ScribeAssignmentActionMessage message)
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
        // Capture both parties BEFORE applying — a successful transition never changes who they are,
        // but reading them off the record after a failed lookup would be wrong.
        string assignerUid = record.Assignment.AssignerUid;
        string targetUid = record.Assignment.TargetPlayerUid;

        var action = (ScribeAssignmentAction)message.Action;
        if (!assignmentStore.TryApplyAction(assignmentId, fromPlayer.PlayerUID, action))
        {
            Trace("assignment-action from {0}: illegal transition {1} on {2} — ignored", fromPlayer.PlayerName, action, assignmentId);
            return;
        }
        StampTransitionDate(record.Assignment, NotebookHost.FormatDate(sapi));

        if (action == ScribeAssignmentAction.Accept) TryPlaceAcceptedAssignment(fromPlayer, record, message);

        Trace("assignment-action from {0}: {1} on {2} -> {3}", fromPlayer.PlayerName, action, assignmentId, record.Assignment.State);
        if (sapi.World.PlayerByUid(assignerUid) is IServerPlayer assigner) PushAssignmentsTo(assigner);
        if (sapi.World.PlayerByUid(targetUid) is IServerPlayer assignee) PushAssignmentsTo(assignee);
    }

    /// <summary>Client → server: delete the sender's own side of a terminal-state assignment record
    /// (manage-terminal-assignment-records / split-assignment-delete-by-viewer). Re-validates through
    /// <see cref="ScribeAssignmentStore.TryDelete"/> — an unknown id, a non-terminal state, or a sender
    /// who doesn't actually hold the claimed side is silently ignored. On success, re-syncs both parties;
    /// each party's own resync naturally excludes what's now hidden from THEM (the other side's view is
    /// unaffected — this deletion is scoped to one side only, never both at once).</summary>
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
        // Capture both parties BEFORE deleting — TryGet can return null once the record is fully purged
        // (both sides deleted).
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

    /// <summary>Client → server: mark every one of the sender's currently-unseen received assignments as
    /// seen (design.md Decision 4), sent when the client's Inbox tab becomes the active view
    /// (<see cref="ScribeDialogBase.OnClickSwitchToInbox"/>). Only re-pushes the sender when something
    /// actually flipped, sparing an idle re-sync on every repeat visit.</summary>
    private void OnServerReceivedMarkAssignmentsSeen(IServerPlayer fromPlayer, ScribeMarkAssignmentsSeenMessage message)
    {
        if (sapi is null || assignmentStore is null) return;
        if (assignmentStore.MarkAllSeen(fromPlayer.PlayerUID)) PushAssignmentsTo(fromPlayer);
    }

    /// <summary>Accept-time placement resolution (assignment-state-machine's placement requirement): moves
    /// the store's now-Accepted record into the Assignee's own document, onto the exact slot the client
    /// resolved (held item, or the single/chosen inventory candidate — see
    /// <see cref="ScribeAssignmentActionMessage.TargetInventoryId"/>/<see cref="ScribeAssignmentActionMessage.TargetSlotId"/>).
    /// Falls back to the active hand for a legacy/missing target, mirroring <c>ResolveItemPacketSlot</c>'s
    /// existing convention. The placed block keeps the SAME <see cref="ScribeBlock.TaskId"/> as the store
    /// record (see <see cref="ScribeDocument.AppendAssignedBlock"/>) so later Done→Completed derivation
    /// (<see cref="NotifyAssignmentDoneChanged"/>) and Delete→Discard (<see cref="NotifyAssignmentDiscardOnDelete"/>)
    /// can find the canonical record by that shared id.
    ///
    /// A no-op (the assignment stays Accepted but unplaced) when the resolved slot is ineligible or the
    /// target document has no capacity — both should already be impossible client-side (the Accept control
    /// is disabled without an eligible target), so this is a defensive guard, not the expected path.</summary>
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

        // Capture the destination label on the canonical store record (capture-assignment-accept-
        // destination) — only once placement is actually going to succeed (design.md Risk mitigation:
        // an "Accepted but unplaced" assignment, from either early-return branch above, has no
        // destination to name and stays label-less).
        record.Assignment!.AcceptedIntoLabel = ScribeAssignmentDestinationLabel.Format(slot.Itemstack!);

        // Carry every kind-specific field, not just Kind/Text (playtest 2026-08-31 bug fix): a
        // Tracker/Link/Craft assignment's real content lives on TargetItemCode/LinkTarget/RecipeSignature
        // (its own Text is blank by convention — see ScribeAssignmentStore.TryCreate's remarks), and Depth
        // positions a subtask under its parent. Dropping these silently placed a blank, un-indented block
        // that looked nothing like what was accepted.
        var placed = new ScribeBlock(record.Kind, record.Text, depth: record.Depth, taskId: record.TaskId,
            targetItemCode: record.TargetItemCode, targetQuantity: record.TargetQuantity,
            currentQuantity: record.CurrentQuantity, linkTarget: record.LinkTarget, linkLabel: record.LinkLabel,
            recipeSignature: record.RecipeSignature, linkDescription: record.LinkDescription,
            assignment: record.Assignment!.Clone());
        // Follow the accepting player's own New Task Insert preference (refine-assignment-desk-inbox-ux
        // 13.1) instead of always appending to the bottom — the byte on the wire since this preference
        // is client-local and never server state (see ScribeModSystem.MySettings's remarks). Prefer
        // landing next to an already-placed sibling from the same batch (triage 2026-08-31: accepting a
        // batch's rows one at a time was scattering subtasks away from their parent) over the raw
        // Top/Bottom preference — see ScribeDocument.InsertIndexForBatch's remarks.
        doc.InsertAssignedBlock(
            doc.InsertIndexForBatch(placed.Assignment!.BatchId, (ScribeNewTaskInsert)message.NewTaskInsert),
            placed);
        ScribeDocumentAttributes.WriteTo(slot.Itemstack!, doc);
        slot.MarkDirty();

        // Diagnostic for the accept-destination-remembers-a-dropped-item investigation (2026-09-01):
        // pinpoints exactly which item/slot a placement landed on, since the only other trace lines here
        // cover the two FAILURE branches above — a successful placement was previously silent. Drop once
        // the investigation concludes (or demote to VerboseDebug).
        Trace("assignment-action from {0}: Accept placed onto {1} (doc {2} \"{3}\") at inv={4} slot={5}",
            assignee.PlayerName, slot.Itemstack!.Collectible.Code, doc.DocId, doc.Title,
            message.TargetInventoryId, message.TargetSlotId);

        // Refresh an already-open dialog on this exact item, if any — mirrors the history-refresh push in
        // OnServerReceivedNotebookOpened. Best-effort; the raw stack sync alone already persists the
        // placement even if this item's dialog isn't open right now.
        sapi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeNotebookSaveMessage
        {
            DocIdBytes = doc.DocId.ToByteArray(),
            DocumentBytes = ScribeDocumentCodec.Serialize(doc),
        }, assignee);
    }

    /// <summary>Derived-completion hook (assignment-state-machine: "Completed is derived from the task's
    /// own completion flag, never a manual transition"). Called after any server-side Done→true toggle on a
    /// completable block (<see cref="ScribeModSystem.CompleteTaskForPlayer"/> and
    /// <see cref="ScribeModSystem.CompleteUnpinnedTaskAtSource"/> — the two choke points every completion
    /// trigger, Read/Editor/Pinned/HUD alike, funnels through), called UNCONDITIONALLY regardless of
    /// whether the caller could resolve the task's owning document — the canonical
    /// <see cref="ScribeAssignmentStore"/> record is addressed by <paramref name="taskId"/> alone and never
    /// needed the document (fix-assignment-completion-doc-resolution: a Notebook not currently in the
    /// completing player's inventory used to make this derivation silently never run).
    ///
    /// Gates on the CANONICAL store record's state, not <paramref name="assignmentOnBlock"/>'s — the block
    /// can be stale in either direction: the Inbox's manual Discard action (legal from Accepted, <see
    /// cref="OnServerReceivedAssignmentAction"/>) transitions only the store record, since the task it
    /// discards is deliberately left in place rather than deleted (that's <see
    /// cref="NotifyAssignmentDiscardOnDelete"/>'s job) — gating on the store means checking off that
    /// already-discarded task correctly stays a no-op. When <paramref name="assignmentOnBlock"/> IS
    /// available and itself Accepted, it is additionally mirrored to Completed in place — this is the only
    /// thing that keeps a resolved block's own embedded Assignment object in sync (the server's actual
    /// Done-toggle path, <see cref="Scribe.Core.ScribeCompletion.ApplyLeaf"/>, sets only <c>Done</c> and
    /// never touches <c>Assignment</c> itself).</summary>
    public void NotifyAssignmentDoneChanged(Guid taskId, bool nowDone, ScribeAssignment? assignmentOnBlock)
    {
        if (!nowDone) return; // ordinary uncheck — not an assignment concern, nothing to trace
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
        if (assignmentOnBlock is { State: ScribeAssignmentState.Accepted } assignment)
        {
            ScribeAssignmentTransitions.TryMarkCompleted(assignment, true);
            StampTransitionDate(assignment, date);
        }
        ScribeAssignmentTransitions.TryMarkCompleted(storeAssignment, true);
        StampTransitionDate(storeAssignment, date);

        PushAssignmentSyncToBothParties(storeAssignment.AssignerUid, storeAssignment.TargetPlayerUid);
    }

    /// <summary>Delete-on-Accepted hook (assignment-state-machine: "Deleting an Accepted assigned task
    /// performs the Discard transition"). Called after a document Delete removes a block that carried an
    /// Accepted assignment; applies Discard to the canonical store record through the same actor-validated
    /// path a wire Discard action would use, then pushes a fresh sync to both parties.</summary>
    public void NotifyAssignmentDiscardOnDelete(Guid taskId, ScribeAssignment? assignmentOnBlock, string actingPlayerUid)
    {
        if (sapi is null || assignmentStore is null) return;
        if (assignmentOnBlock is not { State: ScribeAssignmentState.Accepted }) return;
        if (!assignmentStore.TryApplyAction(taskId, actingPlayerUid, ScribeAssignmentAction.Discard)) return;
        if (assignmentStore.TryGet(taskId)?.Assignment is { } storeAssignment)
            StampTransitionDate(storeAssignment, NotebookHost.FormatDate(sapi));

        PushAssignmentSyncToBothParties(assignmentOnBlock.AssignerUid, assignmentOnBlock.TargetPlayerUid);
    }

    /// <summary>Stamps the appropriate per-transition date field (refine-assignment-desk-inbox-ux triage
    /// 2026-08-31) for whichever state <paramref name="assignment"/> is CURRENTLY in — call this
    /// immediately after a transition actually succeeded, never speculatively. Core has no calendar
    /// access, so this Mod-layer stamp is the only place these fields are ever set.</summary>
    private static void StampTransitionDate(ScribeAssignment assignment, string date)
    {
        switch (assignment.State)
        {
            case ScribeAssignmentState.Accepted: assignment.AcceptedDate = date; break;
            case ScribeAssignmentState.Declined: assignment.DeclinedDate = date; break;
            case ScribeAssignmentState.Cancelled: assignment.CancelledDate = date; break;
            case ScribeAssignmentState.Discarded: assignment.DiscardedDate = date; break;
            case ScribeAssignmentState.Completed: assignment.CompletedDate = date; break;
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

    /// <summary>Re-push a single player their own Sent/Received assignment views (server → client).
    /// Called on join and after any store change affecting that player. Only ever sends a player
    /// their own assignments — never another player's.</summary>
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
