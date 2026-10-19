// Row-height animation primitives shared by every Scribe animating list row, in either direction:
//   - Collapse (exit): a departing row shrinks 1->0 so rows below slide up, then it is removed.
//   - Reveal (enter): a freshly-mounted row grows 0->1 instead of popping in.
//
// Load-bearing: the HUD and lectern push content via GuiBase.ForceRebuild, which UNMOUNTS and recreates
// the whole widget tree. Stock implicit animation widgets recreated fresh init Begin==End==target and
// SNAP; a State-owned controller would restart from zero every rebuild. So each widget here is a
// self-ticking StatefulWidget whose AnimationController lives in a host-held ScribeAnimationRegistry,
// keyed by the row's stable id, and is RESUMED on remount (same pattern as ScribeNumericFocusRegistry).
// The id key also survives a reconcile SetState.
//
// One widget with a direction enum over one registry, not a family: only the Build factor mapping and
// the terminal callback differ, so the survival logic cannot drift between the two motions.

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

/// <summary>Coordinate math for the collapse-time hover refresh, shared by the two hosts (touches no VS API).</summary>
internal static class ScribeHoverRefresh
{
    /// <summary>Converts raw-pixel mouse coordinates to window-local coordinates, as LibGUI's private
    /// <c>GuiBase.ToWindowLocal(ToLogicalScreen(rawX, rawY))</c> does: divide by the UI scale, then
    /// subtract the window's top-left position.</summary>
    public static Vector2 ToWindowLocal(int rawMouseX, int rawMouseY, float uiScale, Vector2 windowPos)
        => new Vector2(rawMouseX / uiScale, rawMouseY / uiScale) - windowPos;
}

/// <summary>
/// A frame countdown that keeps a synthetic hover refresh running for a few frames PAST its trigger.
/// <c>GuiBase.ForceRebuild</c> mounts a new tree where every element is <c>hovered = false</c> and that
/// tree has no geometry until a LATER frame, so a same-frame refresh hit-tests nothing and the row under
/// a stationary cursor loses its hover-gated controls. Lingering guarantees a refresh lands after layout.
///
/// Armed by an in-flight collapse, and by <see cref="ArmIfRebuilt"/> for ANY tree rebuild, detected
/// centrally by <c>RootElement</c> identity so no call site needs wiring.
/// </summary>
internal sealed class ScribeHoverRefreshLatch
{
    /// <summary>Frames to keep refreshing after the last trigger; 3 spans the
    /// trigger -> <c>ForceRebuild</c> -> layout -> paint gap for both hosts' <c>OnRenderGUI</c> orderings
    /// (the dialog rebuilds after its base layout, the HUD before).</summary>
    private const int LingerFrames = 3;

    private int remaining;

    /// <summary>Identity of the <c>RootElement</c> last seen by <see cref="ArmIfRebuilt"/>.</summary>
    private object? lastRoot;

    /// <summary>Re-arms to the full linger window.</summary>
    public void Arm() => remaining = LingerFrames;

    /// <summary>Arms the latch if <paramref name="currentRoot"/> differs from the last one seen
    /// (<c>ForceRebuild</c> assigns a fresh <c>RootElement</c>). Call once per frame from
    /// <c>OnRenderGUI</c>. The first call (from null) counts as a rebuild, a harmless refresh on open.</summary>
    public void ArmIfRebuilt(object? currentRoot)
    {
        if (!ReferenceEquals(currentRoot, lastRoot))
        {
            lastRoot = currentRoot;
            Arm();
        }
    }

    /// <summary>Consumes one frame; returns true if a hover refresh should be dispatched this frame.</summary>
    public bool Tick()
    {
        if (remaining <= 0) return false;
        remaining--;
        return true;
    }
}

/// <summary>
/// Render object behind <see cref="ScribeHeightFactorWidget"/>: lays its child out at full constraints
/// but reports its OWN height as <c>childHeight * factor</c> and clips paint to that box, so a parent
/// <c>Column</c> reflows each frame. Like LibGUI's <c>RenderAnimatedSize</c>, but driven externally so it
/// works under the <c>ForceRebuild</c>-only rebuild path.
/// </summary>
internal sealed class ScribeHeightFactorRender : RenderProxyBox
{
    /// <summary>Fraction of the child's natural height to report, in [0, 1]. A change relayouts.</summary>
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
        // Full constraints keep the child's natural width and content height; only the reported height shrinks.
        child.Layout(Constraints);

        Size = Constraints.Constrain(new Vector2(child.Size.X, child.Size.Y * Factor));
    }

    public override void Paint(PaintingContext context)
    {
        // Clip to the shrinking box so the child doesn't overflow past the collapsed height.
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

/// <summary>Create/update bridge for <see cref="ScribeHeightFactorRender"/>.</summary>
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
/// A pass-through that reports its child's measured layout size to the caller after every layout pass,
/// so <see cref="ScribeAnimatedList"/> can learn each row's rendered height without altering layout.
/// <see cref="RenderProxyBox"/>'s base <c>PerformLayout</c> already lays the child out at full
/// constraints and adopts its size.
/// </summary>
internal sealed class ScribeSizeReportRender : RenderProxyBox
{
    /// <summary>Invoked with this box's measured size after every layout pass.</summary>
    public Action<Vector2>? OnMeasured { get; set; }

    protected override void PerformLayout()
    {
        base.PerformLayout();
        OnMeasured?.Invoke(Size);
    }
}

/// <summary>Create/update bridge for <see cref="ScribeSizeReportRender"/>.</summary>
internal sealed class ScribeSizeReportWidget : SingleChildWidget
{
    public ScribeSizeReportWidget(Action<Vector2> onMeasured, Widget child, Gui.Widgets.Framework.Key? key = null)
        : base(child, key)
    {
        OnMeasured = onMeasured;
    }

    public Action<Vector2> OnMeasured { get; }

    public override RenderObject CreateRenderObject() => new ScribeSizeReportRender { OnMeasured = OnMeasured };

    public override void UpdateRenderObject(RenderObject renderObject) =>
        ((ScribeSizeReportRender)renderObject).OnMeasured = OnMeasured;
}

/// <summary>
/// Host-owned animation state for a list's animating rows. The host owns one registry for its lifetime;
/// each <see cref="ScribeRowSizeAnimation"/> looks up its <see cref="AnimationController"/> by row id.
/// Because the controller lives here, not in the transient widget <c>State</c>, it SURVIVES
/// <c>ForceRebuild</c> remounts and reconcile <c>SetState</c>, and RESUMES from its elapsed progress.
///
/// <para>Direction-neutral: the controller always runs 0->1; the widget maps it per its
/// <see cref="ScribeRowSizeDirection"/>.</para>
/// </summary>
internal sealed class ScribeAnimationRegistry
{
    private readonly Dictionary<string, AnimationController> controllers = new();

    /// <summary>Per-id seed values for a motion's fixed starting parameter (e.g.
    /// <see cref="ScribeRowReposition"/>'s <c>TargetOffsetY</c>). Kept here for the same reason as the
    /// controller: LibGUI's reconciler matches slots by index, not id (see <c>VSAPI-NOTES.md</c> § LibGUI),
    /// so a row whose Column index shifts is remounted, and a State-local seed would be reseeded from the
    /// current build's (often zero) value, snapping an in-flight motion to rest.</summary>
    private readonly Dictionary<string, float> seeds = new();

    /// <summary>The persistent controller for a row id: created and started
    /// <see cref="AnimationController.Forward"/> on first request, resumed on later ones.</summary>
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

    /// <summary>The seed for a keyed motion: <paramref name="value"/> on first request for
    /// <paramref name="id"/>, then that SAME original value on every later request, so a re-attach (reconcile
    /// or forced remount) cannot truncate an in-flight motion with a later caller's placeholder value.</summary>
    public float Seed(string id, float value) => seeds.TryGetValue(id, out var existing) ? existing : seeds[id] = value;

    /// <summary>Whether the animation for this id has reached its terminal factor. The host uses this to
    /// avoid re-arming a completed animation before its cleanup is processed.</summary>
    public bool IsComplete(string id) =>
        controllers.TryGetValue(id, out var c) && c.Status == AnimationStatus.Completed;

    /// <summary>Whether ANY owned animation is still running. While true the list geometry reflows each
    /// frame under a possibly stationary cursor, and LibGUI only recomputes hover on real mouse motion,
    /// so the host drives a per-frame hover refresh.</summary>
    public bool AnyAnimating
    {
        get
        {
            foreach (var c in controllers.Values)
                if (c.Status != AnimationStatus.Completed) return true;
            return false;
        }
    }

    /// <summary>Releases a row's controller and seed once its cleanup is handled, so the id can be reused
    /// without inheriting stale state.</summary>
    public void Release(string id)
    {
        if (controllers.Remove(id, out var controller)) controller.Dispose();
        seeds.Remove(id);
    }

    /// <summary>Disposes all owned controllers (the host calls this from its own Dispose).</summary>
    public void Dispose()
    {
        foreach (var controller in controllers.Values) controller.Dispose();
        controllers.Clear();
    }
}

/// <summary>Which way a <see cref="ScribeRowSizeAnimation"/> drives its height. The controller always
/// runs 0->1; the direction changes the height-factor mapping and the meaning of the terminal state.</summary>
internal enum ScribeRowSizeDirection
{
    /// <summary>Exit: shrink 1->0 (factor <c>1 - value</c>), then fire <c>onEnd</c> so the host removes the row.</summary>
    Collapse,

    /// <summary>Enter: grow 0->1 (factor <c>value</c>); <c>onEnd</c> is an optional settle hook.</summary>
    Reveal,
}

/// <summary>
/// Wraps a list row so its height animates per <see cref="Direction"/>: <see cref="ScribeRowSizeDirection.Collapse"/>
/// shrinks it 1->0 then fires <see cref="OnEnd"/> so the host can remove it;
/// <see cref="ScribeRowSizeDirection.Reveal"/> grows it 0->1.
///
/// <para>When <c>animating</c> is false this is a zero-cost pass-through at full height. Otherwise it
/// takes its controller from the host's <see cref="ScribeAnimationRegistry"/> by <c>id</c>, so the motion
/// resumes across remounts. <see cref="OnEnd"/> fires exactly once, on
/// <see cref="AnimationStatus.Completed"/>.</para>
/// </summary>
internal sealed class ScribeRowSizeAnimation : StatefulWidget
{
    /// <summary>Default animation duration.</summary>
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
    /// <summary>Fired exactly once on completion. Needed for Collapse (host removes the row); optional for Reveal.</summary>
    public Action? OnEnd { get; }
    public Widget Child { get; }
    public int DurationMs { get; }

    public override State CreateState() => new ScribeRowSizeAnimationState();
}

internal sealed class ScribeRowSizeAnimationState : State<ScribeRowSizeAnimation>
{
    /// <summary>Decelerating ease shared by both directions.</summary>
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

        // If the animation finished while this row was between remounts, fire the end callback now
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

        float eased = (float)SizeCurve.Transform(controller.Value);
        float factor = Widget.Direction == ScribeRowSizeDirection.Collapse ? 1f - eased : eased;
        return new ScribeHeightFactorWidget(factor, Widget.Child);
    }

    public override void Dispose()
    {
        // Detach handlers but do NOT dispose the controller: the host registry owns it so the next mount resumes it.
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
/// Slides a freshly-added row IN by translating its content into place while fading it up. The row takes
/// its FULL height from the first frame (the translate is paint-only), so unlike
/// <see cref="ScribeRowSizeDirection.Reveal"/> the caret, hit-tests and scroll-into-view work against
/// final geometry immediately. Content starts offset upward and settles to rest.
///
/// <para>Translation and fade share ONE controller value. <see cref="RenderTransform.GlobalToChild"/>
/// inverts the matrix, so a click lands where the row is DRAWN mid-slide.</para>
///
/// <para>Same survival discipline as <see cref="ScribeRowSizeAnimation"/>, and deliberately NOT
/// <c>AnimatedSlide</c>/<c>AnimatedOpacity</c>/<c>ScribeFadeText</c>, which all snap on
/// <c>ForceRebuild</c>. The controller lives in the host's <see cref="ScribeAnimationRegistry"/>, keyed by row id.</para>
///
/// <para>Opacity floor: <c>RenderOpacity</c> skips painting at alpha &lt;= 0.001, which would leave the
/// auto-focused new row invisible-but-focused for a frame, so opacity is floored at a small non-zero value.</para>
/// </summary>
internal sealed class ScribeSlideIn : StatefulWidget
{
    public const int DefaultDurationMs = ScribeRowSizeAnimation.DefaultDurationMs;

    /// <summary>How far (logical px) the content starts ABOVE its resting position. Fixed because the
    /// widget cannot know the row's height at build time.</summary>
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
    private static readonly Curve SlideCurve = Curves.EaseInOutCubic;

    /// <summary>Opacity floor, above <c>RenderOpacity</c>'s alpha &lt;= 0.001 skip-paint threshold.</summary>
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

        // If the slide finished while this row was between remounts, fire the end callback now (the
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
        // ALWAYS render the same Opacity > Transform > child shape, even settled or not animating. The
        // reconciler is positional by type+key, so swapping to the bare child on completion would
        // remount the row's field and drop its caret.
        if (controller == null)
            return new Opacity(1f, Transform.Translate(Widget.Child, Vector2.Zero)); // stable pass-through

        float eased = (float)SlideCurve.Transform(controller.Value);
        float opacity = Math.Max(MinOpacity, eased); // floor so a focused row always paints
        // Starts SlideDistance above its slot (negative Y is up) and travels to 0.
        float offsetY = -Widget.SlideDistance * (1f - eased);
        return new Opacity(opacity, Transform.Translate(Widget.Child, new Vector2(0f, offsetY)));
    }

    public override void Dispose()
    {
        // Detach handlers; the host-owned controller is left for the next mount to resume.
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
/// Displaces a surviving row from its previous rendered position to its new one, a third self-ticking,
/// registry-keyed row motion alongside <see cref="ScribeRowSizeAnimation"/> and <see cref="ScribeSlideIn"/>.
/// Unlike SlideIn's fixed distance, <see cref="TargetOffsetY"/> is the row's REAL measured displacement
/// (computed by <c>ScribeAnimatedList</c> from live row heights); an assumed distance would pop at the
/// start for a row that was visibly elsewhere last frame.
///
/// <para><see cref="TargetOffsetY"/> is captured ONCE when a generation starts, not re-read each build.
/// The container does not reliably rebuild every tick, and a later unrelated rebuild would recompute a
/// near-zero delta and SNAP the motion to rest.</para>
///
/// <para>The seed is stored in <see cref="ScribeAnimationRegistry.Seed"/>, not just a field: the
/// positional reconciler (<c>MultiChildElement.Update</c>, see <c>VSAPI-NOTES.md</c> § LibGUI) remounts a
/// row whose slot shifts, re-running <see cref="ScribeRowRepositionState.InitState"/> with a build whose
/// <c>TargetOffsetY</c> is typically zero. Reading from the registry returns the ORIGINAL value however
/// many times the Element remounts.</para>
///
/// <para>Like <see cref="ScribeSlideIn"/>, a once-wrapped row MUST keep the same wrapper shape for its
/// live lifetime (a settled reposition is <c>Transform.Translate(child, Vector2.Zero)</c>); unwrapping
/// would remount the field and drop a live caret. A row can move more than once, so <see cref="Id"/>
/// embeds a generation number the host bumps per displacement, letting <see cref="UpdateWidget"/> swap to
/// a fresh registry controller without remounting the Element.</para>
/// </summary>
internal sealed class ScribeRowReposition : StatefulWidget
{
    public const int DefaultDurationMs = ScribeRowSizeAnimation.DefaultDurationMs;

    public ScribeRowReposition(
        string id,
        float targetOffsetY,
        ScribeAnimationRegistry registry,
        Widget child,
        int durationMs = DefaultDurationMs,
        Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        Id = id;
        TargetOffsetY = targetOffsetY;
        Registry = registry;
        Child = child;
        DurationMs = durationMs;
    }

    /// <summary>Registry key for this row's CURRENT reposition generation (<c>move:&lt;id&gt;:&lt;gen&gt;</c>);
    /// changes per displacement so a new controller is created instead of resuming a completed one.</summary>
    public string Id { get; }

    /// <summary>The row's total displacement (old Y minus new Y) when this generation started. Read once,
    /// on attach; later builds of the same generation may pass zero, which the State ignores.</summary>
    public float TargetOffsetY { get; }
    public ScribeAnimationRegistry Registry { get; }
    public Widget Child { get; }
    public int DurationMs { get; }

    public override State CreateState() => new ScribeRowRepositionState();
}

internal sealed class ScribeRowRepositionState : State<ScribeRowReposition>
{
    private static readonly Curve MoveCurve = Curves.EaseInOutCubic;

    private AnimationController? controller;
    private string? controllerId;

    /// <summary>Displacement captured once per generation, mirrored from
    /// <see cref="ScribeAnimationRegistry.Seed"/> so it survives a forced remount (a State-local field
    /// alone would be lost when <see cref="InitState"/> re-runs with a stale zero).</summary>
    private float seededOffset;

    public override void InitState()
    {
        base.InitState();
        Attach(Widget.Id, Widget.TargetOffsetY);
    }

    public override void UpdateWidget(ScribeRowReposition oldWidget)
    {
        base.UpdateWidget(oldWidget);
        if (Widget.Id == controllerId) return; // same generation — keep animating toward the seeded offset

        Detach();
        Widget.Registry.Release(oldWidget.Id); // the old generation's controller is done; free it
        Attach(Widget.Id, Widget.TargetOffsetY);
    }

    private void Attach(string id, float offset)
    {
        controllerId = id;
        // Seed returns the ORIGINAL value even if first seeded on a prior mount of this generation.
        seededOffset = Widget.Registry.Seed(id, offset);
        controller = Widget.Registry.Controller(
            id, TimeSpan.FromMilliseconds(Widget.DurationMs), Element.Owner!.GetTickerProvider());
        controller.OnValueChanged += OnValueChanged;
        // No OnEnd: a completed reposition rests at offset zero; the host reads Registry.IsComplete
        // itself to decide when a later displacement needs a fresh generation.
    }

    private void Detach()
    {
        if (controller == null) return;
        controller.OnValueChanged -= OnValueChanged;
        controller = null;
    }

    private void OnValueChanged(double _) => Element.MarkNeedsBuild();

    public override Widget Build(BuildContext context)
    {
        if (controller == null) return Widget.Child;

        float eased = (float)MoveCurve.Transform(controller.Value);
        float offsetY = seededOffset * (1f - eased);
        // ALWAYS the same Transform > child shape, even settled (see ScribeSlideIn.Build).
        return Transform.Translate(Widget.Child, new Vector2(0f, offsetY));
    }

    public override void Dispose()
    {
        // Detach handlers; the host-owned controller is left for the next mount to resume.
        Detach();
        base.Dispose();
    }
}
