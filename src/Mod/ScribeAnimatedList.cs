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

/// <summary>When a departed row begins its collapse (animated-task-list D3). The collapse mechanism is
/// identical either way; only <em>when</em> it starts differs.</summary>
internal enum ScribeListRemovalPolicy
{
    /// <summary>The ghost begins collapsing the frame it departs (default — Editor/Read/Pinned and all future
    /// tabs). Their removal is an affirmative choice, so no misclick grace is needed.</summary>
    Immediate,

    /// <summary>The ghost holds full height for an undo window (optionally fading) before collapsing — the
    /// pinned-task HUD's behavior, which hides the Completion Policy so a completion may be a silent
    /// delete-with-no-undo. NOT WIRED in this change: the HUD's fade/undo-window migration is a follow-up
    /// (see tasks.md §5.4). Passing this today throws, so it can't be shipped half-built by accident.</summary>
    Delayed,
}

/// <summary>
/// A container that animates row <em>departures</em> by diffing its identity-keyed item set frame-to-frame
/// (animated-task-list). See the file header for the full rationale. Adopted first on the Pin Tab; the
/// editor/HUD migration onto it is a deferred follow-up.
/// </summary>
internal sealed class ScribeAnimatedList : StatefulWidget
{
    public ScribeAnimatedList(
        IReadOnlyList<ScribeAnimatedListItem> items,
        ScribeAnimationRegistry registry,
        Func<IReadOnlyList<Widget>, Widget> layoutBuilder,
        ScribeListRemovalPolicy policy = ScribeListRemovalPolicy.Immediate,
        Action? onDepartureSettled = null,
        int durationMs = ScribeRowSizeAnimation.DefaultDurationMs,
        Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        if (policy == ScribeListRemovalPolicy.Delayed)
        {
            // Guard, not silent fallback: the Delayed (undo-window/fade) path is not implemented in this
            // change (HUD migration is the follow-up). Fail loudly if wired prematurely rather than shipping
            // an immediate collapse mislabelled as delayed.
            throw new NotSupportedException(
                "ScribeListRemovalPolicy.Delayed is not wired yet (HUD fade/undo-window migration is a " +
                "follow-up — see extract-animated-task-list tasks.md §5.4). Use Immediate.");
        }

        Items = items;
        Registry = registry;
        LayoutBuilder = layoutBuilder;
        Policy = policy;
        OnDepartureSettled = onDepartureSettled;
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

    /// <summary>SEAM (unused today): ids that appeared this frame (present now, neither live last frame nor a
    /// ghost). Exposed by the Core diff so a future insert/reveal animation can drive off it without an API
    /// churn; no consumer wires it in this change.</summary>
    private IReadOnlyList<Guid> lastAppeared = Array.Empty<Guid>();

    private static string Key(Guid id) => id.ToString("N");

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
        foreach (var dep in diff.Departed)
        {
            if (priorSnapshots.TryGetValue(dep.Id, out var snapshot))
            {
                ghostSlots[dep.Id] = dep.Slot;
                capturedGhosts[dep.Id] = snapshot;
            }
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
                rows.Add(live);
            }
        }

        // 6. Refresh caches for next frame: snapshot every current live row, and record what we rendered.
        priorSnapshots = Widget.Items.ToDictionary(it => it.Id, it => it.Ghost ?? it.Child);
        prevRenderOrder = new List<Guid>(diff.RenderOrder);
        prevLiveIds = new HashSet<Guid>(newLiveIds);
        lastAppeared = diff.Appeared;

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
}
