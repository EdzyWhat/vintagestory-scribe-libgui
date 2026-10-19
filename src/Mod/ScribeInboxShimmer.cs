using System;
using Gui.Widgets.Animations;   // AnimationController, AnimationStatus
using Gui.Widgets.Framework;    // StatefulWidget, State, BuildContext, Key, Widget
using Gui.Widgets.Painting;     // ShaderMask, LinearGradient, GradientStop
using OpenTK.Mathematics;       // Vector2, Vector4
using SkiaSharp;                // SKShader, SKBlendMode

namespace Scribe;

/// <summary>
/// The Inbox nav-button shimmer: a periodic highlight sweep across a nav button's icon while
/// <see cref="Active"/> is true. A <see cref="ShaderMask"/> wraps the icon with a <see cref="LinearGradient"/>
/// <see cref="SKShader"/> built once; the sweep animates only <see cref="ShaderMask.OffsetX"/>, since
/// <see cref="RenderShaderMask"/> draws an oversized rect clipped to the button's bounds.
///
/// <para><see cref="SKBlendMode.SrcATop"/> recolors only pixels the child already painted (the icon glyph).</para>
///
/// <para>Driven by an <see cref="AnimationController"/> restarted from 0 on <see cref="AnimationStatus.Completed"/>.
/// The controller is owned by this widget's <c>State</c>, not the shared <c>ScribeAnimationRegistry</c>: a
/// decorative looping sweep loses nothing when a <c>ForceRebuild</c> remount restarts its phase.</para>
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
    private static readonly TimeSpan SweepDuration = TimeSpan.FromMilliseconds(1600);

    /// <summary>Gradient axis angle in degrees.</summary>
    private const float SweepAngleDeg = 20f;

    /// <summary>Sweep travel each direction, as a multiple of button size, so the band clears both edges.</summary>
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

    // Restart from 0, not a bare Forward(): at value 1.0 that re-fires OnStatusChanged synchronously and recurses forever.
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

        // offsetX runs +travel → -travel, so the highlight enters from the right and exits left
        // (RenderShaderMask.Paint translates the canvas by -offsetX).
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
