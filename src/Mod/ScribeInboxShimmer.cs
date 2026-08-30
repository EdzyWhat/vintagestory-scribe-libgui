using System;
using Gui.Widgets.Animations;   // AnimationController, AnimationStatus
using Gui.Widgets.Framework;    // StatefulWidget, State, BuildContext, Key, Widget
using Gui.Widgets.Painting;     // ShaderMask, LinearGradient, GradientStop
using OpenTK.Mathematics;       // Vector2, Vector4
using SkiaSharp;                // SKShader, SKBlendMode

namespace Scribe;

/// <summary>
/// The Inbox nav-button shimmer (add-assignment-and-quest-support §8.5, design.md Decision 9b): a
/// periodic highlight sweep across a nav button's icon while <see cref="Active"/> is true, matching
/// the Flutter shimmer-loading cookbook pattern the design doc cites almost exactly —
/// <see cref="ShaderMask"/> wrapping the icon, painted with a <see cref="LinearGradient"/>-built
/// <see cref="SKShader"/> (built ONCE, not rebuilt every frame) whose visible position is animated
/// purely by translating <see cref="ShaderMask.OffsetX"/> — <see cref="RenderShaderMask"/> draws an
/// oversized rect and clips it to the button's bounds, so sliding the offset sweeps the same shader
/// pattern fully across and back off both edges without ever regenerating gradient stops.
///
/// <para><see cref="SKBlendMode.SrcATop"/> recolors only pixels the child ALREADY painted (the icon
/// glyph) toward the highlight, leaving the surrounding transparent box untouched — the same trick the
/// cookbook's gradient overlay relies on.</para>
///
/// <para>Driven by a plain <see cref="AnimationController"/> — the SAME primitive
/// <c>ScribeRowSizeAnimation</c>/<c>ScribeAnimatedList</c> use (design.md: "reuse whichever looping/
/// continuous-tick animation-driving pattern... rather than inventing a new ticker") — restarted from
/// 0 on <see cref="AnimationStatus.Completed"/> to loop. UNLIKE those, this controller is owned locally
/// by this widget's own <c>State</c>, not the shared <c>ScribeAnimationRegistry</c>: a row collapse/
/// reveal's direction and end-state are meaningful and must survive a host <c>ForceRebuild</c> remount
/// intact, but a shimmer is a purely decorative, perpetually-looping sweep — a remount simply
/// restarting its phase from 0 is imperceptible, so the extra registry plumbing (threading an id +
/// the registry instance through every <c>TitleButton</c> call site across four dialog subclasses)
/// isn't justified here.</para>
/// </summary>
internal sealed class ScribeShimmerWrap : StatefulWidget
{
    public ScribeShimmerWrap(bool active, float size, Widget child, Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        Active = active;
        Size = size;
        Child = child;
    }

    public bool Active { get; }
    public float Size { get; }
    public Widget Child { get; }

    public override State CreateState() => new ScribeShimmerWrapState();
}

internal sealed class ScribeShimmerWrapState : State<ScribeShimmerWrap>
{
    /// <summary>One full sweep cycle — playtest-tunable, not final.</summary>
    private static readonly TimeSpan SweepDuration = TimeSpan.FromMilliseconds(1600);

    /// <summary>Gradient axis angle (degrees) — a slight diagonal, matching the typical shimmer look.</summary>
    private const float SweepAngleDeg = 20f;

    /// <summary>How far beyond the button's own width the sweep travels each direction (as a multiple
    /// of the button size), so the highlight band fully clears both edges before looping.</summary>
    private const float TravelMargin = 1.2f;

    private AnimationController? controller;
    private SKShader? shader;
    private float shaderBuiltForSize = -1f;

    public override void InitState()
    {
        base.InitState();
        if (Widget.Active) StartLoop();
    }

    public override void UpdateWidget(ScribeShimmerWrap oldWidget)
    {
        base.UpdateWidget(oldWidget);
        if (Widget.Active && controller is null) StartLoop();
        else if (!Widget.Active && controller is not null) StopLoop();
    }

    private void StartLoop()
    {
        controller = new AnimationController(SweepDuration, Element.Owner!.GetTickerProvider());
        controller.OnValueChanged += OnValueChanged;
        controller.OnStatusChanged += OnStatusChanged;
        controller.Forward();
    }

    private void StopLoop()
    {
        if (controller is null) return;
        controller.OnValueChanged -= OnValueChanged;
        controller.OnStatusChanged -= OnStatusChanged;
        controller.Dispose();
        controller = null;
    }

    private void OnValueChanged(double _) => Element.MarkNeedsBuild();

    // Restart from 0 (NOT a bare Forward()) — Forward() with _value already at 1.0 takes the
    // already-Completed branch and re-fires OnStatusChanged synchronously, recursing forever.
    private void OnStatusChanged(AnimationStatus status)
    {
        if (status == AnimationStatus.Completed) controller?.Forward(0.0);
    }

    public override Widget Build(BuildContext context)
    {
        if (controller is null) return Widget.Child;

        if (shader is null || shaderBuiltForSize != Widget.Size)
        {
            shaderBuiltForSize = Widget.Size;
            shader = new LinearGradient(SweepAngleDeg,
                    new GradientStop(new Vector4(1f, 1f, 1f, 0f), 0f),
                    new GradientStop(new Vector4(1f, 1f, 1f, 0.85f), 0.5f),
                    new GradientStop(new Vector4(1f, 1f, 1f, 0f), 1f))
                .CreateShader(new Vector2(Widget.Size, Widget.Size));
        }

        // t: 0→1 over one sweep. offsetX: +travel → -travel, so the highlight (centered on the shader's
        // own local midpoint) enters from the right and exits to the left of the button's clip bounds
        // (RenderShaderMask.Paint translates the canvas by -offsetX before drawing the shader rect).
        float t = (float)controller.Value;
        float travel = Widget.Size * TravelMargin;
        float offsetX = travel * (1f - 2f * t);

        return new ShaderMask(shader, SKBlendMode.SrcATop, Widget.Child, offsetX: offsetX);
    }

    public override void Dispose()
    {
        StopLoop();
        shader?.Dispose();
        base.Dispose();
    }
}
