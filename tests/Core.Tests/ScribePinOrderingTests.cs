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
}
