// A reusable "animate a list row's height" primitive (gui-row-animation-harness) — the ONE harness
// every Scribe animating row shares, in either direction:
//   • Collapse (exit): a departing row shrinks its height 1→0 so the rows below slide up to meet it,
//     then it is removed once the collapse finishes (a HUD unpin/delete completion, a lectern editor
//     delete).
//   • Reveal (enter): a freshly-mounted row grows its height 0→1 so it slides into place instead of
//     popping in. No removal — the row just settles at full height (the "ScribeRevealable" sketch in
//     docs/animation-lessons-learned.md).
//
// The load-bearing constraint (verified against LibGUI source): the HUD and the lectern both push
// content via GuiBase.ForceRebuild, which UNMOUNTS and recreates the whole widget tree rather than
// reconciling it. Any stock/implicit animation widget (AnimatedSize/AnimatedContainer/…) recreated
// fresh inits Begin==End==target and SNAPS to the end — no motion. So, exactly like ScribeFadeText,
// this is a self-ticking StatefulWidget that drives its own AnimationController. And because EACH
// ForceRebuild remounts the widget, a State-owned controller would restart from zero every rebuild
// and stutter/never finish — so the controller is owned by a host-held ScribeAnimationRegistry,
// keyed by the row's stable identity, and RESUMED on remount (mirrors the ScribeNumericFocusRegistry
// persistent-node pattern used for the settings fields). The identity-keyed registry is exactly what
// lets a motion resume across BOTH a ForceRebuild AND a reconcile SetState (design D5): the same
// controller is found by id no matter how the row's element was rebuilt.
//
// Harness shape (design Open Question, resolved 2026-08-09): ONE widget with a direction enum over
// ONE shared registry, NOT a family. The substrate (ScribeHeightFactorRender/Widget, the registry)
// is direction-neutral; only the Build factor mapping and whether the terminal callback removes the
// row differ between Collapse and Reveal, and both live in the single ScribeRowSizeAnimationState so
// the load-bearing survival logic (registry lookup, resume-on-remount, fire-if-already-completed,
// detach-but-don't-dispose) can't drift between the two motions.
//
// This file is Mod-side only (LibGUI + AnimationController); Core stays API-free and untouched.

using System;
using System.Collections.Generic;
using Gui.Core.Framework;         // RenderObject, RenderProxyBox
using Gui.Rendering;              // PaintingContext
using Gui.Widgets.Animations;     // AnimationController, AnimationStatus, Curve, Curves
using Gui.Widgets.Framework;      // Widget, StatefulWidget, State, BuildContext, SingleChildWidget, Key
using Gui.Widgets.Painting;       // Opacity, Transform
using OpenTK.Mathematics;         // Vector2
using SkiaSharp;                  // SKRect

namespace Scribe;

/// <summary>
/// Pure coordinate math for the collapse-time hover refresh (fix-list-collapse-stale-hover), factored
/// out of the two hosts so the load-bearing conversion has one definition (and could be unit-tested
/// without a game if it ever needs to be — it touches no VS API).
/// </summary>
internal static class ScribeHoverRefresh
{
    /// <summary>Convert raw-pixel mouse coordinates to window-local coordinates, exactly as LibGUI's
    /// private <c>GuiBase.ToWindowLocal(ToLogicalScreen(rawX, rawY))</c> does: divide by the UI scale to
    /// get logical-screen coords, then subtract the window's top-left position. This is the same
    /// conversion the title-bar grip drag already relies on (<c>ScribeDialogBase.Layout.cs</c>).</summary>
    public static Vector2 ToWindowLocal(int rawMouseX, int rawMouseY, float uiScale, Vector2 windowPos)
        => new Vector2(rawMouseX / uiScale, rawMouseY / uiScale) - windowPos;
}

/// <summary>
/// A tiny frame countdown that keeps a synthetic hover refresh running for a few frames PAST whatever
/// triggered it (fix-list-collapse-stale-hover). Its whole reason to exist is the <c>ForceRebuild</c>
/// after-effect: <c>GuiBase.ForceRebuild</c> unmounts the tree and mounts a brand-new one where every
/// element is <c>hovered = false</c>, and that rebuilt tree isn't laid out until a LATER frame — so a
/// single same-frame refresh hit-tests nothing (the new tree has no geometry yet) and the row under a
/// stationary cursor loses its hover-gated delete/pin controls until the user wiggles the mouse. Lingering
/// a few frames guarantees at least one refresh lands after the new tree has laid out.
///
/// Two things arm it: (1) an in-flight collapse (re-armed every animating frame + on the collapse-cleanup
/// rebuild), and (2) via <see cref="ArmIfRebuilt"/>, ANY tree rebuild at all — unpin, new-row insert,
/// title-edit toggle, corruption rebuild — because every one of those goes through <c>ForceRebuild</c> and
/// hits the exact same stale-hover-after-rebuild problem, animated or not. Detecting the rebuild centrally
/// (by <c>RootElement</c> identity) means no per-call-site wiring: every current and future
/// <c>ForceRebuild</c> path is covered automatically.
/// </summary>
internal sealed class ScribeHoverRefreshLatch
{
    /// <summary>Frames to keep refreshing after the last trigger. 3 comfortably spans the
    /// trigger → <c>ForceRebuild</c> → layout → paint gap on both hosts' <c>OnRenderGUI</c> orderings
    /// (the dialog rebuilds after its <c>base</c> layout, the HUD before — 3 covers either).</summary>
    private const int LingerFrames = 3;

    private int remaining;

    /// <summary>Identity of the <c>RootElement</c> last seen by <see cref="ArmIfRebuilt"/>, so a change
    /// (which only <c>ForceRebuild</c> causes post-mount) is detected as a rebuild.</summary>
    private object? lastRoot;

    /// <summary>Re-arm to the full linger window. Call each frame a collapse is animating and on the
    /// frame a collapse-cleanup rebuild fires.</summary>
    public void Arm() => remaining = LingerFrames;

    /// <summary>Arm the latch if the tree was rebuilt since the last call — i.e. if <paramref name="currentRoot"/>
    /// is a different instance than last seen (<c>GuiBase.ForceRebuild</c> assigns a fresh <c>RootElement</c>,
    /// the only post-mount change). Catches EVERY rebuild path (unpin, new-row, title edit, …) with no
    /// per-call-site code. Call once per frame from <c>OnRenderGUI</c>, passing <c>RootElement</c>. The first
    /// call (from null) counts as a rebuild, which is a harmless one-time refresh on open.</summary>
    public void ArmIfRebuilt(object? currentRoot)
    {
        if (!ReferenceEquals(currentRoot, lastRoot))
        {
            lastRoot = currentRoot;
            Arm();
        }
    }

    /// <summary>Consume one frame; returns true if a hover refresh should be dispatched this frame.</summary>
    public bool Tick()
    {
        if (remaining <= 0) return false;
        remaining--;
        return true;
    }
}

/// <summary>
/// The render object behind <see cref="ScribeHeightFactorWidget"/>: lays its child out at full
/// constraints but reports its OWN height as <c>childHeight * factor</c> and clips paint to that box.
/// Because the reported <see cref="RenderObject.Size"/> shrinks as <see cref="Factor"/> goes 1→0 (and
/// each change relayouts), a parent <c>Column</c> reflows and slides the rows below up each frame —
/// the same layout-shrink behavior LibGUI's own <c>RenderAnimatedSize</c> uses, but driven externally
/// so it animates under the HUD/lectern's <c>ForceRebuild</c>-only rebuild path.
/// </summary>
internal sealed class ScribeHeightFactorRender : RenderProxyBox
{
    /// <summary>Fraction of the child's natural height to report as this box's height, in [0, 1].
    /// 1 = full height (no collapse), 0 = fully collapsed. A change relayouts so the parent reflows.</summary>
    public float Factor
    {
        get => field;
        set => SetProperty(ref field, Math.Clamp(value, 0f, 1f), relayout: true);
    } = 1f;

    protected override void PerformLayout()
    {
        if (Children.Count == 0)
        {
            Size = Constraints.Constrain(Vector2.Zero);
            return;
        }

        var child = Children[0];
        child.X = 0;
        child.Y = 0;
        // Lay the child out at full constraints so it keeps its natural (un-squished) width and its
        // real content height; we only shrink the height WE report to our parent.
        child.Layout(Constraints);

        Size = Constraints.Constrain(new Vector2(child.Size.X, child.Size.Y * Factor));
    }

    public override void Paint(PaintingContext context)
    {
        // Clip to the (shrinking) reported box so the child is revealed/hidden top-down rather than
        // overflowing past the collapsed height — mirrors RenderAnimatedSize.Paint.
        if (context.Canvas == null)
        {
            base.Paint(context);
            return;
        }

        context.Canvas.Save();
        context.Canvas.ClipRect(new SKRect(0, 0, Size.X, Size.Y));
        base.Paint(context);
        context.Canvas.Restore();
    }
}

/// <summary>The create/update bridge for <see cref="ScribeHeightFactorRender"/>. A
/// <see cref="SingleChildWidget"/> so the framework threads the child's render object in automatically
/// (same plumbing as LibGUI's <c>Opacity</c>).</summary>
internal sealed class ScribeHeightFactorWidget : SingleChildWidget
{
    public ScribeHeightFactorWidget(float factor, Widget? child = null, Gui.Widgets.Framework.Key? key = null) : base(child, key)
    {
        Factor = factor;
    }

    public float Factor { get; }

    public override RenderObject CreateRenderObject() => new ScribeHeightFactorRender { Factor = Factor };

    public override void UpdateRenderObject(RenderObject renderObject) =>
        ((ScribeHeightFactorRender)renderObject).Factor = Factor;
}

/// <summary>
/// Host-owned animation state for a list's animating rows (gui-row-animation-harness). The host (HUD or
/// lectern dialog) owns one registry for its lifetime and passes it into each <see cref="ScribeRowSizeAnimation"/>,
/// which looks up its <see cref="AnimationController"/> by the row's stable id. Because the controller
/// lives here — not in the transient widget's <c>State</c> — it SURVIVES the host's <c>ForceRebuild</c>
/// (which remounts the widget) AND a reconcile <c>SetState</c> (which reuses the element only when
/// type+key match), and RESUMES from its elapsed progress instead of restarting. Mirrors the
/// <see cref="ScribeNumericFocusRegistry"/> persistent-node pattern.
///
/// <para>Direction-neutral: the controller always runs 0→1; the widget maps that value to a height
/// factor per its <see cref="ScribeRowSizeDirection"/> (collapse renders <c>1 − value</c>, reveal
/// renders <c>value</c>), so one registry serves both motions.</para>
/// </summary>
internal sealed class ScribeAnimationRegistry
{
    private readonly Dictionary<string, AnimationController> controllers = new();

    /// <summary>The persistent animation controller for a row id, created (and started
    /// <see cref="AnimationController.Forward"/>) on first request and resumed on later requests, so a
    /// remounted <see cref="ScribeRowSizeAnimation"/> picks up where it left off. The controller runs
    /// 0→1 over the duration; the widget maps that value to its height factor per its direction.</summary>
    public AnimationController Controller(string id, TimeSpan duration, ITickerProvider vsync)
    {
        if (!controllers.TryGetValue(id, out var controller))
        {
            controller = new AnimationController(duration, vsync);
            controllers[id] = controller;
            controller.Forward();
        }
        else if (controller.Status != AnimationStatus.Completed)
        {
            // Resume after a remount paused the ticker (Dispose of the prior widget's subscription
            // doesn't stop the controller, but a fresh mount should ensure it keeps advancing).
            controller.Resume();
        }
        return controller;
    }

    /// <summary>Whether the animation for this id has already finished (reached its terminal factor). The
    /// host uses this to guard against re-arming a completed animation before its cleanup is processed.</summary>
    public bool IsComplete(string id) =>
        controllers.TryGetValue(id, out var c) && c.Status == AnimationStatus.Completed;

    /// <summary>Whether ANY owned animation is still running (has not reached
    /// <see cref="AnimationStatus.Completed"/>). While this is true the list geometry is still reflowing
    /// each frame, so a row can slide under a stationary cursor — the host uses this to drive a per-frame
    /// hover refresh (fix-list-collapse-stale-hover) since LibGUI only recomputes hover on real mouse
    /// motion. False (the steady state) means the host skips the refresh entirely.</summary>
    public bool AnyAnimating
    {
        get
        {
            foreach (var c in controllers.Values)
                if (c.Status != AnimationStatus.Completed) return true;
            return false;
        }
    }

    /// <summary>Release a row's animation controller once its cleanup (removal, or settle) has been
    /// handled, so the id is free to be reused by a future row without inheriting stale animation state.</summary>
    public void Release(string id)
    {
        if (controllers.Remove(id, out var controller)) controller.Dispose();
    }

    /// <summary>Dispose all owned controllers (the host calls this in its own Dispose).</summary>
    public void Dispose()
    {
        foreach (var controller in controllers.Values) controller.Dispose();
        controllers.Clear();
    }
}

/// <summary>Which way a <see cref="ScribeRowSizeAnimation"/> drives its height (gui-row-animation-harness).
/// The underlying controller always runs 0→1; the direction only changes how that value maps to the
/// rendered height factor and what the terminal state means.</summary>
internal enum ScribeRowSizeDirection
{
    /// <summary>Exit: shrink 1→0 (factor <c>1 − value</c>), then fire <c>onEnd</c> so the host removes the
    /// row. The load-bearing original case (a departing list row sliding closed).</summary>
    Collapse,

    /// <summary>Enter: grow 0→1 (factor <c>value</c>). A freshly-mounted row slides into place instead of
    /// popping in; there is nothing to remove, so <c>onEnd</c> is optional (a settle hook). The
    /// "ScribeRevealable" sketch (docs/animation-lessons-learned.md) — the on-ramp for future
    /// enter animations; no caller ships in this change.</summary>
    Reveal,
}

/// <summary>
/// Wraps a list row so its height animates in one direction (gui-row-animation-harness): a
/// <see cref="ScribeRowSizeDirection.Collapse"/> shrinks it 1→0 (rows below slide up to meet it) then
/// fires <see cref="OnEnd"/> so the host can remove it; a <see cref="ScribeRowSizeDirection.Reveal"/>
/// grows it 0→1 so it slides into place, with <see cref="OnEnd"/> an optional settle hook.
///
/// <para>When <c>animating</c> is false this is a pass-through at full height (no controller, no cost).
/// When true, it obtains its <see cref="AnimationController"/> from the host-owned
/// <see cref="ScribeAnimationRegistry"/> by <c>id</c> — so the animation resumes rather than restarts
/// across the host's <c>ForceRebuild</c> remounts <em>and</em> across a reconcile <c>SetState</c> —
/// subscribes a per-frame repaint, and renders a height factor per its <see cref="Direction"/> via
/// <see cref="ScribeHeightFactorWidget"/>. <see cref="OnEnd"/> fires exactly once, when the controller
/// reaches <see cref="AnimationStatus.Completed"/>.</para>
/// </summary>
internal sealed class ScribeRowSizeAnimation : StatefulWidget
{
    /// <summary>Default animation duration. Short enough to feel snappy, long enough to read as motion;
    /// tunable if playtest wants a different feel.</summary>
    public const int DefaultDurationMs = 200;

    public ScribeRowSizeAnimation(
        string id,
        bool animating,
        ScribeRowSizeDirection direction,
        ScribeAnimationRegistry registry,
        Widget child,
        Action? onEnd = null,
        int durationMs = DefaultDurationMs,
        Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        Id = id;
        Animating = animating;
        Direction = direction;
        Registry = registry;
        OnEnd = onEnd;
        Child = child;
        DurationMs = durationMs;
    }

    /// <summary>Stable identity of the animating row, keying its controller in the registry.</summary>
    public string Id { get; }
    public bool Animating { get; }
    public ScribeRowSizeDirection Direction { get; }
    public ScribeAnimationRegistry Registry { get; }
    /// <summary>Fired exactly once when the animation completes. Required in practice for
    /// <see cref="ScribeRowSizeDirection.Collapse"/> (the host removes the departing row); optional for
    /// <see cref="ScribeRowSizeDirection.Reveal"/> (nothing to remove — a settle hook).</summary>
    public Action? OnEnd { get; }
    public Widget Child { get; }
    public int DurationMs { get; }

    public override State CreateState() => new ScribeRowSizeAnimationState();
}

internal sealed class ScribeRowSizeAnimationState : State<ScribeRowSizeAnimation>
{
    /// <summary>Eased so the motion decelerates as it settles (calmer than linear). Shared by both
    /// directions — a symmetric ease reads the same opening and closing.</summary>
    private static readonly Curve SizeCurve = Curves.EaseInOutCubic;

    private AnimationController? controller;

    public override void InitState()
    {
        base.InitState();
        if (!Widget.Animating) return;

        controller = Widget.Registry.Controller(
            Widget.Id, TimeSpan.FromMilliseconds(Widget.DurationMs), Element.Owner!.GetTickerProvider());
        controller.OnValueChanged += OnValueChanged;
        controller.OnStatusChanged += OnStatusChanged;

        // If the animation already finished while this row was between remounts, fire the end callback now
        // (the status-changed event won't fire again for an already-Completed controller).
        if (controller.Status == AnimationStatus.Completed) Widget.OnEnd?.Invoke();
    }

    private void OnValueChanged(double _) => Element.MarkNeedsBuild();

    private void OnStatusChanged(AnimationStatus status)
    {
        if (status == AnimationStatus.Completed) Widget.OnEnd?.Invoke();
    }

    public override Widget Build(BuildContext context)
    {
        if (controller == null) return Widget.Child; // not animating: full height, pass-through

        // The controller always runs 0→1; map it to a height factor per direction. Collapse closes
        // (1→0), Reveal opens (0→1).
        float eased = (float)SizeCurve.Transform(controller.Value);
        float factor = Widget.Direction == ScribeRowSizeDirection.Collapse ? 1f - eased : eased;
        return new ScribeHeightFactorWidget(factor, Widget.Child);
    }

    public override void Dispose()
    {
        // Detach this (transient) widget's handlers, but do NOT dispose the controller — it is owned by
        // the host registry so it survives the ForceRebuild remount / reconcile and the next mount resumes it.
        if (controller != null)
        {
            controller.OnValueChanged -= OnValueChanged;
            controller.OnStatusChanged -= OnStatusChanged;
            controller = null;
        }
        base.Dispose();
    }
}

/// <summary>
/// Slides a row IN by translating its content into place while fading it up (gui-row-animation-harness /
/// animate-row-insertion). The entry motion for a freshly-added row: the row takes its FULL height in its
/// slot from the first frame (the translate is paint-only — <see cref="Transform"/> passes layout constraints
/// through unchanged), so unlike <see cref="ScribeRowSizeDirection.Reveal"/> (which grows the row's height and
/// so shrinks/mislocates a variable-height row's caret mid-animation) the caret, pointer hit-tests, and
/// scroll-into-view all work against the row's final geometry immediately. Only the painted content moves:
/// it starts offset upward and settles to rest, cross-fading in as it arrives.
///
/// <para>Translation is the primary read (a moving row is unmistakable where a same-position fade is too
/// subtle — the 2026-08-12 playtest of the fade-only entry "appeared instantly"); the fade is layered polish
/// off the SAME controller value, so there is one controller and no per-row bookkeeping doubling.
/// <see cref="RenderTransform.GlobalToChild"/> inverts the matrix for hit-testing, so a click lands where the
/// row is DRAWN mid-slide, not where it will rest.</para>
///
/// <para>Same load-bearing survival discipline as <see cref="ScribeRowSizeAnimation"/>, and deliberately NOT
/// <c>AnimatedSlide</c>/<c>AnimatedOpacity</c>/<c>ScribeFadeText</c> (all snap on <c>ForceRebuild</c>: the
/// implicit widgets re-init Begin==End==target on a fresh mount, <c>ScribeFadeText</c> owns its controller in
/// its own transient State — see the resolved Open Question in animate-row-insertion/design.md). The
/// controller lives in the host-owned <see cref="ScribeAnimationRegistry"/>, keyed by the row id, so the
/// slide RESUMES across a <c>ForceRebuild</c> remount AND a reconcile <c>SetState</c> instead of restarting.</para>
///
/// <para>Opacity floor: <c>RenderOpacity</c> skips painting entirely at α ≤ 0.001, which for the auto-focused
/// new row would be a one-frame invisible-but-focused row (a live caret in an unpainted row). So the rendered
/// opacity is floored at a small non-zero value — the row is always painted, just faint on frame one.</para>
/// </summary>
internal sealed class ScribeSlideIn : StatefulWidget
{
    public const int DefaultDurationMs = ScribeRowSizeAnimation.DefaultDurationMs;

    /// <summary>How far (logical px) the content starts ABOVE its resting position and travels down into
    /// place. A fixed offset (the widget can't know the row's height at build time, and a fixed distance
    /// reads consistently across one-line and wrapped rows). Small enough to feel like a settle, large
    /// enough to unmistakably read as motion.</summary>
    public const float DefaultSlideDistance = 18f;

    public ScribeSlideIn(
        string id,
        bool animating,
        ScribeAnimationRegistry registry,
        Widget child,
        Action? onEnd = null,
        int durationMs = DefaultDurationMs,
        float slideDistance = DefaultSlideDistance,
        Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        Id = id;
        Animating = animating;
        Registry = registry;
        OnEnd = onEnd;
        Child = child;
        DurationMs = durationMs;
        SlideDistance = slideDistance;
    }

    /// <summary>Stable identity of the sliding-in row, keying its controller in the registry.</summary>
    public string Id { get; }
    public bool Animating { get; }
    public ScribeAnimationRegistry Registry { get; }
    /// <summary>Fired exactly once when the slide completes (the container releases the entry controller).</summary>
    public Action? OnEnd { get; }
    public Widget Child { get; }
    public int DurationMs { get; }
    public float SlideDistance { get; }

    public override State CreateState() => new ScribeSlideInState();
}

internal sealed class ScribeSlideInState : State<ScribeSlideIn>
{
    /// <summary>Eased so the motion decelerates as it settles, matching the size animations' curve for a
    /// consistent feel across every Scribe row animation.</summary>
    private static readonly Curve SlideCurve = Curves.EaseInOutCubic;

    /// <summary>Opacity floor: keep the rendered opacity above <c>RenderOpacity</c>'s α≤0.001 skip-paint
    /// threshold so the auto-focused new row is never invisible-but-focused for a frame. Small enough to
    /// still read as a fade-in from ~nothing.</summary>
    private const float MinOpacity = 0.02f;

    private AnimationController? controller;

    public override void InitState()
    {
        base.InitState();
        if (!Widget.Animating) return;

        controller = Widget.Registry.Controller(
            Widget.Id, TimeSpan.FromMilliseconds(Widget.DurationMs), Element.Owner!.GetTickerProvider());
        controller.OnValueChanged += OnValueChanged;
        controller.OnStatusChanged += OnStatusChanged;

        // If the slide already finished while this row was between remounts, fire the end callback now (the
        // status-changed event won't fire again for an already-Completed controller).
        if (controller.Status == AnimationStatus.Completed) Widget.OnEnd?.Invoke();
    }

    private void OnValueChanged(double _) => Element.MarkNeedsBuild();

    private void OnStatusChanged(AnimationStatus status)
    {
        if (status == AnimationStatus.Completed) Widget.OnEnd?.Invoke();
    }

    public override Widget Build(BuildContext context)
    {
        // ALWAYS render the same Opacity > Transform > child shape, even settled / not animating, so the
        // child subtree structure is IDENTICAL whether animating, completed, or a pass-through. This is
        // load-bearing: the container keeps an entered row wrapped for its whole live lifetime to avoid a
        // type-swap remount (the reconciler is positional-by-type+key — dropping the wrapper would unmount
        // the row's field and drop its caret). If Build swapped between the wrapped and bare child on
        // completion, THAT swap would itself remount the field — so we never do; a settled slide renders
        // Opacity(1) > Transform(identity) > child, both of which delegate straight through to the child.
        if (controller == null)
            return new Opacity(1f, Transform.Translate(Widget.Child, Vector2.Zero)); // stable pass-through

        float eased = (float)SlideCurve.Transform(controller.Value);
        float opacity = Math.Max(MinOpacity, eased); // floor so a focused row always paints
        // Start offset UP by SlideDistance (content sits above its slot) and travel to 0 as the animation
        // completes, so the row drops down into place. Negative Y is up in LibGUI's top-left origin space.
        float offsetY = -Widget.SlideDistance * (1f - eased);
        return new Opacity(opacity, Transform.Translate(Widget.Child, new Vector2(0f, offsetY)));
    }

    public override void Dispose()
    {
        // Detach this (transient) widget's handlers; the controller is host-owned, so leave it for the next
        // mount to resume (identical discipline to ScribeRowSizeAnimation).
        if (controller != null)
        {
            controller.OnValueChanged -= OnValueChanged;
            controller.OnStatusChanged -= OnStatusChanged;
            controller = null;
        }
        base.Dispose();
    }
}
