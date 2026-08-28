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
    /// owned-run geometry (refine-crafting-tasks-1-3-2 D6). Depth-0: append, then gather already-pinned
    /// children whose <c>TaskId</c> is in that parent's current owned run (preserving their relative
    /// pin order). Depth-1: walk back to the document parent; if that parent is pinned, insert after
    /// its contiguous HUD cluster (parent, then following pins in the owned run); otherwise append.
    /// Never auto-pins the parent. An unresolvable source (null document, or the task missing) appends.
    /// Caller has already checked uniqueness and the pin cap; this only chooses the insert index.
    /// </summary>
    public static void PlaceNewPin(List<ScribePinnedRef> pins, ScribePinnedRef newPin, ScribeDocument? source)
    {
        ArgumentNullException.ThrowIfNull(pins);
        ArgumentNullException.ThrowIfNull(newPin);

        if (source is null)
        {
            pins.Add(newPin);
            return;
        }

        int idx = source.IndexOf(newPin.TaskId);
        if (idx < 0)
        {
            pins.Add(newPin);
            return;
        }

        var block = source.Blocks[idx];
        if (block.Depth == 0)
        {
            pins.Add(newPin);
            GatherOwnedRunChildren(pins, newPin, source, idx);
            return;
        }

        int parentIdx = source.FindParentIndex(idx);
        if (parentIdx < 0)
        {
            pins.Add(newPin);
            return;
        }

        Guid parentTaskId = source.Blocks[parentIdx].TaskId;
        int parentPinIdx = IndexOfPin(pins, newPin.OwnerDocId, parentTaskId);
        if (parentPinIdx < 0)
        {
            pins.Add(newPin);
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

    private static int IndexOfPin(List<ScribePinnedRef> pins, Guid docId, Guid taskId)
    {
        for (int i = 0; i < pins.Count; i++)
        {
            if (pins[i].OwnerDocId == docId && pins[i].TaskId == taskId) return i;
        }
        return -1;
    }
}
