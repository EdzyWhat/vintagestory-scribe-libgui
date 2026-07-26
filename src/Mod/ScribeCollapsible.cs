// A reusable "collapse a departing list row to zero height" animation (scribe-list-collapse).
//
// When a row leaves a Scribe list (a HUD unpin/delete completion, or a lectern editor delete) it
// should not vanish in one frame with the rows below snapping up — its height should animate
// smoothly to zero so the rows below slide up to meet it, and it should be removed only once that
// collapse finishes.
//
// The load-bearing constraint (verified against LibGUI source): both the HUD and the lectern
// rebuild via GuiBase.ForceRebuild, which UNMOUNTS and recreates the whole widget tree rather than
// reconciling it. Any stock/implicit animation widget (AnimatedSize/AnimatedContainer/…) recreated
// fresh inits Begin==End==target and SNAPS to the end — no motion. So, exactly like ScribeFadeText,
// the collapse is a self-ticking StatefulWidget that drives its own AnimationController. And because
// EACH ForceRebuild remounts the widget, a State-owned controller would restart from zero every
// rebuild and stutter/never finish — so the controller is owned by a host-held ScribeCollapseRegistry,
// keyed by the row's stable identity, and RESUMED on remount (mirrors the ScribeNumericFocusRegistry
// persistent-node pattern used for the settings fields).
//
// This file is Mod-side only (LibGUI + AnimationController); Core stays API-free and untouched.

using System;
using System.Collections.Generic;
using Gui.Core.Framework;         // RenderObject, RenderProxyBox
using Gui.Rendering;              // PaintingContext
using Gui.Widgets.Animations;     // AnimationController, AnimationStatus, Curve, Curves
using Gui.Widgets.Framework;      // Widget, StatefulWidget, State, BuildContext, SingleChildWidget, Key
using OpenTK.Mathematics;         // Vector2
using SkiaSharp;                  // SKRect

namespace Scribe;

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
/// Host-owned collapse state for a list's departing rows (scribe-list-collapse). The host (HUD or
/// lectern dialog) owns one registry for its lifetime and passes it into each <see cref="ScribeCollapsible"/>,
/// which looks up its <see cref="AnimationController"/> by the row's stable id. Because the controller
/// lives here — not in the transient widget's <c>State</c> — it SURVIVES the host's <c>ForceRebuild</c>
/// (which remounts the widget) and RESUMES from its elapsed progress instead of restarting. Mirrors the
/// <see cref="ScribeNumericFocusRegistry"/> persistent-node pattern.
/// </summary>
internal sealed class ScribeCollapseRegistry
{
    private readonly Dictionary<string, AnimationController> controllers = new();

    /// <summary>The persistent collapse controller for a row id, created (and started
    /// <see cref="AnimationController.Forward"/>) on first request and resumed on later requests, so a
    /// remounted <see cref="ScribeCollapsible"/> picks up where it left off. The animation runs 0→1; the
    /// widget renders height factor <c>1 − value</c>, so the row collapses over the duration.</summary>
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

    /// <summary>Whether a collapse for this id has already finished (reached height zero). The host uses
    /// this to guard against re-arming a completed collapse before its removal is processed.</summary>
    public bool IsComplete(string id) =>
        controllers.TryGetValue(id, out var c) && c.Status == AnimationStatus.Completed;

    /// <summary>Release a row's collapse controller once its removal has been handled, so the id is free
    /// to be reused by a future row without inheriting stale animation state.</summary>
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

/// <summary>
/// Wraps a departing list row so its height collapses smoothly to zero, sliding the rows below up to
/// meet it, then fires <see cref="OnCollapsed"/> once so the host can remove it (scribe-list-collapse).
///
/// <para>When <c>collapsing</c> is false this is a pass-through at full height (no controller, no cost).
/// When true, it obtains its <see cref="AnimationController"/> from the host-owned
/// <see cref="ScribeCollapseRegistry"/> by <c>id</c> — so the animation resumes rather than restarts
/// across the host's <c>ForceRebuild</c> remounts — subscribes a per-frame repaint, and renders a
/// height factor of <c>1 − value</c> via <see cref="ScribeHeightFactorWidget"/>. <see cref="OnCollapsed"/>
/// fires exactly once, when the controller reaches <see cref="AnimationStatus.Completed"/>.</para>
/// </summary>
internal sealed class ScribeCollapsible : StatefulWidget
{
    /// <summary>Default collapse duration. Short enough to feel snappy, long enough to read as motion;
    /// tunable if playtest wants a different feel.</summary>
    public const int DefaultDurationMs = 200;

    public ScribeCollapsible(
        string id,
        bool collapsing,
        ScribeCollapseRegistry registry,
        Action onCollapsed,
        Widget child,
        int durationMs = DefaultDurationMs,
        Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        Id = id;
        Collapsing = collapsing;
        Registry = registry;
        OnCollapsed = onCollapsed;
        Child = child;
        DurationMs = durationMs;
    }

    /// <summary>Stable identity of the departing row, keying its controller in the registry.</summary>
    public string Id { get; }
    public bool Collapsing { get; }
    public ScribeCollapseRegistry Registry { get; }
    public Action OnCollapsed { get; }
    public Widget Child { get; }
    public int DurationMs { get; }

    public override State CreateState() => new ScribeCollapsibleState();
}

internal sealed class ScribeCollapsibleState : State<ScribeCollapsible>
{
    /// <summary>Eased so the collapse decelerates as it closes (calmer than linear).</summary>
    private static readonly Curve CollapseCurve = Curves.EaseInOutCubic;

    private AnimationController? controller;

    public override void InitState()
    {
        base.InitState();
        if (!Widget.Collapsing) return;

        controller = Widget.Registry.Controller(
            Widget.Id, TimeSpan.FromMilliseconds(Widget.DurationMs), Element.Owner!.GetTickerProvider());
        controller.OnValueChanged += OnValueChanged;
        controller.OnStatusChanged += OnStatusChanged;

        // If the collapse already finished while this row was between remounts, fire the removal now
        // (the status-changed event won't fire again for an already-Completed controller).
        if (controller.Status == AnimationStatus.Completed) Widget.OnCollapsed();
    }

    private void OnValueChanged(double _) => Element.MarkNeedsBuild();

    private void OnStatusChanged(AnimationStatus status)
    {
        if (status == AnimationStatus.Completed) Widget.OnCollapsed();
    }

    public override Widget Build(BuildContext context)
    {
        if (controller == null) return Widget.Child; // not collapsing: full height, pass-through

        float factor = 1f - (float)CollapseCurve.Transform(controller.Value);
        return new ScribeHeightFactorWidget(factor, Widget.Child);
    }

    public override void Dispose()
    {
        // Detach this (transient) widget's handlers, but do NOT dispose the controller — it is owned by
        // the host registry so it survives the ForceRebuild remount and the next mount resumes it.
        if (controller != null)
        {
            controller.OnValueChanged -= OnValueChanged;
            controller.OnStatusChanged -= OnStatusChanged;
            controller = null;
        }
        base.Dispose();
    }
}
