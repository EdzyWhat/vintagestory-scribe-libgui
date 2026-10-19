namespace Scribe.Core;

/// <summary>
/// Shared same-depth drag-reorder rule for the editor's block reorder and the Pin Tab's pin reorder, so
/// the drop-target arrow and the commit logic cannot disagree. Works over a plain list of <c>Depth</c>
/// values so one implementation serves both surfaces.
/// </summary>
public static class ScribeReorderValidity
{
    /// <summary>
    /// The half-open range <c>[Start, End)</c> that moves together when a drag starts at
    /// <paramref name="fromIndex"/>. A depth-0 row's cluster is itself plus the contiguous depth-1 rows
    /// after it (as <see cref="ScribeDocument.OwnedRun"/>); a depth-1 row's cluster is itself alone.
    /// Returns <c>(fromIndex, fromIndex)</c> for an out-of-range index.
    /// </summary>
    public static (int Start, int End) Cluster(IReadOnlyList<int> depths, int fromIndex)
    {
        ArgumentNullException.ThrowIfNull(depths);
        if (fromIndex < 0 || fromIndex >= depths.Count) return (fromIndex, fromIndex);

        if (depths[fromIndex] != 0) return (fromIndex, fromIndex + 1);

        int end = fromIndex + 1;
        while (end < depths.Count && depths[end] == 1) end++;
        return (fromIndex, end);
    }

    /// <summary>
    /// Whether dropping the cluster <c>[clusterStart, clusterEnd)</c> onto <paramref name="toIndex"/>
    /// is a valid same-depth target: inside the dragged cluster (a no-op the caller resolves separately)
    /// or at the same depth as <c>depths[clusterStart]</c>. Any other depth is invalid.
    /// </summary>
    public static bool IsValidDropTarget(IReadOnlyList<int> depths, int clusterStart, int clusterEnd, int toIndex)
    {
        ArgumentNullException.ThrowIfNull(depths);
        if (toIndex < 0 || toIndex >= depths.Count) return false;
        if (toIndex >= clusterStart && toIndex < clusterEnd) return true;
        return depths[toIndex] == depths[clusterStart];
    }

    /// <summary>
    /// The destination index to pass to a remove-then-insert move (e.g.
    /// <see cref="ScribeDocument.MoveRange"/>) so a drop never splits the <b>target's own</b> cluster.
    /// Call only with a validated <paramref name="toIndex"/> (see <see cref="IsValidDropTarget"/>) outside
    /// the dragged cluster.
    ///
    /// Moving forward lands after the target's <b>entire</b> cluster, not between it and its children.
    /// Moving backward needs no adjustment: the slice inserts at the target's original index.
    /// </summary>
    public static int ResolveDestination(IReadOnlyList<int> depths, int clusterStart, int toIndex)
    {
        ArgumentNullException.ThrowIfNull(depths);
        if (toIndex < clusterStart) return toIndex;
        var (_, targetClusterEnd) = Cluster(depths, toIndex);
        return targetClusterEnd - 1;
    }
}
