namespace Scribe.Core;

/// <summary>
/// Shared same-depth drag-reorder rule (same-depth-reorder), used by both the document editor's
/// block reorder and the Pin Tab's pin reorder so the grip-handle's drop-target arrow (the
/// player-visible signal) and the commit logic that actually moves rows can never disagree. Works
/// over a plain list of <c>Depth</c> values rather than a <see cref="ScribeDocument"/> or a pin
/// list, so one implementation serves both surfaces.
/// </summary>
public static class ScribeReorderValidity
{
    /// <summary>
    /// The half-open range <c>[Start, End)</c> that moves together when a drag starts at
    /// <paramref name="fromIndex"/>. A depth-0 row's cluster is itself plus the contiguous run of
    /// depth-1 rows immediately following it (mirrors <see cref="ScribeDocument.OwnedRun"/>, but over
    /// any depths list). A depth-1 row's cluster is itself alone — a leaf never carries siblings.
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
    /// is a valid same-depth reorder target. Valid when <paramref name="toIndex"/> sits inside the
    /// dragged row's own cluster (a harmless no-op the caller resolves separately, e.g. dropping a
    /// parent back onto one of its own children) or has the same <c>Depth</c> as the dragged row
    /// (<c>depths[clusterStart]</c>). Any depth mismatch outside the cluster is invalid: a depth-0
    /// row can only reorder among other depth-0 rows, a depth-1 row only among other depth-1 rows.
    /// </summary>
    public static bool IsValidDropTarget(IReadOnlyList<int> depths, int clusterStart, int clusterEnd, int toIndex)
    {
        ArgumentNullException.ThrowIfNull(depths);
        if (toIndex < 0 || toIndex >= depths.Count) return false;
        if (toIndex >= clusterStart && toIndex < clusterEnd) return true;
        return depths[toIndex] == depths[clusterStart];
    }

    /// <summary>
    /// The destination index to actually pass to a remove-then-insert-style move (e.g.
    /// <see cref="ScribeDocument.MoveRange"/>) so a same-depth drop never wedges the dragged row/cluster
    /// into the middle of the <b>target's own</b> cluster. Call only with an already-validated
    /// <paramref name="toIndex"/> (see <see cref="IsValidDropTarget"/>) — never a value inside
    /// <paramref name="clusterStart"/>'s own cluster.
    ///
    /// Dropping "on" a row that owns trailing rows (e.g. a depth-0 parent with depth-1 children) must
    /// land the dragged row/cluster after the target's <b>entire</b> cluster when moving forward
    /// (<paramref name="toIndex"/> after <paramref name="clusterStart"/>) — landing right after only
    /// the target's own single row would insert between the target and its children, splitting it.
    /// Moving backward (<paramref name="toIndex"/> before <paramref name="clusterStart"/>) already
    /// lands correctly before the target's whole cluster with no adjustment needed: the slice inserts
    /// at the target's untouched original index, carrying the target and everything after it —
    /// including its children — forward together.
    /// </summary>
    public static int ResolveDestination(IReadOnlyList<int> depths, int clusterStart, int toIndex)
    {
        ArgumentNullException.ThrowIfNull(depths);
        if (toIndex < clusterStart) return toIndex;
        var (_, targetClusterEnd) = Cluster(depths, toIndex);
        return targetClusterEnd - 1;
    }
}
