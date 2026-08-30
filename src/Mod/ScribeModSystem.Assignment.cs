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
    // ── Assignment ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Client → server: create and send a new player-to-player assignment (the Assignment
    /// Desk's create/send form). The server mints the assigned date and is authoritative for
    /// everything except the client-minted <c>AssignmentId</c>, target player, and task text — see
    /// <see cref="ScribeSendAssignmentMessage"/>. Rejects an unknown target player uid, a duplicate
    /// id, or blank text (<see cref="ScribeAssignmentStore.TryCreate"/> enforces the latter two).</summary>
    private void OnServerReceivedSendAssignment(IServerPlayer fromPlayer, ScribeSendAssignmentMessage message)
    {
        if (sapi is null || assignmentStore is null) return;
        if (!TryReadGuid(message.AssignmentId, out var assignmentId))
        {
            Trace("send-assignment from {0}: MALFORMED packet (assignmentId not 16 bytes) — ignored", fromPlayer.PlayerName);
            return;
        }

        string? targetUid = message.TargetPlayerUid;
        if (string.IsNullOrWhiteSpace(targetUid) || sapi.World.PlayerByUid(targetUid) is null)
        {
            Trace("send-assignment from {0}: unknown target player uid — ignored", fromPlayer.PlayerName);
            return;
        }

        string date = NotebookHost.FormatDate(sapi);
        if (!assignmentStore.TryCreate(assignmentId, fromPlayer.PlayerUID, targetUid, message.TaskText ?? "", date, out _))
        {
            Trace("send-assignment from {0}: rejected (duplicate id, blank text, or store full)", fromPlayer.PlayerName);
            return;
        }

        Trace("send-assignment from {0}: created assignment {1} -> {2}", fromPlayer.PlayerName, assignmentId, targetUid);
        PushAssignmentsTo(fromPlayer);
        if (sapi.World.PlayerByUid(targetUid) is IServerPlayer targetPlayer) PushAssignmentsTo(targetPlayer);
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

        if (action == ScribeAssignmentAction.Accept) TryPlaceAcceptedAssignment(fromPlayer, record, message);

        Trace("assignment-action from {0}: {1} on {2} -> {3}", fromPlayer.PlayerName, action, assignmentId, record.Assignment.State);
        if (sapi.World.PlayerByUid(assignerUid) is IServerPlayer assigner) PushAssignmentsTo(assigner);
        if (sapi.World.PlayerByUid(targetUid) is IServerPlayer assignee) PushAssignmentsTo(assignee);
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

        var placed = new ScribeBlock(record.Kind, record.Text, taskId: record.TaskId, assignment: record.Assignment!.Clone());
        doc.AppendAssignedBlock(placed);
        ScribeDocumentAttributes.WriteTo(slot.Itemstack!, doc);
        slot.MarkDirty();

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
    /// trigger, Read/Editor/Pinned/HUD alike, funnels through); a no-op unless the block carries an Accepted
    /// assignment. Marks the canonical <see cref="ScribeAssignmentStore"/> record (found by the shared
    /// TaskId — see <see cref="TryPlaceAcceptedAssignment"/>) Completed and mirrors it onto the placed
    /// block's own (cloned) Assignment object, then pushes a fresh sync to both parties so the Assigner's
    /// read-only Sent view reflects it too.</summary>
    public void NotifyAssignmentDoneChanged(Guid taskId, bool nowDone, ScribeAssignment? assignmentOnBlock)
    {
        if (!nowDone || sapi is null || assignmentStore is null) return;
        if (assignmentOnBlock is not { State: ScribeAssignmentState.Accepted } assignment) return;

        ScribeAssignmentTransitions.TryMarkCompleted(assignment, true);
        if (assignmentStore.TryGet(taskId)?.Assignment is { } storeAssignment)
            ScribeAssignmentTransitions.TryMarkCompleted(storeAssignment, true);

        PushAssignmentSyncToBothParties(assignment.AssignerUid, assignment.TargetPlayerUid);
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

        PushAssignmentSyncToBothParties(assignmentOnBlock.AssignerUid, assignmentOnBlock.TargetPlayerUid);
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
