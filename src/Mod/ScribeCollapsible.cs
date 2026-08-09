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

    /// <summary>Whether ANY owned collapse is still animating (has not reached
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
