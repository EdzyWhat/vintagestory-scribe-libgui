using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for the pure identity diff behind ScribeAnimatedList (extract-animated-task-list): given the
// previous rendered order, which of those were live, the incoming live order, and the ghosts still
// animating, it computes departures (+ their slots), revivals, appearances, and the full spliced render
// order. Pure/game-agnostic — no LibGUI, no widgets. Guids are made deterministic per-name so a scenario
// reads by letter.
public class ScribeListDiffTests
{
    // Stable, deterministic Guids keyed by a single letter so tests read like the on-screen row order.
    private static Guid Id(char c) => new Guid(c, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static List<Guid> Ids(string letters) => letters.Select(Id).ToList();

    private static ScribeListDiffResult Compute(
        string prevRender, string prevLive, string newLive, params (char id, int slot)[] ghosts)
        => ScribeListDiff.Compute(
            Ids(prevRender),
            Ids(prevLive).ToHashSet(),
            Ids(newLive),
            ghosts.ToDictionary(g => Id(g.id), g => g.slot));

    private static string Render(ScribeListDiffResult r) =>
        new string(r.RenderOrder.Select(id => id.ToByteArray()[0] == 0 ? '?' : (char)id.ToByteArray()[0]).ToArray());

    // ---- Departures ----

    [Fact]
    public void MiddleRowDeparts_CollapsesAtItsSlot_OthersStayLive()
    {
        // "abc" all live; "b" removed → b departs at slot 1, render order keeps all three (b as ghost).
        var r = Compute(prevRender: "abc", prevLive: "abc", newLive: "ac");

        Assert.Single(r.Departed);
        Assert.Equal(Id('b'), r.Departed[0].Id);
        Assert.Equal(1, r.Departed[0].Slot);
        Assert.Equal("abc", Render(r)); // ghost b spliced back at slot 1
        Assert.Empty(r.Revived);
        Assert.Empty(r.Appeared);
    }

    [Fact]
    public void BottomRowDeparts_CollapsesAtEnd()
    {
        var r = Compute(prevRender: "abc", prevLive: "abc", newLive: "ab");

        Assert.Single(r.Departed);
        Assert.Equal(Id('c'), r.Departed[0].Id);
        Assert.Equal(2, r.Departed[0].Slot);
        Assert.Equal("abc", Render(r));
    }

    [Fact]
    public void NothingChanged_NoDepartures()
    {
        var r = Compute(prevRender: "abc", prevLive: "abc", newLive: "abc");

        Assert.Empty(r.Departed);
        Assert.Empty(r.Revived);
        Assert.Empty(r.Appeared);
        Assert.Equal("abc", Render(r));
    }

    // ---- Slot-order preservation for simultaneous departures ----

    [Fact]
    public void TwoAdjacentRowsDepartAtOnce_EachKeepsItsOwnSlot()
    {
        // "abcd" live; "b" and "c" both removed same frame → each collapses in place at its old slot.
        var r = Compute(prevRender: "abcd", prevLive: "abcd", newLive: "ad");

        Assert.Equal(2, r.Departed.Count);
        Assert.Equal(Id('b'), r.Departed[0].Id);
        Assert.Equal(1, r.Departed[0].Slot);
        Assert.Equal(Id('c'), r.Departed[1].Id);
        Assert.Equal(2, r.Departed[1].Slot);
        // Live rows a,d in order with ghosts b,c spliced at slots 1,2 → original order restored.
        Assert.Equal("abcd", Render(r));
    }

    [Fact]
    public void DepartureWhileAGhostAlreadyCollapses_LaterSlotAccountsForTheGhost()
    {
        // Frame 1 already produced ghost "b" at slot 1 (prevRender "abc" includes it, but b is NOT live).
        // Now "c" departs. c's slot is its index in prevRender (2) — the pre-existing ghost pushes it down,
        // exactly the editor's display-index math. Both ghosts render at their slots.
        var r = Compute(prevRender: "abc", prevLive: "ac", newLive: "a", ghosts: ('b', 1));

        Assert.Single(r.Departed);
        Assert.Equal(Id('c'), r.Departed[0].Id);
        Assert.Equal(2, r.Departed[0].Slot);
        Assert.Equal("abc", Render(r)); // live a, then ghosts b@1, c@2
        // b is not re-reported as a departure (it's already a ghost).
        Assert.DoesNotContain(r.Departed, d => d.Id == Id('b'));
    }

    // ---- Reappear-mid-collapse revival ----

    [Fact]
    public void GhostIdReappears_IsRevived_NotDoubleRendered()
    {
        // "b" is mid-collapse (ghost at slot 1) and its id comes back into the live set → revive it.
        var r = Compute(prevRender: "abc", prevLive: "ac", newLive: "abc", ghosts: ('b', 1));

        Assert.Contains(Id('b'), r.Revived);
        Assert.Empty(r.Departed);
        // b renders exactly once, as a live row (it's in newLive); the ghost slot is dropped.
        Assert.Equal("abc", Render(r));
        Assert.Single(r.RenderOrder, id => id == Id('b'));
    }

    [Fact]
    public void RevivedRowAtNewPosition_RendersLiveOnce()
    {
        // b was a ghost at slot 1 but reappears live at the END of the new order → one live b at the end,
        // no ghost, no duplicate.
        var r = Compute(prevRender: "abc", prevLive: "ac", newLive: "acb", ghosts: ('b', 1));

        Assert.Contains(Id('b'), r.Revived);
        Assert.Equal("acb", Render(r));
        Assert.Single(r.RenderOrder, id => id == Id('b'));
    }

    // ---- Appeared seam ----

    [Fact]
    public void BrandNewRow_IsReportedAppeared()
    {
        // "d" is present now, was neither live last frame nor a ghost → appeared.
        var r = Compute(prevRender: "abc", prevLive: "abc", newLive: "abcd");

        Assert.Equal(new[] { Id('d') }, r.Appeared);
        Assert.Empty(r.Departed);
        Assert.Equal("abcd", Render(r));
    }

    [Fact]
    public void DepartureAndAppearanceSameFrame_BothReportedDisjoint()
    {
        // "b" leaves, "d" arrives in one frame.
        var r = Compute(prevRender: "abc", prevLive: "abc", newLive: "acd");

        Assert.Equal(new[] { Id('b') }, r.Departed.Select(d => d.Id));
        Assert.Equal(new[] { Id('d') }, r.Appeared);
        // live a,c,d in order; ghost b spliced at slot 1.
        Assert.Equal("abcd", Render(r));
    }

    // ---- Edge: from empty / to empty ----

    [Fact]
    public void FirstBuild_AllRowsAppear_NoDepartures()
    {
        var r = Compute(prevRender: "", prevLive: "", newLive: "abc");

        Assert.Equal(Ids("abc"), r.Appeared);
        Assert.Empty(r.Departed);
        Assert.Equal("abc", Render(r));
    }

    [Fact]
    public void LastRowDeparts_ToEmpty_StillRendersTheCollapsingGhost()
    {
        var r = Compute(prevRender: "a", prevLive: "a", newLive: "");

        Assert.Single(r.Departed);
        Assert.Equal(Id('a'), r.Departed[0].Id);
        Assert.Equal(0, r.Departed[0].Slot);
        Assert.Equal("a", Render(r)); // the ghost still occupies the list until it finishes collapsing
    }
}
