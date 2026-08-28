using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for the shared completion-policy semantics (reconcile-animating-surfaces D9). The pure Decide()
// table is the single definition both the server and client dispatch off; ApplyLocal() is the client's
// optimistic apply built on it. Each test maps to a policy's document consequence.
public class ScribeCompletionTests
{
    private static ScribeDocument DocWith(params string[] tasks)
    {
        var doc = new ScribeDocument();
        foreach (var t in tasks) doc.AddTask(t);
        return doc;
    }

    // --- Decide(): the pure policy table ---

    [Theory]
    [InlineData(ScribeCompletionPolicy.Delete, ScribeCompletionDocAction.Delete, true)]
    [InlineData(ScribeCompletionPolicy.Sink, ScribeCompletionDocAction.SinkToBottom, false)]
    [InlineData(ScribeCompletionPolicy.UnpinSink, ScribeCompletionDocAction.SinkToBottom, true)]
    [InlineData(ScribeCompletionPolicy.Unpin, ScribeCompletionDocAction.None, true)]
    [InlineData(ScribeCompletionPolicy.Keep, ScribeCompletionDocAction.None, false)]
    public void Decide_IntoDone_MapsPolicyToActionAndUnpin(
        ScribeCompletionPolicy policy, ScribeCompletionDocAction expectedAction, bool expectedUnpin)
    {
        var decision = ScribeCompletion.Decide(nowDone: true, policy);

        Assert.Equal(expectedAction, decision.DocAction);
        Assert.Equal(expectedUnpin, decision.ShouldRemovePin);
    }

    [Theory]
    [InlineData(ScribeCompletionPolicy.Delete)]
    [InlineData(ScribeCompletionPolicy.Sink)]
    [InlineData(ScribeCompletionPolicy.UnpinSink)]
    [InlineData(ScribeCompletionPolicy.Unpin)]
    [InlineData(ScribeCompletionPolicy.Keep)]
    public void Decide_UnChecking_IsAlwaysInert(ScribeCompletionPolicy policy)
    {
        // Unchecking (a transition OUT of done) never sinks, deletes, or unpins under any policy.
        var decision = ScribeCompletion.Decide(nowDone: false, policy);

        Assert.Equal(ScribeCompletionDocAction.None, decision.DocAction);
        Assert.False(decision.ShouldRemovePin);
    }

    // --- ApplyLocal(): the done flip ---

    [Fact]
    public void ApplyLocal_TogglesDoneIntoTrue()
    {
        var doc = DocWith("a", "b");
        var id = doc.Blocks[0].TaskId;

        var outcome = ScribeCompletion.ApplyLocal(doc, id, ScribeCompletionPolicy.Keep);

        Assert.True(outcome.Toggled);
        Assert.True(outcome.NowDone);
        Assert.True(outcome.DocChanged);
        Assert.True(doc.Blocks[0].Done);
    }

    [Fact]
    public void ApplyLocal_UncheckingADoneTask_AppliesNoPolicy()
    {
        var doc = DocWith("a", "b");
        var id = doc.Blocks[0].TaskId;
        doc.Blocks[0].Done = true; // already done; this completion un-checks it

        var outcome = ScribeCompletion.ApplyLocal(doc, id, ScribeCompletionPolicy.Delete);

        Assert.True(outcome.Toggled);
        Assert.False(outcome.NowDone);
        Assert.False(outcome.ShouldRemovePin);
        // Unchecking never deletes: the task is still present, just now incomplete.
        Assert.Equal(2, doc.Blocks.Count);
        Assert.False(doc.Blocks[0].Done);
    }

    // --- ApplyLocal(): per-policy document effect ---

    [Fact]
    public void ApplyLocal_Delete_RemovesTaskAndRequestsUnpin()
    {
        var doc = DocWith("a", "b", "c");
        var id = doc.Blocks[1].TaskId;

        var outcome = ScribeCompletion.ApplyLocal(doc, id, ScribeCompletionPolicy.Delete);

        Assert.True(outcome.DocChanged);
        Assert.True(outcome.ShouldRemovePin);
        Assert.Equal(new[] { "a", "c" }, doc.Blocks.Select(b => b.Text));
    }

    [Fact]
    public void ApplyLocal_Sink_MovesTaskToBottomAndKeepsPin()
    {
        var doc = DocWith("a", "b", "c");
        var id = doc.Blocks[0].TaskId;

        var outcome = ScribeCompletion.ApplyLocal(doc, id, ScribeCompletionPolicy.Sink);

        Assert.True(outcome.DocChanged);
        Assert.False(outcome.ShouldRemovePin);
        Assert.Equal(new[] { "b", "c", "a" }, doc.Blocks.Select(b => b.Text));
        Assert.True(doc.Blocks[2].Done); // the sunk task is the now-done one
    }

    [Fact]
    public void ApplyLocal_Sink_OnAlreadyLastTask_StillDoneStillChanged()
    {
        var doc = DocWith("a", "b");
        var id = doc.Blocks[1].TaskId; // already last

        var outcome = ScribeCompletion.ApplyLocal(doc, id, ScribeCompletionPolicy.Sink);

        // MoveTaskToBottom is a no-op (already last), but the done flip is still a content change.
        Assert.True(outcome.DocChanged);
        Assert.Equal(new[] { "a", "b" }, doc.Blocks.Select(b => b.Text));
        Assert.True(doc.Blocks[1].Done);
    }

    [Fact]
    public void ApplyLocal_UnpinSink_SinksAndRequestsUnpin()
    {
        var doc = DocWith("a", "b", "c");
        var id = doc.Blocks[0].TaskId;

        var outcome = ScribeCompletion.ApplyLocal(doc, id, ScribeCompletionPolicy.UnpinSink);

        Assert.True(outcome.DocChanged);
        Assert.True(outcome.ShouldRemovePin);
        Assert.Equal(new[] { "b", "c", "a" }, doc.Blocks.Select(b => b.Text));
    }

    [Fact]
    public void ApplyLocal_Unpin_RequestsUnpinButLeavesDocumentOrder()
    {
        var doc = DocWith("a", "b", "c");
        var id = doc.Blocks[1].TaskId;

        var outcome = ScribeCompletion.ApplyLocal(doc, id, ScribeCompletionPolicy.Unpin);

        Assert.True(outcome.ShouldRemovePin);
        Assert.Equal(new[] { "a", "b", "c" }, doc.Blocks.Select(b => b.Text));
        Assert.True(doc.Blocks[1].Done);
    }

    [Fact]
    public void ApplyLocal_Keep_LeavesDocumentInPlaceAndKeepsPin()
    {
        var doc = DocWith("a", "b", "c");
        var id = doc.Blocks[0].TaskId;

        var outcome = ScribeCompletion.ApplyLocal(doc, id, ScribeCompletionPolicy.Keep);

        Assert.False(outcome.ShouldRemovePin);
        Assert.Equal(new[] { "a", "b", "c" }, doc.Blocks.Select(b => b.Text));
        Assert.True(doc.Blocks[0].Done);
    }

    // --- ApplyLocal(): bad ids ---

    [Fact]
    public void ApplyLocal_UnknownTaskId_IsNoOp()
    {
        var doc = DocWith("a", "b");

        var outcome = ScribeCompletion.ApplyLocal(doc, System.Guid.NewGuid(), ScribeCompletionPolicy.Delete);

        Assert.False(outcome.Toggled);
        Assert.False(outcome.DocChanged);
        Assert.False(outcome.ShouldRemovePin);
        Assert.Equal(2, doc.Blocks.Count);
    }

    [Fact]
    public void ApplyLocal_NonTaskBlock_IsNoOp()
    {
        var doc = new ScribeDocument();
        doc.AddTextSection("just a note");
        var id = doc.Blocks[0].TaskId;

        var outcome = ScribeCompletion.ApplyLocal(doc, id, ScribeCompletionPolicy.Delete);

        Assert.False(outcome.Toggled);
        Assert.False(outcome.DocChanged);
        Assert.Single(doc.Blocks);
    }

    // --- Parent range (refine-crafting-tasks-1-3-2) ---

    private static ScribeDocument ParentWithTwoChildren()
    {
        var doc = new ScribeDocument();
        doc.ReplaceBlocks(new[]
        {
            new ScribeBlock(ScribeBlockKind.Task, "parent", depth: 0),
            new ScribeBlock(ScribeBlockKind.Task, "c1", depth: 1),
            new ScribeBlock(ScribeBlockKind.Task, "c2", depth: 1),
            new ScribeBlock(ScribeBlockKind.Task, "other", depth: 0),
        });
        return doc;
    }

    [Fact]
    public void Bound_Sink_KeepsParentFirstContiguity()
    {
        var doc = ParentWithTwoChildren();
        var parentId = doc.Blocks[0].TaskId;

        ScribeCompletion.ApplyLocal(doc, parentId, ScribeCompletionPolicy.Sink, ScribeSubtaskBehavior.Bound);

        Assert.Equal(new[] { "other", "parent", "c1", "c2" }, doc.Blocks.Select(b => b.Text));
        Assert.True(doc.Blocks[1].Done);
        Assert.True(doc.Blocks[2].Done);
        Assert.True(doc.Blocks[3].Done);
        Assert.Equal(0, doc.Blocks[1].Depth);
        Assert.Equal(1, doc.Blocks[2].Depth);
        Assert.Equal(1, doc.Blocks[3].Depth);
    }

    [Fact]
    public void Bound_Sink_IsNotSequentialPerRow()
    {
        // Sequential MoveTaskToBottom(child, child, parent) reverses to children-then-parent.
        // One range mutation keeps parent first. This document has a trailing row so a botched
        // per-row sink would be visible as reversed order at the bottom.
        var doc = ParentWithTwoChildren();
        var parentId = doc.Blocks[0].TaskId;

        ScribeCompletion.ApplyLocal(doc, parentId, ScribeCompletionPolicy.Sink, ScribeSubtaskBehavior.Bound);

        int parentAt = doc.IndexOf(parentId);
        Assert.Equal("parent", doc.Blocks[parentAt].Text);
        Assert.Equal("c1", doc.Blocks[parentAt + 1].Text);
        Assert.Equal("c2", doc.Blocks[parentAt + 2].Text);
        Assert.Equal(doc.Blocks.Count - 3, parentAt); // the three-row range is at the bottom, parent first
    }

    [Fact]
    public void Independent_Sink_LeavesChildrenInPlace()
    {
        var doc = ParentWithTwoChildren();
        var parentId = doc.Blocks[0].TaskId;

        ScribeCompletion.ApplyLocal(doc, parentId, ScribeCompletionPolicy.Sink, ScribeSubtaskBehavior.Independent);

        Assert.Equal(new[] { "c1", "c2", "other", "parent" }, doc.Blocks.Select(b => b.Text));
        Assert.True(doc.Blocks[3].Done);
        Assert.False(doc.Blocks[0].Done); // c1 stayed, incomplete
        Assert.False(doc.Blocks[1].Done);
    }

    [Fact]
    public void CompletingAChild_DoesNotTakeSiblings()
    {
        var doc = ParentWithTwoChildren();
        var c1 = doc.Blocks[1].TaskId;

        ScribeCompletion.ApplyLocal(doc, c1, ScribeCompletionPolicy.Sink, ScribeSubtaskBehavior.Bound);

        Assert.Equal(new[] { "parent", "c2", "other", "c1" }, doc.Blocks.Select(b => b.Text));
        Assert.False(doc.Blocks[0].Done);
        Assert.False(doc.Blocks[1].Done); // c2 unchanged
        Assert.True(doc.Blocks[3].Done);
    }

    [Fact]
    public void Discard_ThenUncheck_DoesNotRestoreChildren()
    {
        var doc = ParentWithTwoChildren();
        var parentId = doc.Blocks[0].TaskId;

        ScribeCompletion.ApplyLocal(doc, parentId, ScribeCompletionPolicy.Keep, ScribeSubtaskBehavior.DiscardChildren);
        Assert.Equal(new[] { "parent", "other" }, doc.Blocks.Select(b => b.Text));
        Assert.True(doc.Blocks[0].Done);

        ScribeCompletion.ApplyLocal(doc, parentId, ScribeCompletionPolicy.Keep, ScribeSubtaskBehavior.DiscardChildren);
        Assert.Equal(new[] { "parent", "other" }, doc.Blocks.Select(b => b.Text));
        Assert.False(doc.Blocks[0].Done); // unchecked; children stay gone
    }

    [Fact]
    public void Bound_Trash_DeletesOwnedRun()
    {
        var doc = ParentWithTwoChildren();
        var parentId = doc.Blocks[0].TaskId;

        var deleted = ScribeCompletion.ApplyDelete(doc, parentId, ScribeSubtaskBehavior.Bound);

        Assert.Equal(3, deleted.Count);
        Assert.Equal(new[] { "other" }, doc.Blocks.Select(b => b.Text));
    }

    [Fact]
    public void Independent_Trash_DeletesOnlyParent()
    {
        var doc = ParentWithTwoChildren();
        var parentId = doc.Blocks[0].TaskId;

        ScribeCompletion.ApplyDelete(doc, parentId, ScribeSubtaskBehavior.Independent);

        Assert.Equal(new[] { "c1", "c2", "other" }, doc.Blocks.Select(b => b.Text));
    }

    [Fact]
    public void Discard_Trash_DeletesOwnedRun()
    {
        var doc = ParentWithTwoChildren();
        var parentId = doc.Blocks[0].TaskId;

        ScribeCompletion.ApplyDelete(doc, parentId, ScribeSubtaskBehavior.DiscardChildren);

        Assert.Equal(new[] { "other" }, doc.Blocks.Select(b => b.Text));
    }

    [Fact]
    public void Bound_Uncheck_DoesNotUnsink()
    {
        var doc = ParentWithTwoChildren();
        var parentId = doc.Blocks[0].TaskId;
        ScribeCompletion.ApplyLocal(doc, parentId, ScribeCompletionPolicy.Sink, ScribeSubtaskBehavior.Bound);
        Assert.Equal(new[] { "other", "parent", "c1", "c2" }, doc.Blocks.Select(b => b.Text));

        ScribeCompletion.ApplyLocal(doc, parentId, ScribeCompletionPolicy.Sink, ScribeSubtaskBehavior.Bound);

        // Uncheck mirrors the run's done flags but does not move them back.
        Assert.Equal(new[] { "other", "parent", "c1", "c2" }, doc.Blocks.Select(b => b.Text));
        Assert.False(doc.Blocks[1].Done);
        Assert.False(doc.Blocks[2].Done);
        Assert.False(doc.Blocks[3].Done);
    }

    [Fact]
    public void Bound_Sink_MovesTextInTheRun()
    {
        var doc = new ScribeDocument();
        doc.ReplaceBlocks(new[]
        {
            new ScribeBlock(ScribeBlockKind.Task, "parent", depth: 0),
            new ScribeBlock(ScribeBlockKind.Text, "note", depth: 1),
            new ScribeBlock(ScribeBlockKind.Task, "c1", depth: 1),
            new ScribeBlock(ScribeBlockKind.Task, "other", depth: 0),
        });
        var parentId = doc.Blocks[0].TaskId;

        ScribeCompletion.ApplyLocal(doc, parentId, ScribeCompletionPolicy.Sink, ScribeSubtaskBehavior.Bound);

        Assert.Equal(new[] { "other", "parent", "note", "c1" }, doc.Blocks.Select(b => b.Text));
        Assert.False(doc.Blocks[2].Done); // text has no done flag
        Assert.True(doc.Blocks[1].Done);
        Assert.True(doc.Blocks[3].Done);
    }
}
