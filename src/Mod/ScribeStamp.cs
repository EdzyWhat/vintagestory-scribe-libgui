using System;
using Gui.Rendering.Text;         // TextStyle, FontWeight
using Gui.Widgets.Animations;     // AnimationController, AnimationStatus, Curve, Curves
using Gui.Widgets.Basic;          // Text, Container
using Gui.Widgets.Framework;      // Widget, StatefulWidget, State, BuildContext, Key
using Gui.Widgets.Layout;         // Stack, Positioned, SizedBox, Alignment
using Gui.Widgets.Painting;       // Opacity, Transform, BoxStyle
using OpenTK.Mathematics;         // Vector2, Vector4
using SkiaSharp;                  // SKBitmap, SKMatrix

namespace Scribe;

/// <summary>
/// A one-shot, paint-only "rubber stamp" flourish played over the Scriptorium's Duplicate slot when a
/// Transcribe copy lands (add-transcribe-copy-paste D4). A pixel-art wooden stamp
/// (<c>scribe-copy-stamp.png</c>, baked by <c>build/gen-copy-stamp.py</c>) fades in above the slot,
/// descends onto it, gives a little squash-and-tilt press, then lifts and fades out — leaving a briefly
/// visible, slightly-tilted "COPIED" imprint (rendered procedurally here, not from an asset) that then fades
/// away, revealing the freshly-copied summary underneath.
///
/// <para><b>Non-load-bearing.</b> The copy itself is server-authoritative and already complete by the time
/// this plays (the slot sync fired first); this widget is pure decoration. If the asset is missing the
/// wooden stamp is simply skipped; if the whole widget is never mounted (animation disabled) the copy and
/// the slot update are unaffected.</para>
///
/// <para><b>Survival discipline (mirrors <see cref="ScribeSlideIn"/>).</b> The dialog reconciles its body
/// every frame, so this self-ticks off a host-owned <see cref="ScribeAnimationRegistry"/> controller keyed
/// by <see cref="Id"/> — the motion RESUMES across a reconcile <c>SetState</c> rather than restarting. Each
/// new copy bumps the generation in the id AND the widget's <c>ValueKey</c>, so the reconciler remounts a
/// fresh <see cref="State"/> (new <see cref="InitState"/> → new controller → plays again) instead of reusing
/// the completed one. <see cref="OnEnd"/> fires once on completion so the host can release the controller and
/// drop the overlay.</para>
/// </summary>
internal sealed class ScribeStamp : StatefulWidget
{
    /// <summary>Total play time. Retimed 850 → 2400 → 3000 → 2100ms (refinements 2026-08-16): every stamp beat
    /// runs slower than the original so the descend/press/lift reads clearly, but the last pass trimmed the whole
    /// thing 30% (3000 × 0.7) for a snappier flourish. The "COPIED" imprint still pops in as the stamp lifts,
    /// HOLDS, then fades. Long enough to savour, still short enough that a re-copy remounts a fresh play. Tunable
    /// if playtest wants a different feel.</summary>
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
        DurationMs = durationMs;
    }

    public string Id { get; }
    public ScribeAnimationRegistry Registry { get; }
    public SKBitmap? StampBitmap { get; }
    public string CopyLabel { get; }
    public Vector4 ImprintColor { get; }
    /// <summary>The parchment page-background colour used for the "COPIED" imprint's outer glow (refinement
    /// 2026-08-17). Passed in from the active theme's <c>Surface</c> so the glow tracks the palette; the
    /// transparent-filled imprint box lets this blurred parchment blob back the letters AND halo outward.</summary>
    public Vector4 GlowColor { get; }
    /// <summary>The slot's edge length — the fixed, layout-affecting FOOTPRINT of this overlay, so the
    /// flourish never resizes the slot regardless of how big the visuals are.</summary>
    public float SlotSize { get; }
    /// <summary>The drawn width of BOTH the wooden stamp and the "Copied" imprint, in logical px, pegged by
    /// the caller to the player's Pixel Art Size setting (× 0.2, so 600 → 120). Larger than the 48px slot on
    /// purpose: the visuals spill over the slot's sides (paint-only, unclipped), centred on the slot.</summary>
    public float ArtWidth { get; }
    public Action? OnEnd { get; }
    public int DurationMs { get; }

    public override State CreateState() => new ScribeStampState();
}

internal sealed class ScribeStampState : State<ScribeStamp>
{
    private AnimationController? controller;

    public override void InitState()
    {
        base.InitState();
        controller = Widget.Registry.Controller(
            Widget.Id, TimeSpan.FromMilliseconds(Widget.DurationMs), Element.Owner!.GetTickerProvider());
        controller.OnValueChanged += OnValueChanged;
        controller.OnStatusChanged += OnStatusChanged;

        // If the play finished while this widget was between remounts, fire the end callback now (the
        // status event won't fire again for an already-Completed controller).
        if (controller.Status == AnimationStatus.Completed) Widget.OnEnd?.Invoke();
    }

    private void OnValueChanged(double _) => Element.MarkNeedsBuild();

    private void OnStatusChanged(AnimationStatus status)
    {
        if (status == AnimationStatus.Completed) Widget.OnEnd?.Invoke();
    }

    public override Widget Build(BuildContext context)
    {
        float t = controller is null ? 0f : (float)controller.Value;
        float slot = Widget.SlotSize;
        float art = Widget.ArtWidth;

        // Descend/lift distances scale with the art so the motion reads the same at any Pixel Art Size.
        // The lift travels back up EXACTLY the distance it descended (refinement 2026-08-16), so the stamp
        // retreats the way it arrived instead of appearing to just fade in place.
        // Travel bumped 30% (0.45 → 0.585; feedback 2026-08-16): the stamp enters from — and retreats to —
        // 30% farther above the slot, so it traverses more distance in and out.
        float descendDistance = art * 0.585f;
        float liftDistance = descendDistance;

        // ---- Wooden stamp: fade-in + descend, a squash press, then lift + fade-out (NO tilt) ----
        const float DescendEnd = 0.24f;
        // Press window shortened 15% (0.115 → 0.09775; refinement 2026-08-16). The lift begins the instant the
        // press ends so the beats stay contiguous.
        const float PressLen = 0.115f * 0.85f;
        const float PressEnd = DescendEnd + PressLen;
        const float LiftStart = PressEnd;
        // Leaving-translation duration (was the shared "FadeSpan"; the fades are now HALF-windows tied to the
        // translations, so this only sizes the lift travel).
        const float LiftSpan = 0.155f;
        const float LiftEnd = LiftStart + LiftSpan;
        // Descend (0 → DescendEnd): drop from DescendDistance above to rest. easeInSine — accelerates INTO the
        // page (refinement 2026-08-16).
        float descend = EaseInSine(Seg(t, 0f, DescendEnd));
        // Fade-in over the FIRST HALF of the entering translation ([0, DescendEnd/2]) so the stamp is fully
        // opaque by the time it's halfway down; fade-out over the SECOND HALF of the leaving translation
        // ([LiftStart + LiftSpan/2, LiftEnd]) so it holds opaque until it's halfway back up, then vanishes as it
        // finishes retreating (refinement 2026-08-16).
        float fadeIn = Seg(t, 0f, DescendEnd * 0.5f);
        // Press: a sine bump 0→1→0 that peaks at the bottom of the travel — a slight squash as the stamp meets
        // the page, easing back out as it settles.
        float press = Sine01(Seg(t, DescendEnd, PressEnd));
        // Lift (LiftStart → LiftEnd): rise back up the full descend distance. easeInOutSine — eases out of the
        // press and back to a stop (refinement 2026-08-16).
        float lift = EaseInOutSine(Seg(t, LiftStart, LiftEnd));
        float fadeOut = Seg(t, LiftStart + LiftSpan * 0.5f, LiftEnd);

        // Persistent downward nudge: the whole stamp (at every phase) sits 1% of the pixel-art width lower on
        // the page (feedback 2026-08-16), so the imprint lands slightly further down.
        float offsetY = Lerp(-descendDistance, 0f, descend) + Lerp(0f, -liftDistance, lift) + art * 0.01f;
        // Squash magnitude reduced 40% (0.05/0.08 → 0.03/0.048; refinement 2026-08-16) and anchored to the
        // BOTTOM edge (see Alignment.BottomCenter below) so the stamp presses DOWN into the page rather than
        // squashing symmetrically about its centre.
        float squashX = 1f + 0.03f * press;
        float squashY = 1f - 0.048f * press;
        float stampOpacity = Clamp01(Math.Min(fadeIn, 1f - fadeOut));

        // ---- "COPIED" imprint: APPEARS INSTANTLY (no fade-in, no pop) the moment the stamp finishes its
        // entering translation — i.e. the descend completes at t = DescendEnd — as if pressed onto the page in
        // one contact. It then HOLDS and fades out at the very end (feedback 2026-08-16). No pop-scale either;
        // it just snaps to full opacity on contact. The fade-OUT window is unchanged from before. ----
        // Fade-OUT window halved (0.8536 → 0.9268; refinement 2026-08-17): the imprint fades out over HALF the
        // time it used to — same end point (t = 1), but twice as fast, so it lingers longer at full then snaps away.
        const float ImprintFadeStart = 0.9268f;
        const float ImprintEnd = 1f;
        float imprintOut = Seg(t, ImprintFadeStart, ImprintEnd);
        float imprintScale = 1f; // no pop — appears at rest size
        float imprintOpacity = t >= DescendEnd ? Clamp01(1f - imprintOut) : 0f;
        const float ImprintTilt = -0.06f; // subtle lean, ~ -3.4° (feedback 2026-08-16)
        const float ImprintSizeScale = 0.92f; // scales the imprint box + text; bumped 0.8 → 0.92 (15% larger, refinement 2026-08-17)

        var children = new System.Collections.Generic.List<Widget>();

        // "COPIED" imprint — a bordered, tilted block-text mark centred on the slot. Added to the Stack FIRST so
        // it paints UNDERNEATH the wooden stamp (feedback 2026-08-16): the imprint is the ink left ON THE PAGE,
        // so the descending/lifting stamp passes OVER it, not the other way round. (It only reaches full opacity
        // as the stamp lifts and fades, so the stamp rarely occludes it in practice.)
        if (imprintOpacity > 0.001f)
        {
            // The border HUGS the label: the Container carries no explicit Width/Height, so it shrink-wraps to the
            // Caudex text + padding (Center lays the child out with loosened constraints — see RenderPositionedBox),
            // and the outline sizes itself to whatever the text measures. This keeps the mark correctly framed when
            // the font metrics change (feedback 2026-08-16 — switched the label to Caudex, the mod's title face).
            var imprint = new Container(
                style: new BoxStyle
                {
                    Color = new Vector4(0f, 0f, 0f, 0f),         // transparent fill — just the ink outline
                    CornerRadius = new Vector4(3f),
                    BorderThickness = 2f,
                    BorderColor = Widget.ImprintColor,
                    Padding = Gui.Rendering.EdgeInsets.Symmetric(horizontal: 6f, vertical: 3f),
                    // OUTER GLOW in the parchment page colour (refinement 2026-08-17): a zero-offset, blurred,
                    // slightly-spread rounded-rect behind the mark. Because the box fill is transparent, this
                    // parchment blob shows THROUGH the interior too — backing the dark-red letters so they read
                    // over whatever content sits under the slot — and haloes outward past the border to lift the
                    // whole mark off the page. Blur/spread scale with the Pixel Art Size so it reads the same at
                    // any setting. Tunable (blur 0.03, spread 0.04, colour = theme Surface @ 0.6 alpha).
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

            // Centre the content-sized mark on the slot, but give it an ART-WIDE (not slot-wide) box to lay out
            // in — the same footprint the wooden stamp uses. A slot-wide (~48px) box capped the text's max width
            // and wrapped the bold "COPIED" after ~2 chars; `art` (~120px) leaves room for the whole word on one
            // line. The box is larger than the slot on purpose, centred on it, and spills over the sides
            // (unclipped, paint-only) exactly like the stamp above.
            children.Add(new Positioned(
                left: (slot - art) / 2f, top: (slot - art) / 2f, width: art, height: art,
                child: new Center(child: new Opacity(imprintOpacity,
                    new Transform(
                        child: imprint,
                        matrix: SKMatrix.CreateRotation(ImprintTilt).PreConcat(SKMatrix.CreateScale(imprintScale, imprintScale)),
                        alignment: Alignment.Center)))));
        }

        // ---- Landing shadow: a static, soft, dark rounded-rect "contact shadow" marking WHERE the stamp will
        // land (refinement 2026-08-17). It NEVER moves or scales — it only FADES IN over the stamp's entering
        // translation (the descend, [0, DescendEnd]) and FADES OUT over the leaving translation (the lift,
        // [LiftStart, LiftEnd]), holding at full through the press in between. Added to the Stack AFTER the
        // imprint but BEFORE the wooden stamp, so it paints ABOVE the "COPIED" ink and BELOW the descending
        // stamp. Sized to the stamp's flat base (≈ the "COPIED" box width) and centred on the slot. ----
        float shadowVisibility = Clamp01(Math.Min(Seg(t, 0f, DescendEnd), 1f - Seg(t, LiftStart, LiftEnd)));
        if (shadowVisibility > 0.001f)
        {
            const float ShadowPeakAlpha = 0.7f;      // darkness of the contact shadow at full press (tunable)
            float shadowW = art * 0.55f;             // ≈ the stamp's flat base / "COPIED" box width (tunable)
            float shadowH = art * 0.25f;             // short, like a shadow cast flat on the page (tunable)
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
                        BlurRadius: art * 0.1f),               // soft edge, scales with Pixel Art Size (tunable)
                },
            });
            children.Add(new Positioned(
                left: (slot - art) / 2f, top: (slot - art) / 2f, width: art, height: art,
                child: new Center(child: new Opacity(shadowVisibility, shadow))));
        }

        // Wooden stamp image, painted ON TOP of the imprint (skipped if the asset failed to load — the imprint
        // still plays).
        if (Widget.StampBitmap is { } bmp && stampOpacity > 0.001f)
        {
            float stampW = art;
            float stampH = stampW * bmp.Height / bmp.Width;
            // Squash (no tilt) anchored to the stamp's BOTTOM edge, so the base stays planted on the page and
            // only the top compresses downward — the impression of pressing INTO the paper (refinement
            // 2026-08-16). Translate is applied by the outer Transform.
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

        // Fixed footprint = the slot; every visual is a Positioned overlay so the flourish never resizes the
        // slot and can overflow (unclipped, paint-only) above it as the stamp descends.
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
    private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    /// <summary>Map <paramref name="t"/> from [lo,hi] onto 0..1, clamped (0 below lo, 1 above hi).</summary>
    private static float Seg(float t, float lo, float hi) => hi <= lo ? (t >= hi ? 1f : 0f) : Clamp01((t - lo) / (hi - lo));
    private static float EaseOutCubic(float x) { float u = 1f - x; return 1f - u * u * u; }
    /// <summary>Sine ease-in: slow start accelerating to the end (the entering/descend translation).</summary>
    private static float EaseInSine(float x) => 1f - (float)Math.Cos((x * Math.PI) / 2.0);
    /// <summary>Sine ease-in-out: eased at both ends (the leaving/lift translation).</summary>
    private static float EaseInOutSine(float x) => -((float)Math.Cos(Math.PI * x) - 1f) / 2f;
    /// <summary>A 0→1→0 bump over [0,1] (sine half-period) — used for the momentary press.</summary>
    private static float Sine01(float x) => (float)Math.Sin(x * Math.PI);
}
