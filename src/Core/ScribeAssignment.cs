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
}

/// <summary>The actor requesting an assignment transition.</summary>
public enum ScribeAssignmentActor : byte
{
    Assigner = 0,
    Assignee = 1,
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

    public ScribeAssignment(string assignerUid, string assignedDate,
        ScribeAssignmentState state = ScribeAssignmentState.Unaccepted, bool seen = false,
        string? targetPlayerUid = null)
    {
        AssignerUid = assignerUid ?? "";
        TargetPlayerUid = targetPlayerUid ?? "";
        AssignedDate = assignedDate ?? "";
        State = state;
        Seen = seen;
    }

    public ScribeAssignment Clone() => new(AssignerUid, AssignedDate, State, Seen, TargetPlayerUid);

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
