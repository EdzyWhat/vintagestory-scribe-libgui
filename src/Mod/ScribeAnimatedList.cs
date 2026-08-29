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

    /// <summary>Each row's last-measured real rendered height (animate-row-reposition), reported by the
    /// <see cref="ScribeSizeReportWidget"/> every row is wrapped in. Reset only when this State itself is
    /// torn down (a genuine <c>ForceRebuild</c>) — an ordinary row mutation goes through the surface's local
    /// <c>RebuildBody</c>, which reconciles this State rather than recreating it, so the cache survives
    /// across exactly the builds where a reposition needs to compare an old height to a new one.</summary>
    private readonly Dictionary<Guid, float> knownHeight = new();

    /// <summary>For a row that has ever been repositioned, its CURRENT generation number
    /// (animate-row-reposition) — bumped each time a fresh, non-negligible displacement starts, so
    /// <see cref="ScribeRowReposition"/> can tell "resume this animation" (unchanged generation) apart from
    /// "start a new one" (bumped) without unmounting the row's wrapper (which would drop a live caret).
    /// Once added, an id is never removed except on departure — the wrapper renders forever (inert at rest)
    /// for the same focus-safety reason <see cref="entering"/> does.</summary>
    private readonly Dictionary<Guid, int> repositionGeneration = new();

    private static string Key(Guid id) => id.ToString("N");

    /// <summary>Registry key for a row's ENTRY controller. Distinct from the collapse key (<see cref="Key"/>)
    /// for the same id so a grow-then-delete of one row starts a FRESH collapse instead of resuming the
    /// already-Completed entry controller (which would render an instantly-closed ghost).</summary>
    private static string EntryKey(Guid id) => "enter:" + id.ToString("N");

    /// <summary>Registry key for a row's reposition controller at a given generation
    /// (animate-row-reposition). The generation is embedded in the key itself (not looked up separately) so
    /// a fresh displacement gets a genuinely new <see cref="AnimationController"/> from the registry, while
    /// an unchanged generation resumes the existing one across a reconciling rebuild.</summary>
    private static string MoveKey(Guid id, int generation) => "move:" + id.ToString("N") + ":" + generation;

    /// <summary>Below this many logical px, a survivor's computed displacement is treated as noise (float
    /// accumulation, a sub-pixel wrap difference) rather than a real reposition worth animating.</summary>
    private const float RepositionEpsilon = 1f;

    /// <summary>Cumulative Y position immediately BEFORE each id in <paramref name="order"/>, using each id's
    /// last-known real height (animate-row-reposition). A missing height (a row never yet measured — only
    /// possible for one that just appeared this very build, e.g. a brand-new task) falls back to the
    /// SMALLEST currently-known row height rather than zero: defaulting to zero was the exact bug found in
    /// playtest — a single fresh insertion's survivor delta rounded to nothing (nothing separated old and
    /// new position except the unmeasured row), so it never animated at all. A brand-new task starts as
    /// empty text, so the shortest known row is a reasonable stand-in for its real height until the next
    /// build measures it for real (the row's OWN entry animation is unaffected either way — this fallback
    /// only feeds OTHER survivors' displacement math).</summary>
    private Dictionary<Guid, float> PrefixY(IReadOnlyList<Guid> order)
    {
        float fallback = knownHeight.Count > 0 ? knownHeight.Values.Min() : 0f;
        var result = new Dictionary<Guid, float>(order.Count);
        float y = 0f;
        foreach (var id in order)
        {
            result[id] = y;
            y += knownHeight.TryGetValue(id, out var h) ? h : fallback;
        }
        return result;
    }

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
            if (repositionGeneration.Remove(dep.Id, out var gen)) Widget.Registry.Release(MoveKey(dep.Id, gen));
            knownHeight.Remove(dep.Id);
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

        // 4c. Reposition (animate-row-reposition): a survivor — an id live in BOTH the previous and current
        //     render order (so neither departing, reviving, nor freshly appeared) — whose cumulative Y
        //     position changes gets a fresh displacement animation. Computed purely from the before/after
        //     order and each row's real measured height, so it fires uniformly regardless of WHY the slot
        //     changed (an insertion above it, a removal elsewhere, or an explicit reorder). Skipped on the
        //     first build: there is no meaningful "previous position" right after a ForceRebuild reset.
        //     This computed value is only USED by ScribeRowReposition at the exact moment a fresh
        //     generation attaches (see its class doc comment) — a later build recomputing ~0 here (because
        //     the order has already caught up to itself) does NOT retroactively cancel an in-flight motion.
        var survivorTargetOffset = new Dictionary<Guid, float>();
        if (!firstBuild)
        {
            var oldPrefixY = PrefixY(prevRenderOrder);
            var newPrefixY = PrefixY(diff.RenderOrder);
            foreach (var id in newLiveIds)
            {
                if (!prevLiveIds.Contains(id)) continue; // appeared/revived — not a survivor
                if (!oldPrefixY.TryGetValue(id, out var oldY) || !newPrefixY.TryGetValue(id, out var newY)) continue;

                float delta = oldY - newY;
                if (Math.Abs(delta) < RepositionEpsilon) continue;

                // A fresh displacement needs a NEW generation (and so a fresh controller) whenever this row
                // has never been repositioned before, or its last reposition already finished — resuming a
                // Completed controller would render zero motion for what should be a brand-new slide.
                bool needsFreshGeneration = !repositionGeneration.TryGetValue(id, out var currentGen)
                    || Widget.Registry.IsComplete(MoveKey(id, currentGen));
                if (needsFreshGeneration) repositionGeneration[id] = currentGen + 1;

                survivorTargetOffset[id] = delta;
            }
        }

        // 5. Materialize the render order: a ghost id → its frozen snapshot wrapped in a Collapse animation
        //    (keyed by id so its controller is found across rebuilds); a live id → the caller's row widget,
        //    optionally wrapped in its entry slide-in or its reposition displacement. Every rendered row is
        //    additionally wrapped in a size reporter so knownHeight stays current for the next build's
        //    reposition math (animate-row-reposition), regardless of which (if any) animation wraps it.
        var rows = new List<Widget>(diff.RenderOrder.Count);
        foreach (var id in diff.RenderOrder)
        {
            Widget row;
            if (ghostSlots.ContainsKey(id) && capturedGhosts.TryGetValue(id, out var ghost))
            {
                Guid captured = id;
                row = new ScribeRowSizeAnimation(
                    id: Key(captured),
                    animating: true,
                    direction: ScribeRowSizeDirection.Collapse,
                    registry: Widget.Registry,
                    onEnd: () => OnGhostCollapsed(captured),
                    durationMs: Widget.DurationMs,
                    child: ghost,
                    key: new ValueKey<Guid>(captured));
            }
            else if (liveById.TryGetValue(id, out var live))
            {
                // Both wrappers are INDEPENDENT and stack rather than being mutually exclusive: `entering`
                // NEVER clears for a row's whole live lifetime (a settled ScribeSlideIn is an inert
                // pass-through, kept forever for focus safety — see its own doc comment), so a row that
                // entered once and later needs to reposition (e.g. a brand-new task, still "entering"
                // forever, later shifted by ANOTHER new task landing above it) would otherwise be
                // permanently stuck on the entry branch and never reposition-animate again for the rest of
                // its life in this mounted session (the exact bug this fixes — confirmed by testing: it
                // only "fixed itself" after a ForceRebuild, i.e. reopening the dialog or switching tabs,
                // reset `entering` to empty). A row is excluded from reposition only on the EXACT build it
                // first appears (no prior position exists yet — survivorTargetOffset excludes it by
                // construction); from the next build on it is eligible even while still mid-slide-in.
                Widget content = live;

                if (repositionGeneration.TryGetValue(id, out var gen))
                {
                    // Wrapped forever once ever repositioned (same focus-safety reasoning as entering below);
                    // a build with no active displacement for this id still renders the wrapper, settled at
                    // offset zero via its own controller's Completed state.
                    Guid capturedMove = id;
                    float offset = survivorTargetOffset.TryGetValue(id, out var d) ? d : 0f;
                    content = new ScribeRowReposition(
                        id: MoveKey(capturedMove, gen),
                        targetOffsetY: offset,
                        registry: Widget.Registry,
                        child: content,
                        key: new ValueKey<Guid>(capturedMove));
                }

                if (entering.Contains(id))
                {
                    Guid capturedEnter = id;
                    content = new ScribeSlideIn(
                        id: EntryKey(capturedEnter),
                        animating: true,
                        registry: Widget.Registry,
                        onEnd: () => OnEntryComplete(capturedEnter),
                        durationMs: Widget.DurationMs,
                        child: content,
                        key: new ValueKey<Guid>(capturedEnter));
                }

                row = content;
            }
            else
            {
                continue; // shouldn't happen — every RenderOrder id is either a kept ghost or a live row
            }

            Guid measuredId = id;
            rows.Add(new ScribeSizeReportWidget(
                onMeasured: size => knownHeight[measuredId] = size.Y,
                child: row,
                key: new ValueKey<Guid>(measuredId)));
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
