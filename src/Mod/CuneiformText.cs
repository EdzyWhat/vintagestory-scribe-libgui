// Display-only cuneiform text widget (add-cuneiform-glyph-font, Proposal A). Renders a single line of
// text as filled rectangle STROKES (not a TTF typeface) from the glyph-forge geometry parsed by
// Scribe.Core.Cuneiform. It mirrors ScribeMultilineField's 3-class LibGUI pattern:
//   • CuneiformTextRender : RenderBox — runs the Core line layout, scales grid units → pixels by the
//     requested em size, sizes itself to the line, and FILLS each stroke's oriented quad on the raw
//     Skia canvas (context.Canvas + SKPath + SharedPaint). It deliberately does NOT use DrawBox:
//     strokes are arbitrary-angle rectangles and DrawBox only draws axis-aligned (rounded) boxes.
//   • CuneiformTextRenderWidget : RenderObjectWidget — the create/update bridge.
//   • CuneiformText : StatefulWidget — owns an OPTIONAL AnimationController for a stroke-by-stroke
//     reveal; for this first prototype it idles fully revealed (static display) so proving legibility
//     never waits on animation.
//
// This is the #1 de-risk of the tablet plan: raw-canvas polygon fill inside PaintInternal is new to
// this codebase (existing widgets only ever call DrawText/DrawBox). See
// openspec/changes/add-cuneiform-glyph-font/.

using System;
using Gui.Core.Framework;         // RenderObject, RenderBox
using Gui.Rendering;              // PaintingContext
using Gui.Widgets.Animations;     // AnimationController, AnimatedBuilder
using Gui.Widgets.Framework;      // Widget, StatefulWidget, State, RenderObjectWidget
using Gui.Widgets.Input;          // ITickerProvider (via Element.Owner)
using OpenTK.Mathematics;         // Vector2, Vector4
using Scribe.Core.Cuneiform;      // GlyphBundle, CuneiformLineLayout, CuneiformLine, GlyphStroke
using SkiaSharp;                  // SKPath, SKPoint

namespace Scribe;

/// <summary>
/// Shared cuneiform rendering metrics (add-tablet-cuneiform-chrome D7). Every cuneiform render object —
/// the display <see cref="CuneiformTextRender"/>, the editable row (<see cref="ScribeCuneiformFieldRender"/>),
/// and the title field — sizes a line to <c>fontSizeEm</c> pixels. But normal TTF <c>Text</c> at the same
/// nominal font size occupies a line-height of roughly <see cref="LineHeightRatio"/>× that font size, so a
/// cuneiform glyph authored to fill its em box reads visibly SHORTER than adjacent readable text (the
/// 2026-08-02 playtest measured the footer label ~30% short). Scaling the cuneiform em by this ratio at
/// render time brings the rendered cuneiform height in line with the readable text's rendered line-height —
/// applied globally here (not as a per-call fudge) so rows, title, and labels all match. Scale-independent
/// (a ratio, not a pixel target), so it holds at any GUI scale.
/// </summary>
internal static class CuneiformMetrics
{
    /// <summary>Cuneiform's em→pixel scale is multiplied by this so its rendered glyph height reads well
    /// against the surrounding readable text. ~1.4 matched the measured TTF line-height ratio (so cuneiform
    /// sat at the same height as adjacent text); the 2026-08-03 playtest then asked for the glyphs ~20%
    /// larger than that for legibility (1.4 × 1.2 = 1.68), and a follow-up pass asked for another bump — +20%
    /// read too big, so it settled at +10% on top (1.68 × 1.1 = 1.848). This is a deliberate oversize, not a
    /// height-match. Applied globally (rows, title, labels) so every surface scales together; tuned in-game.</summary>
    public const float LineHeightRatio = 1.848f;

    /// <summary>Default hand-written jitter strength (add-cuneiform-handwriting-feel) applied to cuneiform
    /// text until the client config knob (task 6) overrides it. Reads as a hand-pressed wobble without
    /// hurting legibility; 0 reproduces today's crisp geometry exactly. Tuned in-game across the 2026-08-03
    /// playtest passes (0.5 → 0.8 too strong → settled at 0.6).</summary>
    public const float DefaultJitterStrength = 0.6f;

    /// <summary>Default whole-character rotation, in degrees, applied to cuneiform text
    /// (tune-tablet-jitter-add-rotation). Each glyph tilts by a deterministic random angle in
    /// [-max, +max] about its box center, so the text reads as hand-pressed rather than mechanically
    /// upright. Stacks with (and is applied after) the per-endpoint jitter; 0 keeps every glyph upright.
    /// Prototyped in the glyph-forge tool at ±8°. Tuned in-game.</summary>
    public const float DefaultRotationDegrees = (float)GlyphStrokeRotation.DefaultMaxDegrees;

    /// <summary>Derives a stable base jitter seed from a string (e.g. a label's text), so the same text
    /// always wobbles the same way and different texts differ. Order-independent of frame/wall-clock — a
    /// plain deterministic string hash. Used for static display text; editable fields use a fixed per-field
    /// seed instead so typing a character does not reseed the letters already on screen.</summary>
    public static int SeedFromString(string? s)
    {
        // Deterministic FNV-1a over UTF-16 code units (String.GetHashCode is randomized per process, so it
        // must NOT be used for a seed that has to be stable across sessions).
        unchecked
        {
            uint h = 2166136261u;
            if (s is not null)
            {
                foreach (char c in s)
                {
                    h = (h ^ c) * 16777619u;
                }
            }
            return (int)h;
        }
    }
}

/// <summary>
/// The render object: lays a string out through the Core cuneiform engine and paints its strokes as
/// filled quads, auto-sizing to the laid-out line. The line height in pixels equals
/// <see cref="FontSizeEm"/> × <see cref="CuneiformMetrics.LineHeightRatio"/> (grid units scale to pixels by
/// <c>renderedHeight / lineHeightGridUnits</c>) so cuneiform matches adjacent readable text (D7).
/// </summary>
internal sealed class CuneiformTextRender : Gui.Core.Framework.RenderBox
{
    private string text = "";
    private float fontSizeEm = 32f;
    private Vector4 inkColor = Vector4.One;
    private GlyphBundle? bundle;
    // Fraction of the line's strokes to reveal, in authored construction order (1 = whole line). Idle
    // at 1 for the static prototype; the animation drives it 0→1 when active.
    private float revealFraction = 1f;
    // Per-stroke hand-written jitter (add-cuneiform-handwriting-feel). 0 = crisp/authored geometry; the
    // seed anchors the (deterministic) wobble so the SAME text jitters identically each frame/open.
    private float jitterStrength;
    private int jitterSeed;
    // Whole-character rotation (tune-tablet-jitter-add-rotation). 0 = upright; each glyph tilts by a
    // deterministic angle in [-max, +max]° about its box center, applied AFTER jitter, paint-time only.
    private float rotationDegrees;
    // Per-stroke outer glow (add-tablet-clay-type-themes). Default disabled — non-tablet surfaces pay nothing.
    private CuneiformGlow glow;
    // Per-view stroke-weight scale (adopt-glyph-forge-tablet-themes). 1 = the Core-authored weight exactly;
    // the tablet firms strokes up (fired) or thins them (wet). Paint-time only — never touches layout metrics.
    private float strokeWeightScale = 1f;

    // Cached from the last PerformLayout, reused by PaintInternal so layout and paint agree.
    private CuneiformLine? line;
    private float scale = 1f;

    /// <summary>The text to render (uppercase-folded by the layout; unauthored chars advance a small
    /// gap). Relayouts on change (the line width/height depend on it).</summary>
    public string Text { get => text; set => SetProperty(ref text, value ?? "", relayout: true); }

    /// <summary>Pixel height of one em (one glyph grid). The whole line scales from this. Relayouts.</summary>
    public float FontSizeEm { get => fontSizeEm; set => SetProperty(ref fontSizeEm, value, relayout: true); }

    /// <summary>Ink color the stroke quads fill with (from the active theme). Repaint only.</summary>
    public Vector4 InkColor { get => inkColor; set => SetProperty(ref inkColor, value, repaint: true); }

    /// <summary>The parsed glyph geometry. Relayouts on change (glyphs determine the line's extent).
    /// Null until the asset is loaded — the widget then renders nothing.</summary>
    public GlyphBundle? Bundle { get => bundle; set => SetProperty(ref bundle, value, relayout: true); }

    /// <summary>Fraction (0..1) of the line's strokes to paint, in construction order. Repaint only —
    /// the geometry is unchanged, only how many strokes are drawn.</summary>
    public float RevealFraction
    {
        get => revealFraction;
        set => SetProperty(ref revealFraction, Math.Clamp(value, 0f, 1f), repaint: true);
    }

    /// <summary>Hand-written jitter strength (0..1; 0 = crisp authored geometry). Repaint only — jitter is a
    /// visual transform applied at paint time to the drawn quads, never to the layout metrics.</summary>
    public float JitterStrength
    {
        get => jitterStrength;
        set => SetProperty(ref jitterStrength, Math.Clamp(value, 0f, 1f), repaint: true);
    }

    /// <summary>Base seed the per-stroke jitter is derived from (combined with each stroke's stable identity).
    /// The Mod layer picks it (per field/text) so the same text wobbles the same way. Repaint only.</summary>
    public int JitterSeed { get => jitterSeed; set => SetProperty(ref jitterSeed, value, repaint: true); }

    /// <summary>Whole-character rotation in degrees (>= 0; 0 = upright). Repaint only — like jitter it is a
    /// visual transform applied at paint time to the drawn quads, never to the layout metrics.</summary>
    public float RotationDegrees
    {
        get => rotationDegrees;
        set => SetProperty(ref rotationDegrees, Math.Max(0f, value), repaint: true);
    }

    /// <summary>The per-stroke outer glow (halo color/strength + blur fraction). Disabled by default; the
    /// tablet sets a per-material glow to lift the ink off its clay backdrop. Repaint only — the glow is a
    /// paint-time effect that never touches layout.</summary>
    public CuneiformGlow Glow { get => glow; set => SetProperty(ref glow, value, repaint: true); }

    /// <summary>Per-view stroke-weight scale (adopt-glyph-forge-tablet-themes). Multiplies each stroke's
    /// <c>GlyphStroke.Weight</c> at paint time so the tablet can firm the ink up or thin it per drying state.
    /// A non-positive value is treated as 1 (the struct-default <see cref="ScribeRowStyle.CuneiformStrokeWeightScale"/>
    /// of 0 means "use the base weight"). Repaint only — it changes only the painted thickness, never the
    /// advance metrics the layout emits.</summary>
    public float StrokeWeightScale
    {
        get => strokeWeightScale;
        set => SetProperty(ref strokeWeightScale, value <= 0f ? 1f : value, repaint: true);
    }

    protected override void PerformLayout()
    {
        // Match the rendered cuneiform height to adjacent readable text's line-height, not its raw em (D7).
        float renderedHeight = fontSizeEm * CuneiformMetrics.LineHeightRatio;

        if (bundle is null)
        {
            line = null;
            Size = Constraints.Constrain(new Vector2(0f, renderedHeight));
            return;
        }

        var layout = new CuneiformLineLayout(bundle);
        line = layout.Layout(text);

        // Grid units → pixels: one line-height of grid units maps to the rendered (ratio-boosted) height.
        double gridHeight = line.LineHeight > 0 ? line.LineHeight : CuneiformLineLayout.DefaultGridSize;
        scale = (float)(renderedHeight / gridHeight);

        float width = (float)(line.TotalWidth * scale);
        Size = Constraints.Constrain(new Vector2(width, renderedHeight));
    }

    protected override void PaintInternal(PaintingContext context)
    {
        // Paint the base box first (row background/frame, if any theme colors are set), then the strokes.
        base.PaintInternal(context);
        if (context.Canvas is null || line is null || line.Strokes.Count == 0)
        {
            return;
        }

        int total = line.Strokes.Count;
        int revealCount = (int)MathF.Round(revealFraction * total);
        revealCount = Math.Clamp(revealCount, 0, total);
        if (revealCount == 0)
        {
            return;
        }

        SKPaint paint = context.SharedPaint;
        SKPaintStyle prevStyle = paint.Style;
        bool prevAntialias = paint.IsAntialias;   // pre-existing leak: restore this too (task 3.4).

        paint.Style = SKPaintStyle.Fill;
        paint.IsAntialias = true;

        // A reused path, cleared per stroke — allocation-light across the whole line.
        using var path = new SKPath();

        // Pass 1 (optional): every revealed stroke's HALO, blurred, in the glow color. Drawing all halos
        // BEFORE any crisp ink means the crisp fills in pass 2 overwrite the halos inside each glyph, so
        // overlapping strokes never darken/double each other's glow — the halo only shows where it spills
        // past the ink onto the backdrop (design D3). The mask filter is a cached, shared blur (D4).
        if (glow.Enabled && CuneiformGlowMask.ForSigma(fontSizeEm * glow.BlurFraction) is { } mask)
        {
            paint.Color = glow.Color.ToSkColor();
            paint.MaskFilter = mask;
            // Optional directional offset (a fraction of the em): translate ONLY the blurred halo pass so the
            // glow reads as a seated drop rather than a symmetric aura (design D3). The crisp ink pass below
            // draws un-offset, so the ink registers over the shifted halo. A zero offset is a centered halo.
            float glowOffX = fontSizeEm * glow.OffsetXFraction;
            float glowOffY = fontSizeEm * glow.OffsetYFraction;
            bool offsetHalo = glowOffX != 0f || glowOffY != 0f;
            if (offsetHalo)
            {
                context.Canvas.Save();
                context.Canvas.Translate(glowOffX, glowOffY);
            }
            for (int i = 0; i < revealCount; i++)
            {
                BuildStrokePath(path, line.Strokes[i]);
                context.Canvas.DrawPath(path, paint);
            }
            if (offsetHalo)
            {
                context.Canvas.Restore();
            }
            paint.MaskFilter = null;   // clear before the crisp pass (and before return) — SharedPaint hygiene.
        }

        // Pass 2: every revealed stroke's crisp ink fill, on top of the halos.
        paint.Color = inkColor.ToSkColor();
        for (int i = 0; i < revealCount; i++)
        {
            BuildStrokePath(path, line.Strokes[i]);
            context.Canvas.DrawPath(path, paint);
        }

        // Restore the shared paint's mutated properties (SharedPaint is reused across draw ops). Leave Color
        // OPAQUE rather than restored to the inherited value: base.PaintInternal drew this row's resting box
        // at boxColor alpha 0 just before we captured it, so restoring would leave the shared paint at alpha
        // 0 for the next op — which rendered the read-only tablet's clay backdrop transparent, since
        // DrawMaskedBox reuses SharedPaint.Color unset and DrawBitmap modulates by its alpha. Opaque white is
        // the neutral every sibling draw sets before painting. (tablet-firing / see ScribeCuneiformField.)
        paint.Color = SKColors.White;
        paint.Style = prevStyle;
        paint.IsAntialias = prevAntialias;
        paint.MaskFilter = null;   // defensive: never leave a blur mask on the shared paint.
    }

    /// <summary>Rebuild <paramref name="path"/> (cleared in place) as one stroke's oriented quad in pixel
    /// space, applying the same hand-written jitter (then whole-character rotation) both glow passes use so
    /// the halo tracks the drawn ink. Seeded off the stroke's stable identity, so the SAME stroke transforms
    /// identically every frame. Order: jitter → rotate → corners.</summary>
    private void BuildStrokePath(SKPath path, PositionedStroke ps)
    {
        GlyphStroke stroke = jitterStrength > 0f
            ? GlyphStrokeJitter.Jitter(
                ps.Stroke,
                GlyphStrokeJitter.SeedFor(jitterSeed, ps.SourceCharIndex, ps.StrokeOrdinal),
                jitterStrength,
                ps.GridSize)
            : ps.Stroke;
        if (rotationDegrees > 0f && line is not null)
        {
            stroke = GlyphStrokeRotation.Rotate(
                stroke,
                GlyphRotationPivot(line, ps),
                GlyphStrokeRotation.SeedFor(jitterSeed, ps.SourceCharIndex),
                rotationDegrees);
        }
        // Per-view weight scale (adopt-glyph-forge-tablet-themes): rebuild the stroke with a scaled weight
        // before it becomes a quad. Applied AFTER jitter/rotation so it composes with them, only when it
        // differs from 1. Corners() derives the quad half-width from Weight, so this changes only the painted
        // thickness — the advance metrics the layout emits are the un-scaled geometry.
        if (strokeWeightScale != 1f)
        {
            stroke = new GlyphStroke(stroke.Start, stroke.End, stroke.Weight * strokeWeightScale);
        }
        Scribe.Core.Cuneiform.Vec2[] corners = stroke.Corners();

        path.Reset();
        path.MoveTo((float)(corners[0].X * scale), (float)(corners[0].Y * scale));
        path.LineTo((float)(corners[1].X * scale), (float)(corners[1].Y * scale));
        path.LineTo((float)(corners[2].X * scale), (float)(corners[2].Y * scale));
        path.LineTo((float)(corners[3].X * scale), (float)(corners[3].Y * scale));
        path.Close();
    }

    /// <summary>The glyph box center to rotate a character's strokes about, in grid-unit space (the same
    /// space the strokes live in, before the pixel scale is applied). X is the midpoint of the source
    /// character's advance span from the un-jittered layout (<see cref="CuneiformLine.CharBoundaries"/>);
    /// Y is half the glyph grid. Reading the pivot from the layout — never from a transformed stroke — keeps
    /// rotation a pure paint-time transform that never feeds back into metrics/caret/hit-testing.</summary>
    private static Scribe.Core.Cuneiform.Vec2 GlyphRotationPivot(CuneiformLine line, PositionedStroke ps)
    {
        var b = line.CharBoundaries;
        int i = ps.SourceCharIndex;
        // Guard the boundary range: CharBoundaries has (charCount + 1) entries, so the char at index i has
        // its span in [i, i+1]. Fall back to the stroke's own start x if the index is somehow out of range.
        double left = i >= 0 && i < b.Count ? b[i] : ps.Stroke.Start.X;
        double right = i + 1 >= 0 && i + 1 < b.Count ? b[i + 1] : left;
        return new Scribe.Core.Cuneiform.Vec2((left + right) / 2.0, ps.GridSize / 2.0);
    }
}

/// <summary>RenderObjectWidget bridge: forwards text, em size, ink color, bundle, and reveal fraction.</summary>
internal sealed class CuneiformTextRenderWidget : RenderObjectWidget
{
    public CuneiformTextRenderWidget(
        string text, float fontSizeEm, Vector4 inkColor, GlyphBundle? bundle, float revealFraction,
        float jitterStrength = 0f, int jitterSeed = 0, float rotationDegrees = 0f, CuneiformGlow glow = default,
        float strokeWeightScale = 1f)
    {
        Text = text;
        FontSizeEm = fontSizeEm;
        InkColor = inkColor;
        Bundle = bundle;
        RevealFraction = revealFraction;
        JitterStrength = jitterStrength;
        JitterSeed = jitterSeed;
        RotationDegrees = rotationDegrees;
        Glow = glow;
        StrokeWeightScale = strokeWeightScale;
    }

    public string Text { get; }
    public float FontSizeEm { get; }
    public Vector4 InkColor { get; }
    public GlyphBundle? Bundle { get; }
    public float RevealFraction { get; }
    public float JitterStrength { get; }
    public int JitterSeed { get; }
    public float RotationDegrees { get; }
    public CuneiformGlow Glow { get; }
    public float StrokeWeightScale { get; }

    public override RenderObject CreateRenderObject() => new CuneiformTextRender();

    public override void UpdateRenderObject(RenderObject renderObject)
    {
        var ro = (CuneiformTextRender)renderObject;
        ro.Text = Text;
        ro.FontSizeEm = FontSizeEm;
        ro.InkColor = InkColor;
        ro.Bundle = Bundle;
        ro.RevealFraction = RevealFraction;
        ro.JitterStrength = JitterStrength;
        ro.JitterSeed = JitterSeed;
        ro.RotationDegrees = RotationDegrees;
        ro.Glow = Glow;
        ro.StrokeWeightScale = StrokeWeightScale;
    }
}

/// <summary>
/// A display-only cuneiform text line. Optionally reveals its strokes one-by-one in authored
/// construction order via an owned <see cref="AnimationController"/>; when <see cref="AnimateReveal"/>
/// is false (the default for this prototype) it renders the whole line statically. Owns and disposes
/// its controller per the LibGUI lifecycle (created in <see cref="State.InitState"/>, disposed in
/// <see cref="State.Dispose"/>) — see <c>docs/libgui-reference.md</c>.
/// </summary>
public sealed class CuneiformText : StatefulWidget
{
    public CuneiformText(
        string text,
        float fontSizeEm = 32f,
        Vector4? inkColor = null,
        GlyphBundle? bundle = null,
        bool animateReveal = false,
        int revealDurationMs = 1200,
        float? jitterStrength = null,
        float? rotationDegrees = null,
        CuneiformGlow glow = default,
        float strokeWeightScale = 1f,
        Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        Text = text ?? "";
        FontSizeEm = fontSizeEm;
        InkColor = inkColor ?? Vector4.One;
        Bundle = bundle;
        AnimateReveal = animateReveal;
        RevealDurationMs = revealDurationMs;
        JitterStrength = jitterStrength ?? CuneiformMetrics.DefaultJitterStrength;
        RotationDegrees = rotationDegrees ?? CuneiformMetrics.DefaultRotationDegrees;
        Glow = glow;
        StrokeWeightScale = strokeWeightScale;
    }

    /// <summary>The line of text to render.</summary>
    public string Text { get; }

    /// <summary>Pixel height of one em (one glyph grid).</summary>
    public float FontSizeEm { get; }

    /// <summary>Ink color the strokes fill with (usually the theme's on-surface color).</summary>
    public Vector4 InkColor { get; }

    /// <summary>The parsed glyph geometry; null renders nothing (asset not yet loaded).</summary>
    public GlyphBundle? Bundle { get; }

    /// <summary>Whether to play the stroke-by-stroke reveal on mount. Default false = static full line.</summary>
    public bool AnimateReveal { get; }

    /// <summary>Reveal duration in ms when <see cref="AnimateReveal"/> is on.</summary>
    public int RevealDurationMs { get; }

    /// <summary>Hand-written jitter strength (0..1). Defaults to <see cref="CuneiformMetrics.DefaultJitterStrength"/>;
    /// pass 0 for crisp authored geometry. The base seed is derived from <see cref="Text"/> so the same text
    /// wobbles consistently.</summary>
    public float JitterStrength { get; }

    /// <summary>Whole-character rotation in degrees (tune-tablet-jitter-add-rotation). Defaults to
    /// <see cref="CuneiformMetrics.DefaultRotationDegrees"/>; pass 0 to keep every glyph upright. Applied
    /// after jitter, at paint time only.</summary>
    public float RotationDegrees { get; }

    /// <summary>The per-stroke outer glow. Disabled by default (non-tablet display text pays nothing); the
    /// tablet passes a per-material glow so its cuneiform title/label lifts off the clay backdrop.</summary>
    public CuneiformGlow Glow { get; }

    /// <summary>Per-view cuneiform stroke-weight scale (adopt-glyph-forge-tablet-themes). Defaults to 1 =
    /// the Core-authored weight exactly; the tablet passes its per-state scale so display text firms up or
    /// thins with the drying state. Non-tablet display text leaves it at 1 and is pixel-identical.</summary>
    public float StrokeWeightScale { get; }

    public override State CreateState() => new CuneiformTextState();
}

internal sealed class CuneiformTextState : State<CuneiformText>
{
    private AnimationController? controller;

    public override void InitState()
    {
        base.InitState();
        if (Widget.AnimateReveal)
        {
            // ITickerProvider comes from the owner, exactly like ScribeRowSizeAnimation's controller.
            controller = new AnimationController(
                TimeSpan.FromMilliseconds(Widget.RevealDurationMs), Element.Owner!.GetTickerProvider());
            controller.OnValueChanged += OnValueChanged;
            controller.Forward();
        }
    }

    private void OnValueChanged(double _) => Element.MarkNeedsBuild();

    public override void Dispose()
    {
        if (controller is not null)
        {
            controller.OnValueChanged -= OnValueChanged;
            controller.Dispose();
            controller = null;
        }
        base.Dispose();
    }

    public override Widget Build(BuildContext context)
    {
        // No animation: render the whole line statically (reveal fraction 1).
        if (controller is null)
        {
            return BuildLine(1f);
        }

        // Animated: rebuild each tick, mapping the controller's 0→1 value to the reveal fraction.
        return new AnimatedBuilder(controller, _ => BuildLine((float)controller.Value));
    }

    private Widget BuildLine(float revealFraction) => new CuneiformTextRenderWidget(
        text: Widget.Text,
        fontSizeEm: Widget.FontSizeEm,
        inkColor: Widget.InkColor,
        bundle: Widget.Bundle,
        revealFraction: revealFraction,
        jitterStrength: Widget.JitterStrength,
        // Static display text seeds off its own content, so the same label always wobbles identically.
        jitterSeed: CuneiformMetrics.SeedFromString(Widget.Text),
        rotationDegrees: Widget.RotationDegrees,
        glow: Widget.Glow,
        strokeWeightScale: Widget.StrokeWeightScale);
}
