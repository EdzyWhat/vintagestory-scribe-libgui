// Display-only cuneiform text widget: renders one line as filled rectangle STROKES (not a TTF typeface) from
// the glyph-forge geometry parsed by Scribe.Core.Cuneiform. Three classes, mirroring ScribeMultilineField:
//   - CuneiformTextRender : RenderBox - lays out the line, scales grid units to pixels, and fills each stroke's
//     oriented quad on the raw Skia canvas (not DrawBox, which only draws axis-aligned boxes).
//   - CuneiformTextRenderWidget : RenderObjectWidget - create/update bridge.
//   - CuneiformText : StatefulWidget - owns an optional AnimationController for a stroke-by-stroke reveal.

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
/// Shared cuneiform rendering metrics. Cuneiform render objects size a line to <c>fontSizeEm</c> pixels, but TTF
/// <c>Text</c> at that size occupies roughly <see cref="LineHeightRatio"/>x the line height, so unscaled cuneiform
/// reads shorter than adjacent text. Scaling the em by the ratio globally keeps rows, titles and labels matched at
/// any GUI scale.
/// </summary>
internal static class CuneiformMetrics
{
    /// <summary>Multiplier on cuneiform's em-to-pixel scale. 1.4 x 1.2 x 1.1 = 1.848: a deliberate oversize for
    /// legibility rather than an exact TTF line-height match.</summary>
    public const float LineHeightRatio = 1.848f;

    /// <summary>Multiplier on the em height before the <see cref="LineHeightRatio"/> formula, for the tablet's
    /// cuneiform title/row/label text. Shrinks the render widgets' reserved <c>Size</c> and drawn ink, but not
    /// <see cref="GuiDialogScribeTablet"/>'s <c>CheckboxSize</c>/<c>ControlSize</c>, so the cuneiform band sits shorter
    /// than the control column by design. The checkbox/icon/counter alignment nudges depend on this value; 1.0 (the
    /// property default) applies no scaling.</summary>
    public const float GlyphDrawScale = 0.95f;

    /// <summary>Drawn height of <see cref="ScribeCuneiformFieldRender"/>'s caret bar relative to its line-height
    /// band. Paint-time only: it shrinks toward the band's vertical center without touching layout, hit-testing
    /// or caret X. 1.0 is the full band.</summary>
    public const float CaretHeightScale = 44f / 48f;

    /// <summary>Multiplier on the tablet cuneiform field's top/bottom padding (<c>ScribeRowStyle.FieldPadY</c>),
    /// applied only to real rows (title bands already pad 0). 1.0 leaves it unscaled.</summary>
    public const float FieldPadYScale = 6f / 9f;

    /// <summary>Default hand-written jitter strength for cuneiform text; 0 gives crisp authored geometry.</summary>
    public const float DefaultJitterStrength = 0.6f;

    /// <summary>Default whole-character rotation bound in degrees. Each glyph tilts by a deterministic angle in
    /// [-max, +max] about its box center, after the per-endpoint jitter; 0 keeps glyphs upright.</summary>
    public const float DefaultRotationDegrees = (float)GlyphStrokeRotation.DefaultMaxDegrees;

    /// <summary>Stable base jitter seed from a string, so the same text always wobbles the same way. Used for
    /// static display text; editable fields use a fixed per-field seed so typing does not reseed existing letters.</summary>
    public static int SeedFromString(string? s)
    {
        // FNV-1a over UTF-16 code units; String.GetHashCode is randomized per process so it cannot seed
        // anything that must be stable across sessions.
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
/// Render object: lays a string out through the Core cuneiform engine and paints its strokes as filled quads,
/// auto-sizing to the line. Line height in pixels is <see cref="FontSizeEm"/> x
/// <see cref="CuneiformMetrics.LineHeightRatio"/>.
/// </summary>
internal sealed class CuneiformTextRender : Gui.Core.Framework.RenderBox
{
    private string text = "";
    private float fontSizeEm = 32f;
    private Vector4 inkColor = Vector4.One;
    private GlyphBundle? bundle;
    // Fraction of the line's strokes to reveal, in authored construction order (1 = whole line).
    private float revealFraction = 1f;
    // Per-stroke hand-written jitter; the seed anchors the deterministic wobble.
    private float jitterStrength;
    private int jitterSeed;
    // Whole-character rotation: deterministic angle in [-max, +max] degrees, after jitter, paint-time only.
    private float rotationDegrees;
    // Per-stroke outer glow; disabled by default.
    private CuneiformGlow glow;
    // Stroke-weight scale (1 = Core-authored weight). Paint-time only.
    private float strokeWeightScale = 1f;
    // Multiplier on the em height used by PerformLayout; shrinks both reserved Size and drawn ink.
    private float glyphDrawScale = 1f;

    // Cached from the last PerformLayout so paint and layout agree.
    private CuneiformLine? line;
    private float scale = 1f;

    /// <summary>The text to render (uppercase-folded by the layout; unauthored chars advance a small gap).</summary>
    public string Text { get => text; set => SetProperty(ref text, value ?? "", relayout: true); }

    /// <summary>Pixel height of one em (one glyph grid).</summary>
    public float FontSizeEm { get => fontSizeEm; set => SetProperty(ref fontSizeEm, value, relayout: true); }

    /// <summary>Ink color of the stroke quads.</summary>
    public Vector4 InkColor { get => inkColor; set => SetProperty(ref inkColor, value, repaint: true); }

    /// <summary>The parsed glyph geometry; null (asset not loaded) renders nothing.</summary>
    public GlyphBundle? Bundle { get => bundle; set => SetProperty(ref bundle, value, relayout: true); }

    /// <summary>Fraction (0..1) of strokes painted, in construction order.</summary>
    public float RevealFraction
    {
        get => revealFraction;
        set => SetProperty(ref revealFraction, Math.Clamp(value, 0f, 1f), repaint: true);
    }

    /// <summary>Jitter strength (0..1; 0 = crisp). Paint-time only; never affects layout.</summary>
    public float JitterStrength
    {
        get => jitterStrength;
        set => SetProperty(ref jitterStrength, Math.Clamp(value, 0f, 1f), repaint: true);
    }

    /// <summary>Base seed for per-stroke jitter, combined with each stroke's stable identity.</summary>
    public int JitterSeed { get => jitterSeed; set => SetProperty(ref jitterSeed, value, repaint: true); }

    /// <summary>Whole-character rotation in degrees (0 = upright). Paint-time only; never affects layout.</summary>
    public float RotationDegrees
    {
        get => rotationDegrees;
        set => SetProperty(ref rotationDegrees, Math.Max(0f, value), repaint: true);
    }

    /// <summary>Per-stroke outer glow; the tablet sets a per-material glow to lift ink off its clay backdrop.</summary>
    public CuneiformGlow Glow { get => glow; set => SetProperty(ref glow, value, repaint: true); }

    /// <summary>Multiplies each stroke's <c>GlyphStroke.Weight</c> at paint time. A non-positive value means 1
    /// (<see cref="ScribeRowStyle.CuneiformStrokeWeightScale"/> defaults to 0 for "use base weight"). Never changes
    /// advance metrics.</summary>
    public float StrokeWeightScale
    {
        get => strokeWeightScale;
        set => SetProperty(ref strokeWeightScale, value <= 0f ? 1f : value, repaint: true);
    }

    /// <summary>Multiplier on the em height fed into the line-height formula; non-positive means 1. Shrinks the
    /// reserved <c>Size</c> and, since paint reuses the same <c>scale</c>, the drawn ink too.</summary>
    public float GlyphDrawScale
    {
        get => glyphDrawScale;
        set => SetProperty(ref glyphDrawScale, value <= 0f ? 1f : value, relayout: true);
    }

    protected override void PerformLayout()
    {
        // Rendered height follows readable text's line-height (not the raw em), then glyphDrawScale on top.
        float renderedHeight = fontSizeEm * CuneiformMetrics.LineHeightRatio * glyphDrawScale;

        if (bundle is null)
        {
            line = null;
            Size = Constraints.Constrain(new Vector2(0f, renderedHeight));
            return;
        }

        var layout = new CuneiformLineLayout(bundle);
        line = layout.Layout(text);

        // Grid units to pixels: one line-height of grid units maps to the rendered height.
        double gridHeight = line.LineHeight > 0 ? line.LineHeight : CuneiformLineLayout.DefaultGridSize;
        scale = (float)(renderedHeight / gridHeight);

        float width = (float)(line.TotalWidth * scale);
        Size = Constraints.Constrain(new Vector2(width, renderedHeight));
    }

    protected override void PaintInternal(PaintingContext context)
    {
        // Base box first (row background/frame), then the strokes.
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
        bool prevAntialias = paint.IsAntialias;

        paint.Style = SKPaintStyle.Fill;
        paint.IsAntialias = true;

        // One path reused (cleared) per stroke.
        using var path = new SKPath();

        // Pass 1 (optional): blurred halos of every revealed stroke, drawn before any crisp ink so overlapping
        // strokes never double each other's glow and the halo only shows past the ink.
        if (glow.Enabled && CuneiformGlowMask.ForSigma(fontSizeEm * glow.BlurFraction) is { } mask)
        {
            paint.Color = glow.Color.ToSkColor();
            paint.MaskFilter = mask;
            // Optional offset (fraction of the em) applies to the halo pass only, so the glow reads as a drop
            // shadow; the crisp ink is drawn un-offset.
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
            paint.MaskFilter = null;
        }

        // Pass 2: crisp ink fills on top of the halos.
        paint.Color = inkColor.ToSkColor();
        for (int i = 0; i < revealCount; i++)
        {
            BuildStrokePath(path, line.Strokes[i]);
            context.Canvas.DrawPath(path, paint);
        }

        // Restore the shared paint. Color is left OPAQUE white, not the inherited value: base.PaintInternal
        // draws the resting box at alpha 0, and DrawMaskedBox/DrawBitmap reuse SharedPaint's color/alpha, so
        // an alpha-0 leftover would make the next op transparent.
        paint.Color = SKColors.White;
        paint.Style = prevStyle;
        paint.IsAntialias = prevAntialias;
        paint.MaskFilter = null;
    }

    /// <summary>Rebuilds <paramref name="path"/> as one stroke's oriented quad in pixels: jitter, then rotation,
    /// then corners. Both glow passes use it so the halo tracks the ink; seeded off the stroke's stable identity.</summary>
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
        // Weight scale applies after jitter/rotation; Corners() derives the half-width from Weight, so only
        // painted thickness changes, not advance metrics.
        if (strokeWeightScale != 1f)
        {
            stroke = new GlyphStroke(stroke.Start, stroke.End, stroke.Weight * strokeWeightScale);
        }
        Scribe.Core.Cuneiform.Vec2[] corners = stroke.Corners();

        // GlyphDrawScale lives entirely in PerformLayout's scale and is not re-applied here, so layout, paint
        // and the editable field's caret/hit-testing stay in lockstep.
        path.Reset();
        path.MoveTo((float)(corners[0].X * scale), (float)(corners[0].Y * scale));
        path.LineTo((float)(corners[1].X * scale), (float)(corners[1].Y * scale));
        path.LineTo((float)(corners[2].X * scale), (float)(corners[2].Y * scale));
        path.LineTo((float)(corners[3].X * scale), (float)(corners[3].Y * scale));
        path.Close();
    }

    /// <summary>Center of a character's glyph box in grid-unit space (before pixel scale): X is the midpoint of its
    /// advance span from the un-jittered layout, Y half the grid. Read from the layout, never a transformed stroke,
    /// so rotation never feeds back into metrics/caret/hit-testing.</summary>
    private static Scribe.Core.Cuneiform.Vec2 GlyphRotationPivot(CuneiformLine line, PositionedStroke ps)
    {
        var b = line.CharBoundaries;
        int i = ps.SourceCharIndex;
        // CharBoundaries has charCount + 1 entries; fall back to the stroke's start x if out of range.
        double left = i >= 0 && i < b.Count ? b[i] : ps.Stroke.Start.X;
        double right = i + 1 >= 0 && i + 1 < b.Count ? b[i + 1] : left;
        return new Scribe.Core.Cuneiform.Vec2((left + right) / 2.0, ps.GridSize / 2.0);
    }
}

/// <summary>RenderObjectWidget bridge forwarding the cuneiform render properties.</summary>
internal sealed class CuneiformTextRenderWidget : RenderObjectWidget
{
    public CuneiformTextRenderWidget(
        string text, float fontSizeEm, Vector4 inkColor, GlyphBundle? bundle, float revealFraction,
        float jitterStrength = 0f, int jitterSeed = 0, float rotationDegrees = 0f, CuneiformGlow glow = default,
        float strokeWeightScale = 1f, float glyphDrawScale = 1f)
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
        GlyphDrawScale = glyphDrawScale;
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
    public float GlyphDrawScale { get; }

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
        ro.GlyphDrawScale = GlyphDrawScale;
    }
}

/// <summary>
/// A display-only cuneiform line. With <see cref="AnimateReveal"/> it reveals strokes in construction order via an
/// owned <see cref="AnimationController"/> (created in InitState, disposed in Dispose); otherwise static.
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
        float glyphDrawScale = 1f,
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
        GlyphDrawScale = glyphDrawScale;
    }

    /// <summary>The line of text to render.</summary>
    public string Text { get; }

    /// <summary>Pixel height of one em.</summary>
    public float FontSizeEm { get; }

    /// <summary>Ink color of the strokes.</summary>
    public Vector4 InkColor { get; }

    /// <summary>The parsed glyph geometry; null renders nothing.</summary>
    public GlyphBundle? Bundle { get; }

    /// <summary>Play the stroke-by-stroke reveal on mount; default false = static.</summary>
    public bool AnimateReveal { get; }

    /// <summary>Reveal duration in ms.</summary>
    public int RevealDurationMs { get; }

    /// <summary>Jitter strength (0..1); 0 is crisp. The seed derives from <see cref="Text"/>.</summary>
    public float JitterStrength { get; }

    /// <summary>Whole-character rotation in degrees; 0 keeps glyphs upright.</summary>
    public float RotationDegrees { get; }

    /// <summary>Per-stroke outer glow; disabled by default.</summary>
    public CuneiformGlow Glow { get; }

    /// <summary>Stroke-weight scale; 1 = Core-authored weight.</summary>
    public float StrokeWeightScale { get; }

    /// <summary>Multiplier on the em height for layout and drawn geometry; 1 = no scaling.</summary>
    public float GlyphDrawScale { get; }

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
            // Ticker provider comes from the owner.
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
        // Static: whole line.
        if (controller is null)
        {
            return BuildLine(1f);
        }

        // Animated: map the controller's 0..1 value to the reveal fraction each tick.
        return new AnimatedBuilder(controller, _ => BuildLine((float)controller.Value));
    }

    private Widget BuildLine(float revealFraction) => new CuneiformTextRenderWidget(
        text: Widget.Text,
        fontSizeEm: Widget.FontSizeEm,
        inkColor: Widget.InkColor,
        bundle: Widget.Bundle,
        revealFraction: revealFraction,
        jitterStrength: Widget.JitterStrength,
        // Seeded off the text so a label always wobbles identically.
        jitterSeed: CuneiformMetrics.SeedFromString(Widget.Text),
        rotationDegrees: Widget.RotationDegrees,
        glow: Widget.Glow,
        strokeWeightScale: Widget.StrokeWeightScale,
        glyphDrawScale: Widget.GlyphDrawScale);
}
