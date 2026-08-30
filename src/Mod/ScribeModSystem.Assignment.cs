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
    /// trusted as proof of authorization.
    ///
    /// Accept-time placement (moving the assigned content into the Assignee's own document, per the
    /// `assignment-state-machine` capability's placement requirement) is a separate step not yet
    /// wired here — this handler only applies the state transition and syncs both dashboards.</summary>
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

        Trace("assignment-action from {0}: {1} on {2} -> {3}", fromPlayer.PlayerName, action, assignmentId, record.Assignment.State);
        if (sapi.World.PlayerByUid(assignerUid) is IServerPlayer assigner) PushAssignmentsTo(assigner);
        if (sapi.World.PlayerByUid(targetUid) is IServerPlayer assignee) PushAssignmentsTo(assignee);
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
