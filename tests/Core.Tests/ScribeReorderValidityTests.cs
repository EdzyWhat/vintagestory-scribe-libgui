using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for the same-depth drag-reorder rule shared by the editor's block reorder and the Pin
// Tab's pin reorder (same-depth-reorder). Pure/game-agnostic: operates on plain depth lists.
public class ScribeReorderValidityTests
{
    [Fact]
    public void Cluster_Depth0WithOwnedChildren_IncludesContiguousRun()
    {
        var depths = new[] { 0, 1, 1, 0 };

        var (start, end) = ScribeReorderValidity.Cluster(depths, fromIndex: 0);

        Assert.Equal((0, 3), (start, end));
    }

    [Fact]
    public void Cluster_Depth0WithNoOwnedChildren_IsSingleRow()
    {
        var depths = new[] { 0, 0, 1 };

        var (start, end) = ScribeReorderValidity.Cluster(depths, fromIndex: 0);

        Assert.Equal((0, 1), (start, end));
    }

    [Fact]
    public void Cluster_Depth1Leaf_IsSingleRowEvenAmongSiblings()
    {
        var depths = new[] { 0, 1, 1, 0 };

        var (start, end) = ScribeReorderValidity.Cluster(depths, fromIndex: 1);

        Assert.Equal((1, 2), (start, end));
    }

    [Fact]
    public void Cluster_OutOfRangeIndex_ReturnsEmptyRangeAtIndex()
    {
        var depths = new[] { 0, 1 };

        var (start, end) = ScribeReorderValidity.Cluster(depths, fromIndex: 5);

        Assert.Equal((5, 5), (start, end));
    }

    [Fact]
    public void IsValidDropTarget_SameDepthTarget_IsValid()
    {
        var depths = new[] { 0, 1, 1, 0 };

        // Dragging the depth-1 row at index 1 (single-row cluster [1,2)) onto the other depth-1 row at 2.
        Assert.True(ScribeReorderValidity.IsValidDropTarget(depths, clusterStart: 1, clusterEnd: 2, toIndex: 2));
    }

    [Fact]
    public void IsValidDropTarget_CrossDepthTarget_IsInvalid()
    {
        var depths = new[] { 0, 1, 1, 0 };

        // Dragging the depth-1 row at index 1 onto a depth-0 row at 0 or 3.
        Assert.False(ScribeReorderValidity.IsValidDropTarget(depths, clusterStart: 1, clusterEnd: 2, toIndex: 0));
        Assert.False(ScribeReorderValidity.IsValidDropTarget(depths, clusterStart: 1, clusterEnd: 2, toIndex: 3));
    }

    [Fact]
    public void IsValidDropTarget_Depth0ClusterOntoUnrelatedDepth1_IsInvalid()
    {
        var depths = new[] { 0, 1, 0, 1 };

        // Dragging the parent cluster [0,2) onto the unrelated depth-1 row at index 3.
        Assert.False(ScribeReorderValidity.IsValidDropTarget(depths, clusterStart: 0, clusterEnd: 2, toIndex: 3));
    }

    [Fact]
    public void IsValidDropTarget_DropInsideOwnCluster_IsValidNoOp()
    {
        var depths = new[] { 0, 1, 1, 0 };

        // Dragging the parent cluster [0,3) and releasing on one of its own children (index 1 or 2).
        Assert.True(ScribeReorderValidity.IsValidDropTarget(depths, clusterStart: 0, clusterEnd: 3, toIndex: 1));
        Assert.True(ScribeReorderValidity.IsValidDropTarget(depths, clusterStart: 0, clusterEnd: 3, toIndex: 2));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void IsValidDropTarget_OutOfRangeToIndex_IsInvalid(int toIndex)
    {
        var depths = new[] { 0, 1, 1, 0 };

        Assert.False(ScribeReorderValidity.IsValidDropTarget(depths, clusterStart: 0, clusterEnd: 1, toIndex));
    }

    // ---- ResolveDestination ----

    [Fact]
    public void ResolveDestination_MovingForwardOntoParentWithChildren_LandsAfterWholeTargetCluster()
    {
        // [ParentA, ChildA1, ChildA2, ParentB, ChildB1] -- drag ParentA's cluster [0,3) forward onto
        // ParentB (index 3). Landing right after ParentB's own row (index 3) would wedge ParentA's
        // cluster between ParentB and ChildB1; it must land after ChildB1 instead (index 4).
        var depths = new[] { 0, 1, 1, 0, 1 };

        int destination = ScribeReorderValidity.ResolveDestination(depths, clusterStart: 0, toIndex: 3);

        Assert.Equal(4, destination);
    }

    [Fact]
    public void ResolveDestination_MovingBackward_IsUnchanged()
    {
        // [ParentA, ChildA1, ParentB, ChildB1, ChildB2] -- drag ParentB's cluster [2,5) backward onto
        // ParentA (index 0). Landing at ParentA's own index already carries ParentA (and its child)
        // forward together, so no adjustment is needed.
        var depths = new[] { 0, 1, 0, 1, 1 };

        int destination = ScribeReorderValidity.ResolveDestination(depths, clusterStart: 2, toIndex: 0);

        Assert.Equal(0, destination);
    }

    [Fact]
    public void ResolveDestination_TargetHasNoChildren_IsUnchanged()
    {
        var depths = new[] { 0, 1, 0 };

        int destination = ScribeReorderValidity.ResolveDestination(depths, clusterStart: 0, toIndex: 2);

        Assert.Equal(2, destination);
    }

    [Fact]
    public void ResolveDestination_Depth1TargetLeaf_IsUnchangedEvenAdjacentToOtherRows()
    {
        var depths = new[] { 0, 1, 1, 0 };

        // A depth-1 leaf target is always a single-row cluster, so no adjustment ever applies.
        int destination = ScribeReorderValidity.ResolveDestination(depths, clusterStart: 1, toIndex: 2);

        Assert.Equal(2, destination);
    }
}
