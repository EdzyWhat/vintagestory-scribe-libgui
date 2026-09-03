namespace Scribe.Core;

/// <summary>The lifecycle state of a player-to-player assignment.</summary>
public enum ScribeAssignmentState : byte
{
    Unaccepted = 0,
    Accepted = 1,
    Declined = 2,
    Cancelled = 3,
    Discarded = 4,
    Completed = 5,

    /// <summary>Store-only pre-receipt state for a Task Notice sealed at send time (refine-task-notice-ux):
    /// a record exists (visible to the Assigner as "Sent") before the Assignee has physically received the
    /// notice. Never reached via <see cref="ScribeAssignmentTransitions.CanApply"/>'s normal matrix — only
    /// <see cref="ScribeAssignmentStore.TryCreateSent"/> creates it, and only
    /// <see cref="ScribeAssignmentStore.TryMarkReceived"/> transitions out of it (to Unaccepted), mirroring
    /// how <see cref="ScribeAssignmentStore.TryCreateAccepted"/> already bypasses the matrix.</summary>
    Sent = 6,
}

/// <summary>The actor requesting an assignment transition.</summary>
public enum ScribeAssignmentActor : byte
{
    Assigner = 0,
    Assignee = 1,
}

public static class ScribeAssignmentStateExtensions
{
    /// <summary>True for the four states that accept no further transition (Declined, Cancelled,
    /// Discarded, Completed) — the same set <see cref="ScribeAssignmentStore.TryDelete"/> restricts
    /// deletion to.</summary>
    public static bool IsTerminal(this ScribeAssignmentState state) => state is
        ScribeAssignmentState.Declined or ScribeAssignmentState.Cancelled or
        ScribeAssignmentState.Discarded or ScribeAssignmentState.Completed;
}

/// <summary>The explicitly requested assignment actions. Completed is intentionally absent:
/// completion is derived from the task's Done flag.</summary>
public enum ScribeAssignmentAction : byte
{
    Accept = 0,
    Decline = 1,
    Cancel = 2,
    Discard = 3,
}

/// <summary>Pure, game-agnostic assignment state and transition rules.</summary>
public sealed class ScribeAssignment
{
    public string AssignerUid { get; set; }

    /// <summary>UID of the player this assignment was sent to. Meaningful for a staging record in
    /// <see cref="ScribeAssignmentStore"/> (both the Assigner's Assignment-tab history and the
    /// Assignee's Inbox filter on it); redundant but harmless once the task is placed into the
    /// Assignee's own document (it is then implicitly "whoever owns this document").</summary>
    public string TargetPlayerUid { get; set; }

    public ScribeAssignmentState State { get; set; }
    public string AssignedDate { get; set; }
    public bool Seen { get; set; }

    /// <summary>In-game date this assignment reached the given transition, or null if it never has
    /// (refine-assignment-desk-inbox-ux triage 2026-08-31: "we should also see stubs for when it was
    /// accepted, discarded, etc."). <see cref="AcceptedDate"/> can coexist with any terminal date (an
    /// assignment passes through Accepted before Completed/Discarded); the other four are mutually
    /// exclusive since a state transition is one-way and terminal. Stamped by the Mod layer (which has
    /// calendar access this game-agnostic type does not) immediately after a transition actually
    /// happens — see <c>ScribeModSystem</c>'s assignment-action handlers.</summary>
    public string? AcceptedDate { get; set; }
    public string? DeclinedDate { get; set; }
    public string? CancelledDate { get; set; }
    public string? DiscardedDate { get; set; }
    public string? CompletedDate { get; set; }

    /// <summary>In-game date a Task Notice's store record transitioned Sent → Unaccepted (refine-task-notice-ux):
    /// the moment the sealed notice actually entered the Assignee's own inventory, stamped by
    /// <see cref="ScribeAssignmentStore.TryMarkReceived"/>. Null for every assignment that never went
    /// through the Sent state (every non-notice send, and any notice sent before this change shipped).</summary>
    public string? ReceivedDate { get; set; }

    /// <summary>Short destination label (e.g. <c>Notebook "Book of Nick"</c>) captured once, at
    /// Accept-placement time, naming the Scribe item the task actually landed in. Null when the
    /// assignment was never placed (still Unaccepted, or an Accept that failed placement).</summary>
    public string? AcceptedIntoLabel { get; set; }

    /// <summary>Identifies which single send call created this record, alongside any sibling rows sent
    /// in the same batch (refine-assignment-desk-inbox-ux 12.2 root-cause fix). <see cref="AssignedDate"/>
    /// is a coarse, human-readable in-game-day string that two SEPARATE batches sent on the same day can
    /// share — grouping the Inbox/Sent-history lists by it (the original approach) could silently merge
    /// two unrelated batches into one run. This is a fresh <see cref="Guid"/> minted once per send call
    /// (<c>ScribeModSystem.OnServerReceivedSendAssignmentBatch</c>) and stamped on every row it creates,
    /// giving the client an unambiguous batch boundary to group and newest-first-sort by, independent of
    /// the display date string. Defaults to <see cref="Guid.Empty"/> for callers that don't care (e.g.
    /// most Core unit tests) — every record from one such caller lands in a single default-Id "batch",
    /// which is harmless since nothing in Core itself groups by it.</summary>
    public Guid BatchId { get; set; }

    /// <summary>Whether the Assignee has deleted their own side of a terminal record
    /// (split-assignment-delete-by-viewer) — once true, this record no longer appears in that
    /// player's Inbox (<see cref="ScribeAssignmentStore.Received"/>), even though the Assigner's Sent
    /// Assignment History is unaffected. The record is only actually removed from the store once
    /// BOTH this and <see cref="HiddenFromAssigner"/> are true.</summary>
    public bool HiddenFromAssignee { get; set; }

    /// <summary>Whether the Assigner has deleted their own side of a terminal record — the mirror of
    /// <see cref="HiddenFromAssignee"/>, hiding it from <see cref="ScribeAssignmentStore.Sent"/>
    /// instead. A self-assignment (Assigner == Assignee) needs both flags independently settable on
    /// the one shared record, since the same player's two views can be deleted one at a time.</summary>
    public bool HiddenFromAssigner { get; set; }

    public ScribeAssignment(string assignerUid, string assignedDate,
        ScribeAssignmentState state = ScribeAssignmentState.Unaccepted, bool seen = false,
        string? targetPlayerUid = null, Guid batchId = default)
    {
        AssignerUid = assignerUid ?? "";
        TargetPlayerUid = targetPlayerUid ?? "";
        AssignedDate = assignedDate ?? "";
        State = state;
        Seen = seen;
        BatchId = batchId;
    }

    public ScribeAssignment Clone() => new(AssignerUid, AssignedDate, State, Seen, TargetPlayerUid, BatchId)
    {
        AcceptedDate = AcceptedDate,
        DeclinedDate = DeclinedDate,
        CancelledDate = CancelledDate,
        DiscardedDate = DiscardedDate,
        CompletedDate = CompletedDate,
        ReceivedDate = ReceivedDate,
        AcceptedIntoLabel = AcceptedIntoLabel,
        HiddenFromAssignee = HiddenFromAssignee,
        HiddenFromAssigner = HiddenFromAssigner,
    };

    /// <summary>Marks an incoming assignment as seen without changing its lifecycle state.</summary>
    public void MarkSeen() => Seen = true;
}

/// <summary>Validates the assignment transition matrix without any game/API dependencies.</summary>
public static class ScribeAssignmentTransitions
{
    public static bool TryApply(ScribeAssignment assignment, ScribeAssignmentActor actor,
        ScribeAssignmentAction action)
    {
        if (!CanApply(assignment.State, actor, action, out var next)) return false;
        assignment.State = next;
        return true;
    }

    public static bool CanApply(ScribeAssignmentState state, ScribeAssignmentActor actor,
        ScribeAssignmentAction action, out ScribeAssignmentState next)
    {
        next = state;
        if (state != ScribeAssignmentState.Unaccepted && state != ScribeAssignmentState.Accepted)
            return false;

        if (state == ScribeAssignmentState.Unaccepted)
        {
            if (actor == ScribeAssignmentActor.Assignee && action == ScribeAssignmentAction.Accept)
                next = ScribeAssignmentState.Accepted;
            else if (actor == ScribeAssignmentActor.Assignee && action == ScribeAssignmentAction.Decline)
                next = ScribeAssignmentState.Declined;
            else if (actor == ScribeAssignmentActor.Assigner && action == ScribeAssignmentAction.Cancel)
                next = ScribeAssignmentState.Cancelled;
            else return false;
            return true;
        }

        if (actor == ScribeAssignmentActor.Assignee && action == ScribeAssignmentAction.Discard)
        {
            next = ScribeAssignmentState.Discarded;
            return true;
        }

        return false;
    }

    public static bool TryMarkCompleted(ScribeAssignment assignment, bool taskDone)
    {
        if (!taskDone || assignment.State != ScribeAssignmentState.Accepted) return false;
        assignment.State = ScribeAssignmentState.Completed;
        return true;
    }
}
