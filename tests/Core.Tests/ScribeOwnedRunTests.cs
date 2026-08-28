using Scribe.Core;

namespace Scribe.Core.Tests;

// Owned-run scan (refine-crafting-tasks-1-3-2): a parent is any depth-0 row plus the contiguous
// depth-1 rows under it. Kind-agnostic. The first non-depth-1 row ends the run.
public class ScribeOwnedRunTests
{
    [Fact]
    public void OwnedRun_ContiguousChildren_ExcludesParent()
    {
        var doc = Doc(
            Depth0("parent"),
            Depth1("a"),
            Depth1("b"),
            Depth0("next"));

        var (start, end) = doc.OwnedRun(0);

        Assert.Equal(1, start);
        Assert.Equal(3, end);
        Assert.Equal(new[] { "a", "b" }, Slice(doc, start, end));
    }

    [Fact]
    public void OwnedRun_Depth0Gap_EndsTheRun()
    {
        var doc = Doc(
            Depth0("craft"),
            Depth1("oak"),
            Depth0("gap"),
            Depth1("orphan"));

        var (start, end) = doc.OwnedRun(0);

        Assert.Equal(1, start);
        Assert.Equal(2, end);
        Assert.Equal(new[] { "oak" }, Slice(doc, start, end));
        // The depth-1 after the gap belongs to the gap row, not the first parent.
        var afterGap = doc.OwnedRun(2);
        Assert.Equal(3, afterGap.Start);
        Assert.Equal(4, afterGap.End);
    }

    [Fact]
    public void OwnedRun_ShuffleAmongDepth1_StillTheSameRun()
    {
        var doc = Doc(
            Depth0("parent"),
            Depth1("resin"),
            Depth1("oak"),
            Depth1("note"));

        var (start, end) = doc.OwnedRun(0);

        Assert.Equal(1, start);
        Assert.Equal(4, end);
        Assert.Equal(3, end - start);
    }

    [Fact]
    public void OwnedRun_Empty_WhenNoChildren()
    {
        var doc = Doc(Depth0("lonely"), Depth0("also"));

        var (start, end) = doc.OwnedRun(0);

        Assert.Equal(1, start);
        Assert.Equal(1, end);
        Assert.Empty(Slice(doc, start, end));
    }

    [Fact]
    public void OwnedRun_LastRow_IsEmpty()
    {
        var doc = Doc(Depth0("only"));

        var (start, end) = doc.OwnedRun(0);

        Assert.Equal(1, start);
        Assert.Equal(1, end);
    }

    [Fact]
    public void FindParentIndex_WalksBackToOwningDepth0()
    {
        var doc = Doc(
            Depth0("a"),
            Depth1("a1"),
            Depth0("b"),
            Depth1("b1"));

        Assert.Equal(0, doc.FindParentIndex(1));
        Assert.Equal(2, doc.FindParentIndex(3));
        Assert.Equal(-1, doc.FindParentIndex(0)); // depth-0 is not a child
    }

    [Fact]
    public void MoveRange_MatchesMoveBlock_WhenLengthIsOne()
    {
        var a = Doc(Depth0("A"), Depth0("B"), Depth0("C"));
        var b = Doc(Depth0("A"), Depth0("B"), Depth0("C"));
        a.MoveBlock(0, 2);
        b.MoveRange(0, 1, 2);
        Assert.Equal(a.Blocks.Select(x => x.Text), b.Blocks.Select(x => x.Text));
    }

    [Fact]
    public void MoveRange_ParentTakesChildren_WhenMovingDown()
    {
        var doc = Doc(
            Depth0("parent"),
            Depth1("a"),
            Depth1("b"),
            Depth0("x"),
            Depth0("y"));

        Assert.True(doc.MoveRange(0, 3, 4));
        Assert.Equal(new[] { "x", "y", "parent", "a", "b" }, doc.Blocks.Select(x => x.Text));
        Assert.Equal(new[] { 0, 0, 0, 1, 1 }, doc.Blocks.Select(x => x.Depth));
    }

    [Fact]
    public void MoveRange_ParentTakesChildren_WhenMovingUp()
    {
        var doc = Doc(
            Depth0("x"),
            Depth0("parent"),
            Depth1("a"),
            Depth1("b"));

        Assert.True(doc.MoveRange(1, 4, 0));
        Assert.Equal(new[] { "parent", "a", "b", "x" }, doc.Blocks.Select(x => x.Text));
    }

    [Fact]
    public void MoveRange_DropOnOwnChild_IsNoOp()
    {
        var doc = Doc(
            Depth0("parent"),
            Depth1("a"),
            Depth1("b"),
            Depth0("x"));

        Assert.True(doc.MoveRange(0, 3, 1));
        Assert.Equal(new[] { "parent", "a", "b", "x" }, doc.Blocks.Select(x => x.Text));
    }

    private static ScribeDocument Doc(params ScribeBlock[] blocks)
    {
        var doc = new ScribeDocument();
        doc.ReplaceBlocks(blocks);
        return doc;
    }

    private static ScribeBlock Depth0(string text) => new(ScribeBlockKind.Task, text, depth: 0);
    private static ScribeBlock Depth1(string text) => new(ScribeBlockKind.Task, text, depth: 1);

    private static string[] Slice(ScribeDocument doc, int start, int end)
        => doc.Blocks.Skip(start).Take(end - start).Select(b => b.Text).ToArray();
}
