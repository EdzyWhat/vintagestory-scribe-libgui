using Scribe.Core;

namespace Scribe.Core.Tests;

public class ScribeAssignmentTests
{
    public static IEnumerable<object[]> LegalTransitions()
    {
        yield return new object[] { ScribeAssignmentState.Unaccepted, ScribeAssignmentActor.Assignee, ScribeAssignmentAction.Accept, ScribeAssignmentState.Accepted };
        yield return new object[] { ScribeAssignmentState.Unaccepted, ScribeAssignmentActor.Assignee, ScribeAssignmentAction.Decline, ScribeAssignmentState.Declined };
        yield return new object[] { ScribeAssignmentState.Unaccepted, ScribeAssignmentActor.Assigner, ScribeAssignmentAction.Cancel, ScribeAssignmentState.Cancelled };
        yield return new object[] { ScribeAssignmentState.Accepted, ScribeAssignmentActor.Assignee, ScribeAssignmentAction.Discard, ScribeAssignmentState.Discarded };
    }

    [Theory]
    [MemberData(nameof(LegalTransitions))]
    public void LegalTransitionsAreApplied(ScribeAssignmentState state, ScribeAssignmentActor actor,
        ScribeAssignmentAction action, ScribeAssignmentState expected)
    {
        var assignment = new ScribeAssignment("assigner", "Day 1", state);
        Assert.True(ScribeAssignmentTransitions.TryApply(assignment, actor, action));
        Assert.Equal(expected, assignment.State);
    }

    [Theory]
    [InlineData(ScribeAssignmentState.Unaccepted, ScribeAssignmentActor.Assigner, ScribeAssignmentAction.Accept)]
    [InlineData(ScribeAssignmentState.Unaccepted, ScribeAssignmentActor.Assigner, ScribeAssignmentAction.Decline)]
    [InlineData(ScribeAssignmentState.Unaccepted, ScribeAssignmentActor.Assignee, ScribeAssignmentAction.Cancel)]
    [InlineData(ScribeAssignmentState.Accepted, ScribeAssignmentActor.Assigner, ScribeAssignmentAction.Cancel)]
    [InlineData(ScribeAssignmentState.Accepted, ScribeAssignmentActor.Assignee, ScribeAssignmentAction.Accept)]
    [InlineData(ScribeAssignmentState.Declined, ScribeAssignmentActor.Assignee, ScribeAssignmentAction.Discard)]
    [InlineData(ScribeAssignmentState.Cancelled, ScribeAssignmentActor.Assigner, ScribeAssignmentAction.Cancel)]
    [InlineData(ScribeAssignmentState.Discarded, ScribeAssignmentActor.Assignee, ScribeAssignmentAction.Discard)]
    [InlineData(ScribeAssignmentState.Completed, ScribeAssignmentActor.Assignee, ScribeAssignmentAction.Discard)]
    public void IllegalTransitionsAreRejected(ScribeAssignmentState state, ScribeAssignmentActor actor,
        ScribeAssignmentAction action)
    {
        var assignment = new ScribeAssignment("assigner", "Day 1", state);
        Assert.False(ScribeAssignmentTransitions.TryApply(assignment, actor, action));
        Assert.Equal(state, assignment.State);
    }

    [Fact]
    public void CompletedIsDerivedOnlyFromDoneFlag()
    {
        var assignment = new ScribeAssignment("assigner", "Day 1", ScribeAssignmentState.Accepted);
        Assert.False(ScribeAssignmentTransitions.TryMarkCompleted(assignment, false));
        Assert.Equal(ScribeAssignmentState.Accepted, assignment.State);
        Assert.True(ScribeAssignmentTransitions.TryMarkCompleted(assignment, true));
        Assert.Equal(ScribeAssignmentState.Completed, assignment.State);
    }

    [Fact]
    public void NewAssignmentsAreUnseenAndViewingMarksSeen()
    {
        var assignment = new ScribeAssignment("assigner", "Day 1");
        Assert.Equal(ScribeAssignmentState.Unaccepted, assignment.State);
        Assert.False(assignment.Seen);
        assignment.MarkSeen();
        Assert.True(assignment.Seen);
        Assert.True(ScribeAssignmentTransitions.CanApply(assignment.State,
            ScribeAssignmentActor.Assignee, ScribeAssignmentAction.Accept, out _));
    }

    [Fact]
    public void DeleteOnAcceptedIsDiscard()
    {
        var assignment = new ScribeAssignment("assigner", "Day 1", ScribeAssignmentState.Accepted);
        Assert.True(ScribeAssignmentTransitions.TryApply(assignment,
            ScribeAssignmentActor.Assignee, ScribeAssignmentAction.Discard));
        Assert.Equal(ScribeAssignmentState.Discarded, assignment.State);
    }

    [Fact]
    public void QuestLinkTargetRoundTripsAsNamespace()
    {
        string target = ScribeLinkTarget.ForQuest("chapter-one");
        Assert.Equal("quest:chapter-one", target);
        Assert.True(ScribeLinkTarget.IsQuest(target));
        Assert.Equal("chapter-one", ScribeLinkTarget.QuestCode(target));
        Assert.False(ScribeLinkTarget.IsQuest("page:chapter-one"));
    }

    [Fact]
    public void BinaryCodecRoundTripsAssignmentAndAbsentAssignment()
    {
        var original = new ScribeDocument();
        original.AddTask("unassigned");
        original.AddTask("assigned");
        original.Blocks[1].Assignment = new ScribeAssignment("player-123", "Year 1, Day 4",
            ScribeAssignmentState.Accepted, seen: true);

        Assert.True(ScribeDocumentCodec.TryDeserialize(ScribeDocumentCodec.Serialize(original), out var restored));
        Assert.NotNull(restored);
        Assert.Null(restored!.Blocks[0].Assignment);
        var assignment = restored.Blocks[1].Assignment;
        Assert.NotNull(assignment);
        Assert.Equal("player-123", assignment!.AssignerUid);
        Assert.Equal("Year 1, Day 4", assignment.AssignedDate);
        Assert.Equal(ScribeAssignmentState.Accepted, assignment.State);
        Assert.True(assignment.Seen);
    }
}
