using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for the HUD display ordering: pin order, with completed (LastKnownDone) tasks sunk to the
// bottom, each group keeping its relative order. Pure/game-agnostic.
public class ScribePinOrderingTests
{
    private static ScribePinnedRef Pin(string text, bool done) => new()
    {
        OwnerDocId = Guid.NewGuid(),
        TaskId = Guid.NewGuid(),
        LastKnownText = text,
        LastKnownDone = done,
    };

    [Fact]
    public void ForDisplay_DoneTasks_SinkBelowNotDone()
    {
        var pins = new List<ScribePinnedRef>
        {
            Pin("a", done: true),
            Pin("b", done: false),
            Pin("c", done: true),
            Pin("d", done: false),
        };

        var ordered = ScribePinOrdering.ForDisplay(pins);

        Assert.Equal(new[] { "b", "d", "a", "c" }, ordered.Select(p => p.LastKnownText));
    }

    [Fact]
    public void ForDisplay_PreservesOrder_AmongNotDone()
    {
        var pins = new List<ScribePinnedRef>
        {
            Pin("first", done: false),
            Pin("second", done: false),
            Pin("third", done: false),
        };

        var ordered = ScribePinOrdering.ForDisplay(pins);

        Assert.Equal(new[] { "first", "second", "third" }, ordered.Select(p => p.LastKnownText));
    }

    [Fact]
    public void ForDisplay_PreservesOrder_AmongDone()
    {
        var pins = new List<ScribePinnedRef>
        {
            Pin("done-1", done: true),
            Pin("done-2", done: true),
            Pin("done-3", done: true),
        };

        var ordered = ScribePinOrdering.ForDisplay(pins);

        Assert.Equal(new[] { "done-1", "done-2", "done-3" }, ordered.Select(p => p.LastKnownText));
    }

    [Fact]
    public void ForDisplay_EmptyList_ReturnsEmpty()
    {
        var ordered = ScribePinOrdering.ForDisplay(new List<ScribePinnedRef>());

        Assert.Empty(ordered);
    }

    [Fact]
    public void ForDisplay_DoesNotMutateInput()
    {
        var pins = new List<ScribePinnedRef>
        {
            Pin("a", done: true),
            Pin("b", done: false),
        };

        ScribePinOrdering.ForDisplay(pins);

        // Original list order is untouched.
        Assert.Equal(new[] { "a", "b" }, pins.Select(p => p.LastKnownText));
    }

    // ---- PlaceNewPin (refine-crafting-tasks-1-3-2 D6) ----

    [Fact]
    public void PlaceNewPin_ChildUnderPinnedParent_InsertsAfterCluster()
    {
        var (doc, parent, c1, c2) = CraftFamily();
        var other = PinOn(doc, "other-task", depth: 0);
        var parentPin = PinOn(doc, parent.Text, parent.TaskId, depth: 0);
        var list = new List<ScribePinnedRef> { other, parentPin };

        var childPin = PinOn(doc, c1.Text, c1.TaskId, depth: 1);
        ScribePinOrdering.PlaceNewPin(list, childPin, doc);

        Assert.Equal(new[] { other.TaskId, parent.TaskId, c1.TaskId }, list.Select(p => p.TaskId));
    }

    [Fact]
    public void PlaceNewPin_ParentUnpinned_AppendsWithoutAutoPinningParent()
    {
        var (doc, parent, c1, _) = CraftFamily();
        var other = PinOn(doc, "other", depth: 0);
        var list = new List<ScribePinnedRef> { other };

        var childPin = PinOn(doc, c1.Text, c1.TaskId, depth: 1);
        ScribePinOrdering.PlaceNewPin(list, childPin, doc);

        Assert.Equal(new[] { other.TaskId, c1.TaskId }, list.Select(p => p.TaskId));
        Assert.DoesNotContain(list, p => p.TaskId == parent.TaskId);
    }

    [Fact]
    public void PlaceNewPin_PinningParent_GathersChildrenPreservingRelativeOrder()
    {
        var (doc, parent, c1, c2) = CraftFamily();
        var filler = PinOn(doc, "filler", depth: 0);
        var list = new List<ScribePinnedRef>
        {
            PinOn(doc, c2.Text, c2.TaskId, depth: 1),
            filler,
            PinOn(doc, c1.Text, c1.TaskId, depth: 1),
        };

        var parentPin = PinOn(doc, parent.Text, parent.TaskId, depth: 0);
        ScribePinOrdering.PlaceNewPin(list, parentPin, doc);

        Assert.Equal(new[] { filler.TaskId, parent.TaskId, c2.TaskId, c1.TaskId }, list.Select(p => p.TaskId));
    }

    [Fact]
    public void PlaceNewPin_MixedPinsFromAnotherDocument_LeftInPlace()
    {
        var (doc, parent, c1, _) = CraftFamily();
        var foreign = new ScribePinnedRef
        {
            OwnerDocId = Guid.NewGuid(),
            TaskId = Guid.NewGuid(),
            LastKnownText = "foreign",
        };
        var list = new List<ScribePinnedRef>
        {
            PinOn(doc, c1.Text, c1.TaskId, depth: 1),
            foreign,
        };

        var parentPin = PinOn(doc, parent.Text, parent.TaskId, depth: 0);
        ScribePinOrdering.PlaceNewPin(list, parentPin, doc);

        Assert.Equal(foreign.TaskId, list[0].TaskId); // foreign stays in place (was after the gathered child)
        Assert.Equal(parent.TaskId, list[1].TaskId);
        Assert.Equal(c1.TaskId, list[2].TaskId);
    }

    [Fact]
    public void PlaceNewPin_NullSource_Appends()
    {
        var list = new List<ScribePinnedRef> { Pin("already", done: false) };
        var incoming = Pin("new", done: false);
        ScribePinOrdering.PlaceNewPin(list, incoming, source: null);
        Assert.Equal(new[] { "already", "new" }, list.Select(p => p.LastKnownText));
    }

    // ---- Pin Insert edge (update-pins-1-3-3) ----

    [Fact]
    public void PlaceNewPin_NullSource_Top_InsertsAtHead()
    {
        var list = new List<ScribePinnedRef> { Pin("already", done: false) };
        var incoming = Pin("new", done: false);
        ScribePinOrdering.PlaceNewPin(list, incoming, source: null, insertEdge: ScribePinInsert.Top);
        Assert.Equal(new[] { "new", "already" }, list.Select(p => p.LastKnownText));
    }

    [Fact]
    public void PlaceNewPin_NullSource_Bottom_Appends()
    {
        var list = new List<ScribePinnedRef> { Pin("already", done: false) };
        var incoming = Pin("new", done: false);
        ScribePinOrdering.PlaceNewPin(list, incoming, source: null, insertEdge: ScribePinInsert.Bottom);
        Assert.Equal(new[] { "already", "new" }, list.Select(p => p.LastKnownText));
    }

    [Fact]
    public void PlaceNewPin_UnrelatedDepth0_Top_InsertsAtHead()
    {
        var (doc, parent, _, _) = CraftFamily();
        var other = PinOn(doc, "other-task", depth: 0);
        var list = new List<ScribePinnedRef> { other };

        var parentPin = PinOn(doc, parent.Text, parent.TaskId, depth: 0);
        ScribePinOrdering.PlaceNewPin(list, parentPin, doc, insertEdge: ScribePinInsert.Top);

        Assert.Equal(new[] { parent.TaskId, other.TaskId }, list.Select(p => p.TaskId));
    }

    [Fact]
    public void PlaceNewPin_UnrelatedDepth0_Bottom_Appends()
    {
        var (doc, parent, _, _) = CraftFamily();
        var other = PinOn(doc, "other-task", depth: 0);
        var list = new List<ScribePinnedRef> { other };

        var parentPin = PinOn(doc, parent.Text, parent.TaskId, depth: 0);
        ScribePinOrdering.PlaceNewPin(list, parentPin, doc, insertEdge: ScribePinInsert.Bottom);

        Assert.Equal(new[] { other.TaskId, parent.TaskId }, list.Select(p => p.TaskId));
    }

    [Fact]
    public void PlaceNewPin_ChildWithUnpinnedParent_Top_InsertsAtHead()
    {
        var (doc, _, c1, _) = CraftFamily();
        var other = PinOn(doc, "other", depth: 0);
        var list = new List<ScribePinnedRef> { other };

        var childPin = PinOn(doc, c1.Text, c1.TaskId, depth: 1);
        ScribePinOrdering.PlaceNewPin(list, childPin, doc, insertEdge: ScribePinInsert.Top);

        Assert.Equal(new[] { c1.TaskId, other.TaskId }, list.Select(p => p.TaskId));
    }

    [Fact]
    public void PlaceNewPin_ChildUnderPinnedParent_IgnoresPinInsertTop()
    {
        // A subtask always attaches directly under its pinned parent's cluster — Pin Insert (even Top)
        // must never pull it away to index 0.
        var (doc, parent, c1, _) = CraftFamily();
        var other = PinOn(doc, "other-task", depth: 0);
        var parentPin = PinOn(doc, parent.Text, parent.TaskId, depth: 0);
        var list = new List<ScribePinnedRef> { other, parentPin };

        var childPin = PinOn(doc, c1.Text, c1.TaskId, depth: 1);
        ScribePinOrdering.PlaceNewPin(list, childPin, doc, insertEdge: ScribePinInsert.Top);

        Assert.Equal(new[] { other.TaskId, parent.TaskId, c1.TaskId }, list.Select(p => p.TaskId));
    }

    [Fact]
    public void PlaceNewPin_PinningParent_Top_StillGathersChildren()
    {
        // The parent itself goes to Top, but already-pinned children still cluster directly under it,
        // not at the true head of the list.
        var (doc, parent, c1, c2) = CraftFamily();
        var filler = PinOn(doc, "filler", depth: 0);
        var list = new List<ScribePinnedRef>
        {
            PinOn(doc, c2.Text, c2.TaskId, depth: 1),
            filler,
            PinOn(doc, c1.Text, c1.TaskId, depth: 1),
        };

        var parentPin = PinOn(doc, parent.Text, parent.TaskId, depth: 0);
        ScribePinOrdering.PlaceNewPin(list, parentPin, doc, insertEdge: ScribePinInsert.Top);

        Assert.Equal(new[] { parent.TaskId, c2.TaskId, c1.TaskId, filler.TaskId }, list.Select(p => p.TaskId));
    }

    // ---- Reorder (same-depth-reorder) ----

    [Fact]
    public void Reorder_SameDepthPins_Moves()
    {
        var a = Pin("a", done: false);
        var b = Pin("b", done: false);
        var c = Pin("c", done: false);
        var list = new List<ScribePinnedRef> { a, b, c };

        bool changed = ScribePinOrdering.Reorder(list, from: 0, to: 2);

        Assert.True(changed);
        Assert.Equal(new[] { b.TaskId, c.TaskId, a.TaskId }, list.Select(p => p.TaskId));
    }

    [Fact]
    public void Reorder_ParentWithChildren_MovingForwardOntoAnotherParentWithChildren_KeepsBothClustersIntact()
    {
        // Regression: dragging an EARLIER pinned parent (with its own pinned child) forward past a
        // LATER pinned parent that ALSO has a pinned child must not wedge the dragged cluster between
        // the target parent and its child -- both clusters must stay intact and contiguous.
        var parentA = Pin("parentA", done: false);
        var childA1 = Pin("childA1", done: false);
        childA1.Depth = 1;
        var parentB = Pin("parentB", done: false);
        var childB1 = Pin("childB1", done: false);
        childB1.Depth = 1;
        var list = new List<ScribePinnedRef> { parentA, childA1, parentB, childB1 };

        // Drag parentA's cluster [0,2) forward, dropping on parentB (index 2).
        bool changed = ScribePinOrdering.Reorder(list, from: 0, to: 2);

        Assert.True(changed);
        Assert.Equal(new[] { parentB.TaskId, childB1.TaskId, parentA.TaskId, childA1.TaskId },
            list.Select(p => p.TaskId));
    }

    [Fact]
    public void Reorder_CrossDepth_Rejected()
    {
        var (doc, parent, c1, _) = CraftFamily();
        var parentPin = PinOn(doc, parent.Text, parent.TaskId, depth: 0);
        var childPin = PinOn(doc, c1.Text, c1.TaskId, depth: 1);
        var list = new List<ScribePinnedRef> { parentPin, childPin };

        // Drag the depth-1 child onto the depth-0 parent: invalid.
        bool changed = ScribePinOrdering.Reorder(list, from: 1, to: 0);

        Assert.False(changed);
        Assert.Equal(new[] { parentPin.TaskId, childPin.TaskId }, list.Select(p => p.TaskId));
    }

    [Fact]
    public void Reorder_CrossDepth_OtherDirection_Rejected()
    {
        var (doc, _, c1, _) = CraftFamily();
        // The depth-1 pin sits at index 0, NOT immediately after "other" -- so it isn't swept into
        // "other"'s cluster by adjacency (Cluster() is purely positional, like ScribeDocument.OwnedRun).
        var unrelatedChild = PinOn(doc, c1.Text, c1.TaskId, depth: 1);
        var other = Pin("other", done: false);
        var list = new List<ScribePinnedRef> { unrelatedChild, other };

        // Drag the depth-0 "other" pin (index 1) onto the unrelated depth-1 pin (index 0): invalid.
        bool changed = ScribePinOrdering.Reorder(list, from: 1, to: 0);

        Assert.False(changed);
        Assert.Equal(new[] { unrelatedChild.TaskId, other.TaskId }, list.Select(p => p.TaskId));
    }

    [Fact]
    public void Reorder_PinnedParentWithChildren_MovesClusterTogether()
    {
        var (doc, parent, c1, c2) = CraftFamily();
        var parentPin = PinOn(doc, parent.Text, parent.TaskId, depth: 0);
        var childPin1 = PinOn(doc, c1.Text, c1.TaskId, depth: 1);
        var childPin2 = PinOn(doc, c2.Text, c2.TaskId, depth: 1);
        var sibling = Pin("sibling", done: false);
        var list = new List<ScribePinnedRef> { parentPin, childPin1, childPin2, sibling };

        // Drag the parent (cluster [0,3)) past the sibling depth-0 pin at index 3.
        bool changed = ScribePinOrdering.Reorder(list, from: 0, to: 3);

        Assert.True(changed);
        Assert.Equal(new[] { sibling.TaskId, parentPin.TaskId, childPin1.TaskId, childPin2.TaskId },
            list.Select(p => p.TaskId));
    }

    [Fact]
    public void Reorder_DropOnOwnCluster_IsNoOp()
    {
        var (doc, parent, c1, _) = CraftFamily();
        var parentPin = PinOn(doc, parent.Text, parent.TaskId, depth: 0);
        var childPin = PinOn(doc, c1.Text, c1.TaskId, depth: 1);
        var list = new List<ScribePinnedRef> { parentPin, childPin };

        bool changed = ScribePinOrdering.Reorder(list, from: 0, to: 1);

        Assert.False(changed);
        Assert.Equal(new[] { parentPin.TaskId, childPin.TaskId }, list.Select(p => p.TaskId));
    }

    [Fact]
    public void Reorder_SameIndex_IsNoOp()
    {
        var list = new List<ScribePinnedRef> { Pin("a", done: false), Pin("b", done: false) };

        bool changed = ScribePinOrdering.Reorder(list, from: 0, to: 0);

        Assert.False(changed);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 5)]
    public void Reorder_OutOfRangeIndices_IsNoOp(int from, int to)
    {
        var list = new List<ScribePinnedRef> { Pin("a", done: false), Pin("b", done: false) };

        bool changed = ScribePinOrdering.Reorder(list, from, to);

        Assert.False(changed);
    }

    private static (ScribeDocument Doc, ScribeBlock Parent, ScribeBlock C1, ScribeBlock C2) CraftFamily()
    {
        var parent = new ScribeBlock(ScribeBlockKind.Craft, "craft", depth: 0);
        var c1 = new ScribeBlock(ScribeBlockKind.Tracker, "oak", depth: 1, targetItemCode: "game:log-oak");
        var c2 = new ScribeBlock(ScribeBlockKind.Tracker, "resin", depth: 1, targetItemCode: "game:resin");
        var doc = new ScribeDocument();
        doc.ReplaceBlocks(new[] { parent, c1, c2 });
        return (doc, parent, c1, c2);
    }

    private static ScribePinnedRef PinOn(ScribeDocument doc, string text, Guid? taskId = null, int depth = 0) => new()
    {
        OwnerDocId = doc.DocId,
        TaskId = taskId ?? Guid.NewGuid(),
        LastKnownText = text,
        Depth = depth,
    };
}
