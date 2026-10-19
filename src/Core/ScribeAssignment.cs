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

    /// <summary>Store-only state for a Task Notice sealed at send time, before the Assignee has received
    /// it. Outside <see cref="ScribeAssignmentTransitions.CanApply"/>'s matrix: only
    /// <see cref="ScribeAssignmentStore.TryCreateSent"/> creates it and only
    /// <see cref="ScribeAssignmentStore.TryMarkReceived"/> leaves it (to Unaccepted).</summary>
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
    /// <summary>True for the four states with no further transition (Declined, Cancelled, Discarded,
    /// Completed); the same set <see cref="ScribeAssignmentStore.TryDelete"/> restricts deletion to.</summary>
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

    /// <summary>UID of the recipient. Used to filter staging records in <see cref="ScribeAssignmentStore"/>
    /// (Assigner history and Assignee Inbox); redundant once the task is in the Assignee's own document.</summary>
    public string TargetPlayerUid { get; set; }

    public ScribeAssignmentState State { get; set; }
    public string AssignedDate { get; set; }

    /// <summary>In-game timestamp for <see cref="AssignedDate"/> (<c>Calendar.TotalDays</c>), used to rebuild
    /// the date via <c>scribe:date-format</c>. Null on older blobs; <c>0</c> is a real value, not a sentinel.</summary>
    public double? AssignedTimestamp { get; set; }

    public bool Seen { get; set; }

    /// <summary>In-game date the assignment reached the transition, or null if it never has.
    /// <see cref="AcceptedDate"/> can coexist with a terminal date; the other four are mutually exclusive.
    /// Stamped by the Mod layer (which has calendar access) right after a transition happens.</summary>
    public string? AcceptedDate { get; set; }
    public string? DeclinedDate { get; set; }
    public string? CancelledDate { get; set; }
    public string? DiscardedDate { get; set; }
    public string? CompletedDate { get; set; }

    /// <summary>Timestamps matching the transition dates above; null if it never happened or the record
    /// predates timestamps. Same <c>0</c>-is-real rule as <see cref="AssignedTimestamp"/>.</summary>
    public double? AcceptedTimestamp { get; set; }
    public double? DeclinedTimestamp { get; set; }
    public double? CancelledTimestamp { get; set; }
    public double? DiscardedTimestamp { get; set; }
    public double? CompletedTimestamp { get; set; }

    /// <summary>In-game date a Task Notice went Sent → Unaccepted (it entered the Assignee's inventory),
    /// stamped by <see cref="ScribeAssignmentStore.TryMarkReceived"/>. Null if it never went through Sent.</summary>
    public string? ReceivedDate { get; set; }

    /// <summary>Timestamp for <see cref="ReceivedDate"/>; null if never Sent or the record predates timestamps.</summary>
    public double? ReceivedTimestamp { get; set; }

    /// <summary>UID the assignment was originally addressed to, before a non-recipient holder of a Task
    /// Notice accepted it. Stamped by <see cref="ScribeAssignmentStore.TryRedirectTarget"/> before
    /// <see cref="TargetPlayerUid"/> is overwritten. Null unless redirected.</summary>
    public string? RedirectedFromUid { get; set; }

    /// <summary>In-game date of the redirect; null unless redirected.</summary>
    public string? RedirectedDate { get; set; }

    /// <summary>Timestamp for <see cref="RedirectedDate"/>; null unless redirected.</summary>
    public double? RedirectedTimestamp { get; set; }

    /// <summary>Destination label (e.g. <c>Notebook "Book of Nick"</c>) captured at Accept-placement time.
    /// Null if never placed.</summary>
    public string? AcceptedIntoLabel { get; set; }

    /// <summary>Id minted once per send call (<c>ScribeModSystem.OnServerReceivedSendAssignmentBatch</c>)
    /// and stamped on every row it creates, so the client can group lists by batch. Needed because
    /// <see cref="AssignedDate"/> is coarse and two batches on the same day would merge. Defaults to
    /// <see cref="Guid.Empty"/>; nothing in Core groups by it.</summary>
    public Guid BatchId { get; set; }

    /// <summary>The Assignee deleted their side of a terminal record: it leaves their Inbox
    /// (<see cref="ScribeAssignmentStore.Received"/>) but not the Assigner's history. The record is
    /// removed from the store only once BOTH this and <see cref="HiddenFromAssigner"/> are true.</summary>
    public bool HiddenFromAssignee { get; set; }

    /// <summary>Mirror of <see cref="HiddenFromAssignee"/>, hiding the record from
    /// <see cref="ScribeAssignmentStore.Sent"/>. Both flags are independent so a self-assignment's two
    /// views can be deleted one at a time.</summary>
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
        AssignedTimestamp = AssignedTimestamp,
        AcceptedDate = AcceptedDate,
        DeclinedDate = DeclinedDate,
        CancelledDate = CancelledDate,
        DiscardedDate = DiscardedDate,
        CompletedDate = CompletedDate,
        AcceptedTimestamp = AcceptedTimestamp,
        DeclinedTimestamp = DeclinedTimestamp,
        CancelledTimestamp = CancelledTimestamp,
        DiscardedTimestamp = DiscardedTimestamp,
        CompletedTimestamp = CompletedTimestamp,
        ReceivedDate = ReceivedDate,
        ReceivedTimestamp = ReceivedTimestamp,
        RedirectedFromUid = RedirectedFromUid,
        RedirectedDate = RedirectedDate,
        RedirectedTimestamp = RedirectedTimestamp,
        AcceptedIntoLabel = AcceptedIntoLabel,
        HiddenFromAssignee = HiddenFromAssignee,
        HiddenFromAssigner = HiddenFromAssigner,
    };

    /// <summary>Marks an incoming assignment as seen without changing its lifecycle state.</summary>
    public void MarkSeen() => Seen = true;

    /// <summary>True when the ambient "unseen assignment" block particle is warranted: unseen and not
    /// delivered as a Task Notice already in the recipient's inventory (the notice is itself the signal).
    /// Local-Inboxes assignments never stamp <see cref="ReceivedDate"/>.</summary>
    public bool NeedsAmbientParticle => !Seen && ReceivedDate is null;
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
