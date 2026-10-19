namespace Scribe.Core;

/// <summary>
/// The pure identity diff behind the animated list container. Given the previous frame's rendered id
/// order, which of those were live (vs. collapsing ghosts), the incoming live order, and the ghosts still
/// animating, it computes which rows <see cref="ScribeListDiffResult.Departed"/> (and the slot each
/// collapses at), which ghosts <see cref="ScribeListDiffResult.Revived"/> (id reappeared before the
/// collapse ended), which rows <see cref="ScribeListDiffResult.Appeared"/> (unused seam), and the full
/// <see cref="ScribeListDiffResult.RenderOrder"/> with ghosts spliced at their slots. Lives in Core so the
/// slot math is unit-testable; the Mod-side <c>ScribeAnimatedList</c> owns no diff logic.
/// </summary>
public static class ScribeListDiff
{
    /// <param name="prevRenderOrder">The full id order rendered last frame, INCLUDING collapsing ghosts. A
    /// departed row's slot is its index here, so existing ghosts push later departures' slots down.</param>
    /// <param name="prevLiveIds">Which of <paramref name="prevRenderOrder"/> were LIVE (not ghosts); only these can newly depart.</param>
    /// <param name="newLiveIds">The incoming live id order for this frame.</param>
    /// <param name="ghostSlots">Ghosts still animating going into this frame (id → slot), BEFORE completed ones
    /// are retired. A ghost whose id is in <paramref name="newLiveIds"/> is revived.</param>
    public static ScribeListDiffResult Compute(
        IReadOnlyList<Guid> prevRenderOrder,
        IReadOnlySet<Guid> prevLiveIds,
        IReadOnlyList<Guid> newLiveIds,
        IReadOnlyDictionary<Guid, int> ghostSlots)
    {
        ArgumentNullException.ThrowIfNull(prevRenderOrder);
        ArgumentNullException.ThrowIfNull(prevLiveIds);
        ArgumentNullException.ThrowIfNull(newLiveIds);
        ArgumentNullException.ThrowIfNull(ghostSlots);

        var newLiveSet = new HashSet<Guid>(newLiveIds);

        // Revival: a ghost whose id is back in the live set cancels its departure and renders live.
        var revived = new List<Guid>();
        var keptGhosts = new Dictionary<Guid, int>(ghostSlots.Count);
        foreach (var (id, slot) in ghostSlots)
        {
            if (newLiveSet.Contains(id)) revived.Add(id);
            else keptGhosts[id] = slot;
        }

        // New departures: live last frame, absent now, not already a kept ghost. The slot is the id's index
        // in prevRenderOrder (ghosts included), so simultaneous departures get distinct in-place slots.
        var departed = new List<ScribeListDeparture>();
        for (int i = 0; i < prevRenderOrder.Count; i++)
        {
            Guid id = prevRenderOrder[i];
            if (prevLiveIds.Contains(id) && !newLiveSet.Contains(id) && !keptGhosts.ContainsKey(id))
            {
                departed.Add(new ScribeListDeparture(id, i));
            }
        }

        // Appeared (unused seam): a live id that was neither live last frame nor an animating ghost.
        var appeared = new List<Guid>();
        foreach (var id in newLiveIds)
        {
            if (!prevLiveIds.Contains(id) && !ghostSlots.ContainsKey(id)) appeared.Add(id);
        }

        // Render order: live rows, then every ghost (kept + departed) spliced back at its slot, ascending and
        // clamped to the current length. A ghost id is never in newLiveIds (revived above), so none duplicate.
        var render = new List<Guid>(newLiveIds);
        var allGhosts = new List<ScribeListDeparture>(keptGhosts.Count + departed.Count);
        foreach (var (id, slot) in keptGhosts) allGhosts.Add(new ScribeListDeparture(id, slot));
        allGhosts.AddRange(departed);
        allGhosts.Sort(static (a, b) => a.Slot.CompareTo(b.Slot));
        foreach (var g in allGhosts)
        {
            int at = Math.Clamp(g.Slot, 0, render.Count);
            render.Insert(at, g.Id);
        }

        return new ScribeListDiffResult(departed, revived, appeared, render);
    }
}

/// <summary>A row that left the live set and collapses in place at <paramref name="Slot"/> (its index in the previous rendered order).</summary>
public readonly record struct ScribeListDeparture(Guid Id, int Slot);

/// <summary>The outcome of one <see cref="ScribeListDiff.Compute"/> pass.</summary>
/// <param name="Departed">Rows that newly left the live set, each with its collapse slot.</param>
/// <param name="Revived">Ghost ids that reappeared before their collapse finished; they render live again.</param>
/// <param name="Appeared">Live ids not present at all last frame (a seam for future insert animation).</param>
/// <param name="RenderOrder">The full id order to render: live rows with every ghost spliced at its slot.</param>
public readonly record struct ScribeListDiffResult(
    IReadOnlyList<ScribeListDeparture> Departed,
    IReadOnlyList<Guid> Revived,
    IReadOnlyList<Guid> Appeared,
    IReadOnlyList<Guid> RenderOrder);
