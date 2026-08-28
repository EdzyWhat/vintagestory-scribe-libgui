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
