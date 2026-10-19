namespace Scribe.Core;

/// <summary>
/// The HUD's display order for a player's pins: pin order with completed tasks sunk below the others.
/// Pure and shared by any surface that renders the pin set; this defines only the resting order.
/// </summary>
public static class ScribePinOrdering
{
    /// <summary>
    /// Returns <paramref name="pins"/> reordered for display: not-done pins first, then done pins, each group
    /// keeping input order. Done-ness is read from <see cref="ScribePinnedRef.LastKnownDone"/>, the same
    /// snapshot the HUD renders. The input is not mutated.
    /// </summary>
    public static IReadOnlyList<ScribePinnedRef> ForDisplay(IReadOnlyList<ScribePinnedRef> pins)
    {
        ArgumentNullException.ThrowIfNull(pins);

        var ordered = new List<ScribePinnedRef>(pins.Count);
        foreach (var pin in pins)
        {
            if (!pin.LastKnownDone) ordered.Add(pin);
        }
        foreach (var pin in pins)
        {
            if (pin.LastKnownDone) ordered.Add(pin);
        }
        return ordered;
    }

    /// <summary>
    /// Inserts <paramref name="newPin"/> into <paramref name="pins"/>. Depth-0: insert at
    /// <paramref name="insertEdge"/>, then gather already-pinned children in that parent's owned run. Depth-1:
    /// if its document parent is pinned, insert after that parent's contiguous cluster (ignoring
    /// <paramref name="insertEdge"/>); otherwise insert at the edge. Never auto-pins the parent. An unresolvable
    /// source also inserts at the edge. The caller has already checked uniqueness and the pin cap.
    /// </summary>
    public static void PlaceNewPin(List<ScribePinnedRef> pins, ScribePinnedRef newPin, ScribeDocument? source,
        ScribePinInsert insertEdge = ScribePinInsert.Bottom)
    {
        ArgumentNullException.ThrowIfNull(pins);
        ArgumentNullException.ThrowIfNull(newPin);

        if (source is null)
        {
            InsertAtEdge(pins, newPin, insertEdge);
            return;
        }

        int idx = source.IndexOf(newPin.TaskId);
        if (idx < 0)
        {
            InsertAtEdge(pins, newPin, insertEdge);
            return;
        }

        var block = source.Blocks[idx];
        if (block.Depth == 0)
        {
            InsertAtEdge(pins, newPin, insertEdge);
            GatherOwnedRunChildren(pins, newPin, source, idx);
            return;
        }

        int parentIdx = source.FindParentIndex(idx);
        if (parentIdx < 0)
        {
            InsertAtEdge(pins, newPin, insertEdge);
            return;
        }

        Guid parentTaskId = source.Blocks[parentIdx].TaskId;
        int parentPinIdx = IndexOfPin(pins, newPin.OwnerDocId, parentTaskId);
        if (parentPinIdx < 0)
        {
            InsertAtEdge(pins, newPin, insertEdge);
            return;
        }

        var ownedIds = OwnedRunTaskIds(source, parentIdx);
        int insertAt = parentPinIdx + 1;
        while (insertAt < pins.Count
            && pins[insertAt].OwnerDocId == newPin.OwnerDocId
            && ownedIds.Contains(pins[insertAt].TaskId))
        {
            insertAt++;
        }
        pins.Insert(insertAt, newPin);
    }

    /// <summary>Inserts a pin with no pinned parent at the player's chosen edge.</summary>
    private static void InsertAtEdge(List<ScribePinnedRef> pins, ScribePinnedRef newPin, ScribePinInsert insertEdge)
    {
        if (insertEdge == ScribePinInsert.Top) pins.Insert(0, newPin);
        else pins.Add(newPin);
    }

    /// <summary>Pulls already-pinned owned-run children to sit right after the new parent pin, keeping their relative order.</summary>
    private static void GatherOwnedRunChildren(List<ScribePinnedRef> pins, ScribePinnedRef parentPin,
        ScribeDocument source, int parentBlockIndex)
    {
        var ownedIds = OwnedRunTaskIds(source, parentBlockIndex);
        if (ownedIds.Count == 0) return;

        var gathered = new List<ScribePinnedRef>();
        for (int i = 0; i < pins.Count; i++)
        {
            var p = pins[i];
            if (p.OwnerDocId == parentPin.OwnerDocId && ownedIds.Contains(p.TaskId))
                gathered.Add(p);
        }
        if (gathered.Count == 0) return;

        pins.RemoveAll(p => p.OwnerDocId == parentPin.OwnerDocId && ownedIds.Contains(p.TaskId));
        int parentPinIdx = IndexOfPin(pins, parentPin.OwnerDocId, parentPin.TaskId);
        if (parentPinIdx < 0) return; // parent itself was somehow removed; don't reinsert children at 0
        pins.InsertRange(parentPinIdx + 1, gathered);
    }

    private static HashSet<Guid> OwnedRunTaskIds(ScribeDocument source, int parentBlockIndex)
    {
        var (start, end) = source.OwnedRun(parentBlockIndex);
        var ids = new HashSet<Guid>();
        for (int i = start; i < end; i++) ids.Add(source.Blocks[i].TaskId);
        return ids;
    }

    /// <summary>
    /// Manual drag-reorder: moves the pin at <paramref name="from"/> (with its cluster of following pinned
    /// depth-1 children, if depth-0) to land at <paramref name="to"/>. Clustering and drop validity come from
    /// the pins' own <see cref="ScribePinnedRef.Depth"/> via <see cref="ScribeReorderValidity"/>, so no source
    /// document is needed. Returns <c>false</c> without mutating when the target is cross-depth, equals
    /// <paramref name="from"/>, lands inside the dragged cluster, or an index is out of range.
    /// </summary>
    public static bool Reorder(List<ScribePinnedRef> pins, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(pins);
        if (from < 0 || from >= pins.Count || to < 0 || to >= pins.Count) return false;

        var depths = pins.Select(p => p.Depth).ToList();
        var (start, end) = ScribeReorderValidity.Cluster(depths, from);
        bool dropOnCluster = to >= start && to < end;
        if (from == to || dropOnCluster || !ScribeReorderValidity.IsValidDropTarget(depths, start, end, to))
            return false;

        // Dropping forward onto a pinned parent lands AFTER its whole cluster, not between it and its first child.
        int destination = ScribeReorderValidity.ResolveDestination(depths, start, to);
        int len = end - start;
        var slice = pins.GetRange(start, len);
        pins.RemoveRange(start, len);
        int insertAt = destination < start ? destination : destination - len + 1;
        insertAt = Math.Clamp(insertAt, 0, pins.Count);
        pins.InsertRange(insertAt, slice);
        return true;
    }

    private static int IndexOfPin(List<ScribePinnedRef> pins, Guid docId, Guid taskId)
    {
        for (int i = 0; i < pins.Count; i++)
        {
            if (pins[i].OwnerDocId == docId && pins[i].TaskId == taskId) return i;
        }
        return -1;
    }
}
