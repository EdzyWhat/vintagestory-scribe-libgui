namespace Scribe.Core;

/// <summary>
/// The pure, game-agnostic identity diff behind the animated list container (animated-task-list). Given
/// the rendered id order of the previous frame, which of those were live (vs. already-collapsing ghosts),
/// the incoming live id order, and the set of ghosts still animating, it computes what changed:
/// which rows just <see cref="Departed"/> (and the slot each should collapse at), which ghosts
/// <see cref="Revived"/> (their id reappeared before the collapse finished), which rows freshly
/// <see cref="Appeared"/> (a seam for future insert animation, unused today), and the full
/// <see cref="ScribeListDiffResult.RenderOrder"/> — live rows in order with every surviving/departing
/// ghost spliced back at the slot it held.
///
/// <para>Kept in <c>src/Core</c> (no VS or LibGUI reference) so the slot math and the
/// reappear-cancels-departure rule are unit-testable without a game install — the Mod-side
/// <c>ScribeAnimatedList</c> widget maps this result onto animation controllers and frozen ghost
/// widgets, but owns none of the diff logic itself.</para>
/// </summary>
public static class ScribeListDiff
{
    /// <param name="prevRenderOrder">The full ordered id list the container rendered last frame, INCLUDING
    /// any collapsing ghosts at their slots. A newly-departed row collapses at its index in this list, so
    /// ghosts already present naturally push a later departure's slot down — the same display-index math the
    /// editor did by hand.</param>
    /// <param name="prevLiveIds">Which of <paramref name="prevRenderOrder"/> were LIVE rows last frame (not
    /// ghosts). Only a row that was live can newly depart.</param>
    /// <param name="newLiveIds">The incoming live id order for this frame.</param>
    /// <param name="ghostSlots">Every ghost still animating going into this frame (id → the slot it collapses
    /// at), BEFORE this diff retires completed ones. A ghost whose id is in <paramref name="newLiveIds"/> is
    /// treated as revived.</param>
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

        // Revival (spec: a departing row whose id reappears before its collapse ends is restored as a live
        // row, not left collapsing and not double-rendered). Any animating ghost whose id is back in the live
        // set cancels its departure; it renders live (it is already in newLiveIds) and its slot is dropped.
        var revived = new List<Guid>();
        var keptGhosts = new Dictionary<Guid, int>(ghostSlots.Count);
        foreach (var (id, slot) in ghostSlots)
        {
            if (newLiveSet.Contains(id)) revived.Add(id);
            else keptGhosts[id] = slot;
        }

        // New departures: ids that were LIVE last frame, are absent now, and aren't already a (kept) ghost.
        // Walk prevRenderOrder so the slot is that id's index in the previously-rendered list (ghosts
        // included) — multiple simultaneous departures each get their own distinct, in-place slot.
        var departed = new List<ScribeListDeparture>();
        for (int i = 0; i < prevRenderOrder.Count; i++)
        {
            Guid id = prevRenderOrder[i];
            if (prevLiveIds.Contains(id) && !newLiveSet.Contains(id) && !keptGhosts.ContainsKey(id))
            {
                departed.Add(new ScribeListDeparture(id, i));
            }
        }

        // Appeared (seam, unused today): a live id that was neither live last frame nor an animating ghost —
        // a genuinely new row. Disjoint from Departed by construction.
        var appeared = new List<Guid>();
        foreach (var id in newLiveIds)
        {
            if (!prevLiveIds.Contains(id) && !ghostSlots.ContainsKey(id)) appeared.Add(id);
        }

        // Render order: live rows in order, then splice every ghost (kept + newly departed) back at its slot,
        // ascending by slot and clamped to the current length — byte-for-byte the editor's ghost-splice so
        // the playtested slot behavior is preserved. A ghost id is never in newLiveIds (a reappearing id was
        // revived above), so no id is duplicated.
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

/// <summary>A row that has left the live set and should collapse in place: its stable id and the slot
/// (index in the previous rendered order) it collapses at.</summary>
public readonly record struct ScribeListDeparture(Guid Id, int Slot);

/// <summary>The outcome of one <see cref="ScribeListDiff.Compute"/> pass (animated-task-list).</summary>
/// <param name="Departed">Rows that newly left the live set this frame, each with the slot it collapses at.</param>
/// <param name="Revived">Ghost ids whose row reappeared before its collapse finished — their departure is
/// cancelled and they render live again.</param>
/// <param name="Appeared">Live ids not present at all last frame (a seam for future insert animation).</param>
/// <param name="RenderOrder">The full ordered id list to render this frame: live rows in order with every
/// surviving and departing ghost spliced at its slot.</param>
public readonly record struct ScribeListDiffResult(
    IReadOnlyList<ScribeListDeparture> Departed,
    IReadOnlyList<Guid> Revived,
    IReadOnlyList<Guid> Appeared,
    IReadOnlyList<Guid> RenderOrder);
