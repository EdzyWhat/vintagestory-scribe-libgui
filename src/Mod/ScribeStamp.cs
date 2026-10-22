using Gui.Rendering.Text;         // TextStyle, FontWeight
using Gui.Widgets.Animations;     // AnimationController, AnimationStatus, Curve, Curves
using Gui.Widgets.Basic;          // Text, Container
using Gui.Widgets.Framework;      // Widget, StatefulWidget, State, BuildContext, Key
using Gui.Widgets.Layout;         // Stack, Positioned, SizedBox, Alignment
using Gui.Widgets.Painting;       // Opacity, Transform, BoxStyle
using OpenTK.Mathematics;         // Vector2, Vector4
using SkiaSharp;                  // SKBitmap, SKMatrix
using static Scribe.Core.ScribeMath;

namespace Scribe;

/// <summary>
/// A one-shot, paint-only "rubber stamp" flourish played over the Scriptorium's Duplicate slot when a
/// Transcribe copy lands. A wooden stamp (<c>scribe-copy-stamp.png</c>) descends, presses, lifts and fades,
/// leaving a briefly visible, tilted "COPIED" imprint (drawn procedurally) that then fades away.
///
/// <para>Pure decoration: the copy is server-authoritative and already complete when this plays. A missing
/// asset just skips the wooden stamp.</para>
///
/// <para>The dialog reconciles its body every frame, so this ticks off a host-owned
/// <see cref="ScribeAnimationRegistry"/> controller keyed by <see cref="Id"/>, resuming across rebuilds
/// (mirrors <see cref="ScribeSlideIn"/>). Each new copy bumps the generation in the id AND the widget's
/// <c>ValueKey</c> so a fresh <see cref="State"/> mounts and plays again. <see cref="OnEnd"/> fires once on
/// completion so the host can release the controller and drop the overlay.</para>
/// </summary>
internal sealed class ScribeStamp : StatefulWidget
{
    /// <summary>Total play time in milliseconds.</summary>
    public const int DefaultDurationMs = 2100;

    public ScribeStamp(
        string id,
        ScribeAnimationRegistry registry,
        SKBitmap? stampBitmap,
        string copyLabel,
        Vector4 imprintColor,
        Vector4 glowColor,
        float slotSize,
        float artWidth,
        Action? onEnd = null,
        Action? onDescend = null,
        int durationMs = DefaultDurationMs,
        Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        Id = id;
        Registry = registry;
        StampBitmap = stampBitmap;
        CopyLabel = copyLabel;
        ImprintColor = imprintColor;
        GlowColor = glowColor;
        SlotSize = slotSize;
        ArtWidth = artWidth;
        OnEnd = onEnd;
        OnDescend = onDescend;
        DurationMs = durationMs;
    }

    public string Id { get; }
    public ScribeAnimationRegistry Registry { get; }
    public SKBitmap? StampBitmap { get; }
    public string CopyLabel { get; }
    public Vector4 ImprintColor { get; }
    /// <summary>Theme <c>Surface</c> colour for the "COPIED" imprint's outer glow, which backs the letters
    /// (the imprint box fill is transparent) and haloes outward.</summary>
    public Vector4 GlowColor { get; }
    /// <summary>The slot's edge length: the fixed layout FOOTPRINT, so the flourish never resizes the slot.</summary>
    public float SlotSize { get; }
    /// <summary>Drawn width of both the stamp and the imprint, in logical px (the Pixel Art Size setting × 0.2).
    /// Deliberately wider than the slot: the visuals spill over its sides (paint-only, unclipped), centred on it.</summary>
    public float ArtWidth { get; }
    public Action? OnEnd { get; }
    /// <summary>Fired ONCE at the contact frame (<see cref="ScribeStampState.DescendEnd"/>), for the stamp
    /// thump. Only fires when the widget is mounted and seen; a late remount already past the descend does
    /// not fire it (see InitState).</summary>
    public Action? OnDescend { get; }
    public int DurationMs { get; }

    public override State CreateState() => new ScribeStampState();
}

internal sealed class ScribeStampState : State<ScribeStamp>
{
    /// <summary>Normalized time at which the stamp lands. Shared by <see cref="Build"/> and
    /// <see cref="OnValueChanged"/> so the thump lands exactly when the stamp visually lands.</summary>
    internal const float DescendEnd = 0.24f;

    private AnimationController? controller;
    /// <summary>Ensures <see cref="ScribeStamp.OnDescend"/> fires once per play.</summary>
    private bool descendFired;

    public override void InitState()
    {
        base.InitState();
        controller = Widget.Registry.Controller(
            Widget.Id, TimeSpan.FromMilliseconds(Widget.DurationMs), Element.Owner!.GetTickerProvider());
        controller.OnValueChanged += OnValueChanged;
        controller.OnStatusChanged += OnStatusChanged;

        // The status event won't fire again for an already-Completed controller.
        if (controller.Status == AnimationStatus.Completed) Widget.OnEnd?.Invoke();

        // A late remount at/past the contact frame missed the landing, so suppress the sound cue.
        if (controller.Value >= DescendEnd || controller.Status == AnimationStatus.Completed) descendFired = true;
    }

    private void OnValueChanged(double value)
    {
        // `>=` plus the once-flag: a frame that hitches past DescendEnd still fires exactly once.
        if (!descendFired && value >= DescendEnd)
        {
            descendFired = true;
            Widget.OnDescend?.Invoke();
        }
        Element.MarkNeedsBuild();
    }

    private void OnStatusChanged(AnimationStatus status)
    {
        if (status == AnimationStatus.Completed) Widget.OnEnd?.Invoke();
    }

    public override Widget Build(BuildContext context)
    {
        float t = controller is null ? 0f : (float)controller.Value;
        float slot = Widget.SlotSize;
        float art = Widget.ArtWidth;

        // Distances scale with the art; the lift retraces exactly the descent.
        float descendDistance = art * 0.585f;
        float liftDistance = descendDistance;

        // Wooden stamp: fade-in + descend, squash press, then lift + fade-out. The lift starts when the press ends.
        const float PressLen = 0.115f * 0.85f;
        const float PressEnd = DescendEnd + PressLen;
        const float LiftStart = PressEnd;
        const float LiftSpan = 0.155f;
        const float LiftEnd = LiftStart + LiftSpan;
        float descend = EaseInSine(Seg(t, 0f, DescendEnd));
        // Fade-in over the first half of the descent; fade-out over the second half of the lift.
        float fadeIn = Seg(t, 0f, DescendEnd * 0.5f);
        // Press: a sine bump 0→1→0 peaking at the bottom of the travel.
        float press = Sine01(Seg(t, DescendEnd, PressEnd));
        float lift = EaseInOutSine(Seg(t, LiftStart, LiftEnd));
        float fadeOut = Seg(t, LiftStart + LiftSpan * 0.5f, LiftEnd);

        // Persistent 1%-of-art downward nudge at every phase.
        float offsetY = Lerp(-descendDistance, 0f, descend) + Lerp(0f, -liftDistance, lift) + art * 0.01f;
        // Squash is anchored to the BOTTOM edge (Alignment.BottomCenter below) so it presses DOWN into the page.
        float squashX = 1f + 0.03f * press;
        float squashY = 1f - 0.048f * press;
        float stampOpacity = Clamp01(Math.Min(fadeIn, 1f - fadeOut));

        // "COPIED" imprint: snaps to full opacity at contact (t = DescendEnd), holds, then fades out by t = 1.
        const float ImprintFadeStart = 0.9268f;
        const float ImprintEnd = 1f;
        float imprintOut = Seg(t, ImprintFadeStart, ImprintEnd);
        float imprintScale = 1f; // no pop — appears at rest size
        float imprintOpacity = t >= DescendEnd ? Clamp01(1f - imprintOut) : 0f;
        const float ImprintTilt = -0.06f; // subtle lean, ~ -3.4°
        const float ImprintSizeScale = 0.92f; // scales the imprint box + text

        var children = new System.Collections.Generic.List<Widget>();

        // Added to the Stack FIRST so the imprint (ink on the page) paints UNDERNEATH the wooden stamp.
        if (imprintOpacity > 0.001f)
        {
            // No explicit Width/Height: the Container shrink-wraps to the text + padding (Center loosens
            // constraints), so the border stays framed if font metrics change.
            var imprint = new Container(
                style: new BoxStyle
                {
                    Color = new Vector4(0f, 0f, 0f, 0f),         // transparent fill — just the ink outline
                    CornerRadius = new Vector4(3f),
                    BorderThickness = 2f,
                    BorderColor = Widget.ImprintColor,
                    Padding = Gui.Rendering.EdgeInsets.Symmetric(horizontal: 6f, vertical: 3f),
                    // Outer glow in the page colour. The fill is transparent, so the glow also shows through
                    // the interior, backing the letters over whatever sits under the slot. Scales with art.
                    BoxShadows = new[]
                    {
                        new BoxShadow(
                            Color: Widget.GlowColor,
                            Offset: Vector2.Zero,
                            BlurRadius: art * 0.03f,
                            SpreadRadius: art * 0.04f),
                    },
                },
                child: new Text(Widget.CopyLabel,
                    new TextStyle
                    {
                        FontSize = art * 0.2f * ImprintSizeScale,
                        FontFamily = ScribeTaskFont.ButtonFamily,   // Caudex
                        Weight = FontWeight.Bold,
                        Color = Widget.ImprintColor,
                    }));

            // Lay out in an ART-WIDE box, not slot-wide: a ~48px box caps the text width and wraps
            // "COPIED". It spills over the slot's sides (unclipped) like the stamp.
            children.Add(new Positioned(
                left: (slot - art) / 2f, top: (slot - art) / 2f, width: art, height: art,
                child: new Center(child: new Opacity(imprintOpacity,
                    new Transform(
                        child: imprint,
                        matrix: SKMatrix.CreateRotation(ImprintTilt).PreConcat(SKMatrix.CreateScale(imprintScale, imprintScale)),
                        alignment: Alignment.Center)))));
        }

        // Landing shadow: a static contact shadow that fades in over the descend and out over the lift.
        // Added after the imprint but before the stamp, so it paints above the ink and below the stamp.
        float shadowVisibility = Clamp01(Math.Min(Seg(t, 0f, DescendEnd), 1f - Seg(t, LiftStart, LiftEnd)));
        if (shadowVisibility > 0.001f)
        {
            const float ShadowPeakAlpha = 0.7f;
            float shadowW = art * 0.55f;             // ≈ the stamp's flat base
            float shadowH = art * 0.25f;
            var shadow = new Container(style: new BoxStyle
            {
                Color = new Vector4(0f, 0f, 0f, 0f),          // no fill — only the blurred shadow shows
                Width = shadowW,
                Height = shadowH,
                CornerRadius = new Vector4(shadowH * 0.5f),   // pill-ish rounded rectangle
                BoxShadows = new[]
                {
                    new BoxShadow(
                        Color: new Vector4(0f, 0f, 0f, ShadowPeakAlpha),
                        Offset: Vector2.Zero,
                        BlurRadius: art * 0.1f),
                },
            });
            children.Add(new Positioned(
                left: (slot - art) / 2f, top: (slot - art) / 2f, width: art, height: art,
                child: new Center(child: new Opacity(shadowVisibility, shadow))));
        }

        // Painted on top of the imprint; skipped if the asset failed to load.
        if (Widget.StampBitmap is { } bmp && stampOpacity > 0.001f)
        {
            float stampW = art;
            float stampH = stampW * bmp.Height / bmp.Width;
            // Squash anchored to the bottom edge; the outer Transform applies the translate.
            var m = SKMatrix.CreateScale(squashX, squashY);
            children.Add(new Positioned(
                left: (slot - stampW) / 2f,
                top: slot - stampH,                 // rest with the stamp's base at the slot's bottom edge
                width: stampW, height: stampH,
                child: new Opacity(stampOpacity,
                    Transform.Translate(
                        new Transform(
                            child: new ScribePixelArtBackdrop(bmp, new SizedBox(width: stampW, height: stampH)),
                            matrix: m,
                            alignment: Alignment.BottomCenter),
                        new Vector2(0f, offsetY)))));
        }

        // Fixed slot footprint; every visual is a Positioned overlay that may overflow it.
        return new SizedBox(width: slot, height: slot,
            children.Count == 0 ? null : new Stack(children: children.ToArray()));
    }

    public override void Dispose()
    {
        // Detach this transient widget's handlers; the controller is host-owned (released by the host on end).
        if (controller != null)
        {
            controller.OnValueChanged -= OnValueChanged;
            controller.OnStatusChanged -= OnStatusChanged;
            controller = null;
        }
        base.Dispose();
    }

    // ---- small easing / mapping helpers (kept local; the row animations use Curves for their eased 0→1) ----
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    /// <summary>Map <paramref name="t"/> from [lo,hi] onto 0..1, clamped (0 below lo, 1 above hi).</summary>
    private static float Seg(float t, float lo, float hi) => hi <= lo ? (t >= hi ? 1f : 0f) : Clamp01((t - lo) / (hi - lo));
    /// <summary>Sine ease-in: slow start accelerating to the end (the entering/descend translation).</summary>
    private static float EaseInSine(float x) => 1f - (float)Math.Cos((x * Math.PI) / 2.0);
    /// <summary>Sine ease-in-out: eased at both ends (the leaving/lift translation).</summary>
    private static float EaseInOutSine(float x) => -((float)Math.Cos(Math.PI * x) - 1f) / 2f;
    /// <summary>A 0→1→0 bump over [0,1] (sine half-period) — used for the momentary press.</summary>
    private static float Sine01(float x) => (float)Math.Sin(x * Math.PI);
}
