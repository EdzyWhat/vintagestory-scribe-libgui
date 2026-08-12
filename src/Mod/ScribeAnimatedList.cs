// A rendering-agnostic "animate row departures" container (animated-task-list). Any surface that renders
// an identity-keyed list of rows gets the editor's collapse-on-removal animation "for free" by routing its
// rows through this container and mutating ONLY its data — no per-view departing map, ghost widget, cleanup
// flag, or OnRenderGUI loop. The container diffs the id-keyed item set frame-to-frame (the pure math lives
// in Core's ScribeListDiff): an id present last frame but absent now becomes a frozen ghost, spliced back at
// the slot it left, wrapped in the shared ScribeRowSizeAnimation(Collapse); when the collapse finishes the
// container drops the ghost itself.
//
// It abstracts MOTION only, never content (design D6). It touches exactly two things about a row — its
// stable Guid identity and its height (to collapse it) — and never inspects what the row renders. Each
// surface supplies its own row widgets AND its own layout wrapper (the layoutBuilder), so an editable task
// row, a static Read line, or a multi-column Guestbook entry are all just "a widget at an id" here and stay
// free to diverge. There is deliberately NO "view behavior profile" layer.
//
// Why the container can self-clean (unlike the editor's hand-wired path, which defers a RebuildBody via a
// needsEditorCollapseCleanup flag): LibGUI's SetState/MarkNeedsBuild is DEFERRED — it adds the element to a
// dirty set drained on the next BuildDirtyElements pass, which explicitly "handles cascaded rebuilds from
// animation controllers or state changes triggered inside Build()" (BuildOwner.cs). So the collapse-end
// callback can SetState to schedule its own rebuild with no re-entrancy; the next Build retires every
// completed ghost. The editor's flag exists only because IT rebuilds the DIALOG tree (a cross-tree
// RebuildBody), whereas the container rebuilds just its own local subtree.
//
// Scroll-pin-during-collapse and the hover-refresh latch are NOT owned here (open question §2.7, resolved):
// they touch dialog-level state (the shared ScrollController, RootElement, RefreshHoverAtCursor), so they
// stay in the host's OnRenderGUI, driven off the SAME host-owned ScribeAnimationRegistry's AnyAnimating that
// this container animates against. The container packages the diff / ghost / slot / self-cleanup
// choreography; the host keeps the two inherently dialog-level loops (and an optional settle callback for a
// final scroll clamp). The registry is host-owned (passed in) so a motion survives ForceRebuild AND
// reconcile, and so the host can read AnyAnimating without reaching into this State.
//
// Mod-side only (LibGUI + the registry); Core stays API-free (the diff is ScribeListDiff there).

using System;
using System.Collections.Generic;
using System.Linq;
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, BuildContext, ValueKey, Key
using Scribe.Core;               // ScribeListDiff

namespace Scribe;

/// <summary>One row in a <see cref="ScribeAnimatedList"/>: its stable identity, the live widget rendered
/// while it is present, and an optional non-interactive <see cref="Ghost"/> snapshot shown while it collapses
/// out.</summary>
/// <param name="Id">Stable identity (the row's TaskId), how the diff tracks the row across builds.</param>
/// <param name="Child">The live row widget. The caller keys it (e.g. <c>ValueKey&lt;Guid&gt;(TaskId)</c>) as
/// usual so its State reconciles across rebuilds; the container never re-keys a live row.</param>
/// <param name="Ghost">An explicit static snapshot to render while this row collapses. REQUIRED in practice
/// for any live, interactive row (a checkbox/field/gesture row is unsafe to freeze in place — it would stay
/// clickable mid-collapse and its focus node is gone once the data leaves), so supply a frozen twin (e.g.
/// <c>ScribeFrozenEditorRow</c>). If null the container falls back to caching <see cref="Child"/> itself
/// (D2's last-built-row default) — safe only for a genuinely static row.</param>
internal readonly record struct ScribeAnimatedListItem(Guid Id, Widget Child, Widget? Ghost = null);

/// <summary>When a departed row begins its collapse (animated-task-list D3). Every surface collapses a
/// departed row the frame its identity leaves the item set — including the pinned-task HUD, whose
/// misclick-rescue undo window is a live-row deferred-send phase (the pin stays IN the set at full height,
/// its checkbox clickable, until the window elapses), not an animation hold. A held-ghost "delayed removal"
/// policy was considered for the HUD and removed as a misconception: a frozen ghost cannot host the live
/// checkbox the undo depends on (see migrate-hud-onto-animated-list). The enum is retained (single-valued)
/// so call sites and the entry/order wiring read the policy explicitly.</summary>
internal enum ScribeListRemovalPolicy
{
    /// <summary>The ghost begins collapsing the frame the row's identity departs the item set — every surface
    /// (Editor/Read/Pinned/HUD). A removal reaching the container is already an affirmative choice (the HUD's
    /// misclick-grace window lives BEFORE the pin leaves the set, so the container never sees a tentative
    /// removal).</summary>
    Immediate,
}

/// <summary>
/// A container that animates row <em>departures</em> by diffing its identity-keyed item set frame-to-frame
/// (animated-task-list). See the file header for the full rationale. Adopted across all four surfaces —
/// editor, Read view, Pin Tab, and the pinned-task HUD.
/// </summary>
internal sealed class ScribeAnimatedList : StatefulWidget
{
    public ScribeAnimatedList(
        IReadOnlyList<ScribeAnimatedListItem> items,
        ScribeAnimationRegistry registry,
        Func<IReadOnlyList<Widget>, Widget> layoutBuilder,
        ScribeListRemovalPolicy policy = ScribeListRemovalPolicy.Immediate,
        Action? onDepartureSettled = null,
        bool animateEntry = true,
        int durationMs = ScribeRowSizeAnimation.DefaultDurationMs,
        Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        Items = items;
        Registry = registry;
        LayoutBuilder = layoutBuilder;
        Policy = policy;
        OnDepartureSettled = onDepartureSettled;
        AnimateEntry = animateEntry;
        DurationMs = durationMs;
    }

    /// <summary>The current live rows, in display order, each keyed by a stable <see cref="ScribeAnimatedListItem.Id"/>.</summary>
    public IReadOnlyList<ScribeAnimatedListItem> Items { get; }

    /// <summary>Host-owned collapse controllers (keyed by row id), so a collapse resumes across the host's
    /// ForceRebuild remounts AND a reconcile SetState, and so the host can read <c>AnyAnimating</c> to drive
    /// its own scroll-pin/hover loops. The host disposes it (never this container).</summary>
    public ScribeAnimationRegistry Registry { get; }

    /// <summary>Builds the surface's layout around the final ordered widget list (live rows + collapsing
    /// ghosts spliced at their slots). This is the D6 seam: the container decides ORDER and MOTION; the
    /// surface decides layout (a <c>Scrollbar &gt; SingleChildScrollView &gt; Column</c>, a <c>ListView</c>,
    /// columns-in-a-row, …). The container never dictates it.</summary>
    public Func<IReadOnlyList<Widget>, Widget> LayoutBuilder { get; }

    public ScribeListRemovalPolicy Policy { get; }

    /// <summary>Optional: fired (deferred, safe) when a departing row's collapse completes and the list has
    /// shrunk. The host uses it for a final scroll clamp — the per-frame scroll-pin only runs while
    /// <c>Registry.AnyAnimating</c>, so the settling frame (when it flips false) needs one last clamp.</summary>
    public Action? OnDepartureSettled { get; }

    /// <summary>Whether appearances animate at all. True everywhere in normal use; a surface may pass false to
    /// suppress entry motion (e.g. a bulk repopulate that should snap). The first build after mount never
    /// animates its rows regardless (see <c>firstBuild</c>) — this only gates subsequent appearances.</summary>
    public bool AnimateEntry { get; }

    public int DurationMs { get; }

    public override State CreateState() => new ScribeAnimatedListState();
}

internal sealed class ScribeAnimatedListState : State<ScribeAnimatedList>
{
    /// <summary>The full ordered id list rendered last frame — live rows AND collapsing ghosts at their
    /// slots. A newly-departed row collapses at its index here, so ghosts already present naturally push a
    /// later departure's slot down (reproducing the editor's display-index math). Kept clean of retired
    /// ghosts so their vacated slots don't corrupt a later departure's index.</summary>
    private List<Guid> prevRenderOrder = new();

    /// <summary>Which of <see cref="prevRenderOrder"/> were LIVE last frame (not ghosts). Only a row that was
    /// live can newly depart.</summary>
    private HashSet<Guid> prevLiveIds = new();

    /// <summary>Ghosts currently collapsing: id → the slot it collapses at. An entry lives from the frame the
    /// row departs until its collapse completes (or its id reappears and revives).</summary>
    private readonly Dictionary<Guid, int> ghostSlots = new();

    /// <summary>The frozen widget captured for each collapsing ghost at the moment it departed (the last-good
    /// snapshot — the row's data is gone by the time it collapses). Held for the collapse's lifetime.</summary>
    private readonly Dictionary<Guid, Widget> capturedGhosts = new();

    /// <summary>Last frame's per-live-id ghost snapshot (<c>item.Ghost ?? item.Child</c>), so a departure
    /// detected THIS frame can freeze the row as it looked while still live.</summary>
    private Dictionary<Guid, Widget> priorSnapshots = new();

    /// <summary>Ids currently entering (animate-row-insertion): a row appeared on a non-first build and is
    /// sliding in. An entered id PERSISTS here for the row's whole live lifetime — it is NOT dropped when the
    /// slide completes (a settled <see cref="ScribeSlideIn"/> renders an inert Opacity(1) > Transform(identity)
    /// pass-through). This is load-bearing: removing the wrapper from a row on completion would type-swap the
    /// slot (wrapper > row → bare row) under the positional reconciler and remount the row's field, dropping a
    /// live caret mid-type. The entry controller is released when the row DEPARTS (step 4) or REVIVES (step 3),
    /// or when the id is no longer live at all (step 1b), never on entry completion.</summary>
    private readonly HashSet<Guid> entering = new();

    /// <summary>Whether this is the first Build since mount. On the first build <see cref="prevLiveIds"/> is
    /// empty, so the diff would report EVERY row as appeared and animate the whole list in on open / view
    /// switch / any ForceRebuild remount. Suppress entry motion on that first pass; only rows that appear on a
    /// LATER build (a genuine add into an already-mounted list) animate.</summary>
    private bool firstBuild = true;

    private static string Key(Guid id) => id.ToString("N");

    /// <summary>Registry key for a row's ENTRY controller. Distinct from the collapse key (<see cref="Key"/>)
    /// for the same id so a grow-then-delete of one row starts a FRESH collapse instead of resuming the
    /// already-Completed entry controller (which would render an instantly-closed ghost).</summary>
    private static string EntryKey(Guid id) => "enter:" + id.ToString("N");

    public override Widget Build(BuildContext context)
    {
        var newLiveIds = Widget.Items.Select(it => it.Id).ToList();
        var liveById = Widget.Items.ToDictionary(it => it.Id, it => it.Child);

        // 1. Retire any ghost whose collapse completed since the last build: drop it, release its controller,
        //    and remove it from prevRenderOrder so its vacated slot can't inflate a later departure's index.
        //    Done BEFORE the diff so a retired ghost is neither re-spliced nor re-detected as a departure.
        List<Guid>? retired = null;
        foreach (var id in ghostSlots.Keys.ToList())
        {
            if (Widget.Registry.IsComplete(Key(id)))
            {
                ghostSlots.Remove(id);
                capturedGhosts.Remove(id);
                Widget.Registry.Release(Key(id));
                (retired ??= new()).Add(id);
            }
        }
        if (retired != null) prevRenderOrder.RemoveAll(retired.Contains);

        // 1b. Drop any entry whose id is no longer live at all (row removed before its slide finished) so the
        //     set can't leak, releasing its entry controller. A COMPLETED entry on a still-live row is
        //     deliberately KEPT in `entering` (it renders an inert Opacity(1) > Transform(identity)
        //     pass-through for the row's whole life) — removing the wrapper would type-swap the slot under the
        //     positional reconciler and remount the row's field, dropping its caret. The entry controller for
        //     a live row is released when it DEPARTS (step 4) or REVIVES (step 3), not on entry completion.
        foreach (var id in entering.ToList())
        {
            if (!liveById.ContainsKey(id))
            {
                entering.Remove(id);
                Widget.Registry.Release(EntryKey(id));
            }
        }

        // 2. Diff (pure Core logic): departures, revivals, appearances, and the full spliced render order.
        var diff = ScribeListDiff.Compute(prevRenderOrder, prevLiveIds, newLiveIds, ghostSlots);

        // 3. Revivals: an id that reappeared before its collapse ended — cancel the departure, render it live
        //    again, and release its (mid-collapse) controller so a future departure of the same id starts fresh.
        foreach (var id in diff.Revived)
        {
            ghostSlots.Remove(id);
            capturedGhosts.Remove(id);
            Widget.Registry.Release(Key(id));
        }

        // 4. New departures: record each ghost's slot and freeze the widget it had while live. If no prior
        //    snapshot exists (shouldn't happen — it was live last frame), drop it rather than animate nothing.
        //    A row that departs mid-entry also has its entry state cleared here (step 1b already released the
        //    entry controller when it saw the id leave the live set, but a same-frame add-then-delete departs
        //    from a live id, so clear defensively).
        foreach (var dep in diff.Departed)
        {
            if (entering.Remove(dep.Id)) Widget.Registry.Release(EntryKey(dep.Id));
            if (priorSnapshots.TryGetValue(dep.Id, out var snapshot))
            {
                ghostSlots[dep.Id] = dep.Slot;
                capturedGhosts[dep.Id] = snapshot;
            }
        }

        // 4b. New appearances (animate-row-insertion): a row present now that was neither live last frame nor
        //     a ghost. On the FIRST build every row looks appeared (prevLiveIds empty) — suppress those so the
        //     list doesn't animate in wholesale on open/view-switch. Otherwise register each appeared id so it
        //     slides in (one uniform entry motion for every appearance — the focus-safe translate holds the
        //     row at full height in its slot, so even the auto-focused new row keeps its caret/clicks exact).
        //     Registration is idempotent — a revived id is not "appeared" (the diff excludes it), and an id
        //     already entering is a no-op in the set.
        if (!firstBuild && Widget.AnimateEntry)
        {
            foreach (var id in diff.Appeared) entering.Add(id);
        }

        // 5. Materialize the render order: a ghost id → its frozen snapshot wrapped in a Collapse animation
        //    (keyed by id so its controller is found across rebuilds); a live id → the caller's row widget.
        var rows = new List<Widget>(diff.RenderOrder.Count);
        foreach (var id in diff.RenderOrder)
        {
            if (ghostSlots.ContainsKey(id) && capturedGhosts.TryGetValue(id, out var ghost))
            {
                Guid captured = id;
                rows.Add(new ScribeRowSizeAnimation(
                    id: Key(captured),
                    animating: true,
                    direction: ScribeRowSizeDirection.Collapse,
                    registry: Widget.Registry,
                    onEnd: () => OnGhostCollapsed(captured),
                    durationMs: Widget.DurationMs,
                    child: ghost,
                    key: new ValueKey<Guid>(captured)));
            }
            else if (liveById.TryGetValue(id, out var live))
            {
                // A live row that is entering is wrapped in its slide-in (keyed by id so its controller is
                // found across rebuilds); the wrapper stays for the row's whole live lifetime (a settled slide
                // is an inert pass-through), so a completed entry never type-swaps the slot back to a bare row
                // and remounts the field. A row that never entered (present since the first build) renders bare.
                if (entering.Contains(id))
                {
                    Guid captured = id;
                    rows.Add(new ScribeSlideIn(
                        id: EntryKey(captured),
                        animating: true,
                        registry: Widget.Registry,
                        onEnd: () => OnEntryComplete(captured),
                        durationMs: Widget.DurationMs,
                        child: live,
                        key: new ValueKey<Guid>(captured)));
                }
                else
                {
                    rows.Add(live);
                }
            }
        }

        // 6. Refresh caches for next frame: snapshot every current live row, and record what we rendered.
        priorSnapshots = Widget.Items.ToDictionary(it => it.Id, it => it.Ghost ?? it.Child);
        prevRenderOrder = new List<Guid>(diff.RenderOrder);
        prevLiveIds = new HashSet<Guid>(newLiveIds);
        firstBuild = false;

        return Widget.LayoutBuilder(rows);
    }

    /// <summary>A departing row's collapse finished. Schedule a rebuild (deferred SetState — the next Build
    /// retires the completed ghost and releases its controller) and notify the host so it can re-clamp scroll.
    /// Guarded against firing after this container unmounted (view switch / dialog close mid-collapse).</summary>
    private void OnGhostCollapsed(Guid id)
    {
        if (Element.Owner == null) return; // unmounted mid-collapse; nothing to rebuild
        SetState(() => { /* Build() retires every ghost whose controller IsComplete */ });
        Widget.OnDepartureSettled?.Invoke();
    }

    /// <summary>An entering row's slide-in finished. Schedule one settling repaint (deferred SetState) so the
    /// final frame renders at rest (opacity 1, offset 0). The entry wrapper is deliberately KEPT for the row's
    /// whole live lifetime (a settled ScribeSlideIn is an inert Opacity(1) > Transform(identity) pass-through),
    /// so this never retires it — removing it would type-swap the slot and remount the row's field. Guarded
    /// against firing after unmount (view switch / dialog close mid-entry).</summary>
    private void OnEntryComplete(Guid id)
    {
        if (Element.Owner == null) return; // unmounted mid-entry; nothing to rebuild
        SetState(() => { /* Build() renders the settled slide as an inert pass-through; the wrapper stays */ });
    }
}
