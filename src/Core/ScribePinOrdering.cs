namespace Scribe.Core;

/// <summary>
/// The HUD's display order for a player's pins: pin order, with completed tasks sunk below the
/// not-completed ones. Pure and game-agnostic so it can be unit-tested and shared by any surface
/// that renders the pin set. The Mod layer decides <em>when</em> a just-completed row sinks (a
/// brief undo window lives in the HUD); this only defines the resting order.
/// </summary>
public static class ScribePinOrdering
{
    /// <summary>
    /// Returns <paramref name="pins"/> reordered for display: all not-done pins first, then all
    /// done pins, each group keeping the input's relative order. The input is not mutated.
    /// </summary>
    /// <remarks>
    /// The sort is stable (a partition, really), so within each group the caller's pin order is
    /// preserved — matching "pin order, completed at the bottom." Done-ness is read from
    /// <see cref="ScribePinnedRef.LastKnownDone"/>, the same snapshot the HUD renders, so the order
    /// stays consistent with what the player sees even when a task's source is unloaded.
    /// </remarks>
    public static IReadOnlyList<ScribePinnedRef> ForDisplay(IReadOnlyList<ScribePinnedRef> pins)
    {
        ArgumentNullException.ThrowIfNull(pins);

        var ordered = new List<ScribePinnedRef>(pins.Count);
        // Not-done first, preserving order.
        foreach (var pin in pins)
        {
            if (!pin.LastKnownDone) ordered.Add(pin);
        }
        // Then done, preserving order.
        foreach (var pin in pins)
        {
            if (pin.LastKnownDone) ordered.Add(pin);
        }
        return ordered;
    }

    /// <summary>
    /// Inserts <paramref name="newPin"/> into <paramref name="pins"/> using the source document's
    /// owned-run geometry (refine-crafting-tasks-1-3-2 D6) and the player's <paramref name="insertEdge"/>
    /// (update-pins-1-3-3). Depth-0: insert at <paramref name="insertEdge"/>, then gather already-pinned
    /// children whose <c>TaskId</c> is in that parent's current owned run (preserving their relative
    /// pin order). Depth-1: walk back to the document parent; if that parent is pinned, insert after
    /// its contiguous HUD cluster (parent, then following pins in the owned run) — this branch ignores
    /// <paramref name="insertEdge"/>, since a subtask always attaches to its pinned parent rather than the
    /// list edge; otherwise insert at <paramref name="insertEdge"/>. Never auto-pins the parent. An
    /// unresolvable source (null document, or the task missing) also inserts at <paramref name="insertEdge"/>.
    /// Caller has already checked uniqueness and the pin cap; this only chooses the insert index.
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

    /// <summary>Insert a pin with no pinned-parent relationship at the player's chosen edge: index 0 for
    /// <see cref="ScribePinInsert.Top"/>, appended for <see cref="ScribePinInsert.Bottom"/>.</summary>
    private static void InsertAtEdge(List<ScribePinnedRef> pins, ScribePinnedRef newPin, ScribePinInsert insertEdge)
    {
        if (insertEdge == ScribePinInsert.Top) pins.Insert(0, newPin);
        else pins.Add(newPin);
    }

    /// <summary>Pull already-pinned owned-run children to sit immediately after the newly appended
    /// parent pin, preserving their relative order. Pins from other documents, and pins whose tasks
    /// are not in this owned run, stay where they are.</summary>
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
    /// Manual drag-reorder of an existing pin (same-depth-reorder): moves the pin at
    /// <paramref name="from"/> — together with its owned-run cluster, if it is a depth-0 pin with
    /// already-pinned depth-1 children immediately following it in <paramref name="pins"/> — to land
    /// at <paramref name="to"/>. Clustering and same-depth drop-target validity are both computed from
    /// the pin list's own <see cref="ScribePinnedRef.Depth"/> values via <see cref="ScribeReorderValidity"/>
    /// (positionally, over the current pin list — no source document resolution needed, so this works
    /// even when a pin's source document is unloaded). Returns <c>false</c> without mutating
    /// <paramref name="pins"/> when the target is invalid (cross-depth), <paramref name="from"/> equals
    /// <paramref name="to"/>, the drop lands inside the dragged pin's own cluster, or either index is
    /// out of range.
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

        // Resolve the actual destination so dropping forward onto a pinned parent with its own pinned
        // children lands AFTER that parent's whole cluster rather than wedging between it and its
        // first child (the upward direction already lands correctly with no adjustment).
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
