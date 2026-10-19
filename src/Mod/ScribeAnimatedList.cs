// A rendering-agnostic "animate row departures" container. A surface that renders an identity-keyed list
// routes its rows through this container and mutates only its data. The container diffs the id-keyed item
// set frame-to-frame (pure math in Core's ScribeListDiff): an id present last frame but absent now becomes
// a frozen ghost, spliced back at the slot it left and wrapped in ScribeRowSizeAnimation(Collapse); when
// the collapse finishes the container drops the ghost itself.
//
// It abstracts motion only, never content: it touches a row's stable Guid identity and its height, and each
// surface supplies its own row widgets and its own layout wrapper (the layoutBuilder).
//
// Self-cleaning is safe because LibGUI's SetState/MarkNeedsBuild is deferred (drained on the next
// BuildDirtyElements pass, which handles cascaded rebuilds), so the collapse-end callback can SetState with
// no re-entrancy and the next Build retires every completed ghost.
//
// Scroll-pin-during-collapse and the hover-refresh latch are NOT owned here: they touch dialog-level state
// (shared ScrollController, RootElement, RefreshHoverAtCursor), so they stay in the host's OnRenderGUI,
// driven off the host-owned ScribeAnimationRegistry's AnyAnimating. The registry is host-owned so a motion
// survives ForceRebuild AND reconcile.
//
// Mod-side only (LibGUI + the registry); Core stays API-free.

using System;
using System.Collections.Generic;
using System.Linq;
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, BuildContext, ValueKey, Key
using Scribe.Core;               // ScribeListDiff

namespace Scribe;

/// <summary>One row in a <see cref="ScribeAnimatedList"/>: its stable identity, the live widget, and an
/// optional non-interactive <see cref="Ghost"/> snapshot shown while it collapses out.</summary>
/// <param name="Id">Stable identity (the row's TaskId); how the diff tracks the row across builds.</param>
/// <param name="Child">The live row widget. The caller keys it so its State reconciles; the container never re-keys it.</param>
/// <param name="Ghost">A static snapshot to render while the row collapses. Required in practice for any
/// interactive row (it would stay clickable mid-collapse and its focus node is gone), so supply a frozen
/// twin. If null the container caches <see cref="Child"/> itself, safe only for a genuinely static row.</param>
internal readonly record struct ScribeAnimatedListItem(Guid Id, Widget Child, Widget? Ghost = null);

/// <summary>When a departed row begins its collapse. Single-valued; every surface (including the HUD, whose
/// undo window is a live-row deferred-send phase before the pin leaves the set) collapses immediately.</summary>
internal enum ScribeListRemovalPolicy
{
    /// <summary>The ghost begins collapsing the frame the row's identity departs the item set.</summary>
    Immediate,
}

/// <summary>
/// A container that animates row <em>departures</em> by diffing its identity-keyed item set frame-to-frame.
/// See the file header.
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

    /// <summary>The current live rows, in display order.</summary>
    public IReadOnlyList<ScribeAnimatedListItem> Items { get; }

    /// <summary>Host-owned collapse controllers keyed by row id, so a collapse resumes across ForceRebuild
    /// remounts and the host can read <c>AnyAnimating</c>. The host disposes it.</summary>
    public ScribeAnimationRegistry Registry { get; }

    /// <summary>Builds the surface's layout around the final ordered widget list (live rows plus ghosts at
    /// their slots). The container decides order and motion; the surface decides layout.</summary>
    public Func<IReadOnlyList<Widget>, Widget> LayoutBuilder { get; }

    public ScribeListRemovalPolicy Policy { get; }

    /// <summary>Optional: fired (deferred, safe) when a departing row's collapse completes. The host uses it
    /// for a final scroll clamp, since the per-frame scroll-pin only runs while <c>Registry.AnyAnimating</c>.</summary>
    public Action? OnDepartureSettled { get; }

    /// <summary>Whether appearances animate. The first build after mount never animates regardless.</summary>
    public bool AnimateEntry { get; }

    public int DurationMs { get; }

    public override State CreateState() => new ScribeAnimatedListState();
}

internal sealed class ScribeAnimatedListState : State<ScribeAnimatedList>
{
    /// <summary>The full ordered id list rendered last frame (live rows and ghosts at their slots), so ghosts
    /// already present push a later departure's slot down. Kept clean of retired ghosts.</summary>
    private List<Guid> prevRenderOrder = new();

    /// <summary>Which of <see cref="prevRenderOrder"/> were live last frame; only a live row can newly depart.</summary>
    private HashSet<Guid> prevLiveIds = new();

    /// <summary>Collapsing ghosts: id to the slot it collapses at, until its collapse completes or it revives.</summary>
    private readonly Dictionary<Guid, int> ghostSlots = new();

    /// <summary>The frozen widget captured for each ghost when it departed (its data is gone by then).</summary>
    private readonly Dictionary<Guid, Widget> capturedGhosts = new();

    /// <summary>Last frame's per-live-id snapshot (<c>item.Ghost ?? item.Child</c>), used to freeze a row detected as departed this frame.</summary>
    private Dictionary<Guid, Widget> priorSnapshots = new();

    /// <summary>Ids currently entering (appeared on a non-first build). An id PERSISTS here for the row's whole
    /// live lifetime; a settled <see cref="ScribeSlideIn"/> is an inert pass-through. Removing the wrapper on
    /// completion would type-swap the slot under the positional reconciler and remount the row's field,
    /// dropping a live caret. The entry controller is released on departure (step 4), revival (step 3), or
    /// when the id is no longer live (step 1b).</summary>
    private readonly HashSet<Guid> entering = new();

    /// <summary>Whether this is the first Build since mount. The diff would report every row as appeared, so
    /// entry motion is suppressed on that pass.</summary>
    private bool firstBuild = true;

    /// <summary>Each row's last-measured rendered height, reported by the <see cref="ScribeSizeReportWidget"/>
    /// wrapping every row. Survives ordinary row mutations (they reconcile this State); reset on ForceRebuild.</summary>
    private readonly Dictionary<Guid, float> knownHeight = new();

    /// <summary>A repositioned row's current generation number, bumped when a fresh displacement starts so
    /// <see cref="ScribeRowReposition"/> can tell "resume" from "start new" without unmounting the wrapper
    /// (which would drop a live caret). Removed only on departure; the wrapper renders forever, inert at rest.</summary>
    private readonly Dictionary<Guid, int> repositionGeneration = new();

    private static string Key(Guid id) => id.ToString("N");

    /// <summary>Registry key for a row's entry controller, distinct from the collapse key so a grow-then-delete
    /// starts a fresh collapse instead of resuming the Completed entry controller.</summary>
    private static string EntryKey(Guid id) => "enter:" + id.ToString("N");

    /// <summary>Registry key for a row's reposition controller. The generation is embedded so a fresh
    /// displacement gets a new controller while an unchanged one resumes.</summary>
    private static string MoveKey(Guid id, int generation) => "move:" + id.ToString("N") + ":" + generation;

    /// <summary>Below this many logical px a survivor's displacement is treated as noise.</summary>
    private const float RepositionEpsilon = 1f;

    /// <summary>Cumulative Y position before each id in <paramref name="order"/>, from last-known heights. A
    /// never-measured row falls back to the smallest known height rather than zero (zero would make a single
    /// insertion's survivor delta round to nothing and never animate).</summary>
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

        // 1. Retire ghosts whose collapse completed (before the diff, so none is re-spliced or re-detected).
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

        // 1b. Drop entries whose id is no longer live (row removed before its slide finished), releasing the
        //     controller. A completed entry on a still-live row is deliberately KEPT (see `entering`).
        foreach (var id in entering.ToList())
        {
            if (!liveById.ContainsKey(id))
            {
                entering.Remove(id);
                Widget.Registry.Release(EntryKey(id));
            }
        }

        // 2. Diff (pure Core logic).
        var diff = ScribeListDiff.Compute(prevRenderOrder, prevLiveIds, newLiveIds, ghostSlots);

        // 3. Revivals: cancel the departure and release the mid-collapse controller so a later departure starts fresh.
        foreach (var id in diff.Revived)
        {
            ghostSlots.Remove(id);
            capturedGhosts.Remove(id);
            Widget.Registry.Release(Key(id));
        }

        // 4. New departures: record the ghost's slot and freeze its live widget. Also clears entry state
        //    defensively (a same-frame add-then-delete departs from a live id).
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

        // 4b. New appearances: neither live last frame nor a ghost. Suppressed on the first build, where every
        //     row looks appeared. Registration is idempotent; a revived id is not "appeared".
        if (!firstBuild && Widget.AnimateEntry)
        {
            foreach (var id in diff.Appeared) entering.Add(id);
        }

        // 4c. Reposition: a survivor (live in both previous and current order) whose cumulative Y changes gets
        //     a fresh displacement animation, whatever the cause. Skipped on the first build. The value is only
        //     used by ScribeRowReposition when a fresh generation attaches, so a later build recomputing ~0
        //     does not cancel an in-flight motion.
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

                // A fresh displacement needs a new generation when the row was never repositioned or its last
                // reposition finished (resuming a Completed controller would render no motion).
                bool needsFreshGeneration = !repositionGeneration.TryGetValue(id, out var currentGen)
                    || Widget.Registry.IsComplete(MoveKey(id, currentGen));
                if (needsFreshGeneration) repositionGeneration[id] = currentGen + 1;

                survivorTargetOffset[id] = delta;
            }
        }

        // 5. Materialize the render order: ghost id to its snapshot in a Collapse animation; live id to the
        //    caller's row, optionally wrapped in entry slide-in and/or reposition. Every row is also wrapped in
        //    a size reporter so knownHeight stays current.
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
                // The wrappers are independent and stack: `entering` never clears for a row's live lifetime,
                // so an either/or would leave an entered row unable to reposition again. A row is excluded from
                // reposition only on the build it first appears (survivorTargetOffset omits it).
                Widget content = live;

                if (repositionGeneration.TryGetValue(id, out var gen))
                {
                    // Wrapped forever once ever repositioned (same focus-safety reasoning as entering);
                    // with no active displacement it settles at offset zero.
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
                continue; // unreachable: every RenderOrder id is a kept ghost or a live row
            }

            Guid measuredId = id;
            rows.Add(new ScribeSizeReportWidget(
                onMeasured: size => knownHeight[measuredId] = size.Y,
                child: row,
                key: new ValueKey<Guid>(measuredId)));
        }

        // 6. Refresh caches for the next frame.
        priorSnapshots = Widget.Items.ToDictionary(it => it.Id, it => it.Ghost ?? it.Child);
        prevRenderOrder = new List<Guid>(diff.RenderOrder);
        prevLiveIds = new HashSet<Guid>(newLiveIds);
        firstBuild = false;

        return Widget.LayoutBuilder(rows);
    }

    /// <summary>A departing row's collapse finished: schedule a rebuild (the next Build retires the ghost) and
    /// notify the host. Guarded against firing after unmount.</summary>
    private void OnGhostCollapsed(Guid id)
    {
        if (Element.Owner == null) return; // unmounted mid-collapse; nothing to rebuild
        SetState(() => { /* Build() retires every ghost whose controller IsComplete */ });
        Widget.OnDepartureSettled?.Invoke();
    }

    /// <summary>An entering row's slide-in finished: schedule one settling repaint. The entry wrapper is kept
    /// for the row's lifetime, so this never retires it. Guarded against firing after unmount.</summary>
    private void OnEntryComplete(Guid id)
    {
        if (Element.Owner == null) return; // unmounted mid-entry; nothing to rebuild
        SetState(() => { /* Build() renders the settled slide as an inert pass-through; the wrapper stays */ });
    }
}
