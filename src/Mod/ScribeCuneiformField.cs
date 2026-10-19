// Live cuneiform INPUT render object: the render half of the tablet's "type in cuneiform" chrome. Driven by the
// same ScribeMultilineFieldState as the normal editable field, but paints the buffer as filled cuneiform
// strokes (like CuneiformText) and draws a synthetic caret bar, since cuneiform has no native caret. The State
// pushes (text, caret, focus) in and asks two geometry questions (OffsetAtPosition, CaretOffsetVertical),
// captured by IScribeEditableTextRender. Per-character advance and word-wrap live in Scribe.Core.Cuneiform;
// this file converts grid units to pixels, stacks wrapped lines, and draws. The State owns the selection range;
// this object paints its highlight behind the strokes using the same CaretXAt map as the caret.

using System;
using System.Collections.Generic;
using Gui.Core.Framework;         // RenderObject, RenderBox
using Gui.Rendering;              // PaintingContext
using Gui.Widgets.Framework;      // RenderObjectWidget
using OpenTK.Mathematics;         // Vector2, Vector4
using Scribe.Core.Cuneiform;      // GlyphBundle, CuneiformLineLayout, CuneiformLine, GlyphStroke
using SkiaSharp;                  // SKPath

namespace Scribe;

/// <summary>
/// The geometry contract <see cref="ScribeMultilineFieldState"/> needs to place the caret from a click and move
/// it vertically; implemented by both the normal and cuneiform render objects. Coordinates are in the render
/// object's LOCAL space; offsets are flat indices into the source text.
/// </summary>
internal interface IScribeEditableTextRender
{
    /// <summary>Map a click point in local space onto the nearest flat caret offset in the source text.</summary>
    int OffsetAtPosition(Vector2 local);

    /// <summary>Move a caret offset one visual line up (<paramref name="direction"/> = -1) or down (+1),
    /// landing on the column nearest the caret's current X.</summary>
    int CaretOffsetVertical(int fromCaret, int direction);

    /// <summary>The caret's vertical extent in LOCAL space (<paramref name="localTop"/>, line
    /// <paramref name="height"/>), matching the painted bar. False before layout has produced line metrics. The
    /// editor scrolls to keep the CARET visible; following the whole row would bounce for rows taller than the
    /// viewport.</summary>
    bool TryGetCaretRect(out float localTop, out float height);
}

/// <summary>
/// Lays the buffer out through the Core cuneiform engine (word-wrapped to the available width), paints each
/// line's strokes as filled quads, and draws a synthetic caret when focused. Auto-grows to the wrapped line
/// count like <see cref="ScribeMultilineFieldRender"/>, so it drops into the same row.
/// </summary>
internal sealed class ScribeCuneiformFieldRender : Gui.Core.Framework.RenderBox, IScribeEditableTextRender
{
    private string text = "";
    private int caret;
    private int selectionAnchor;
    private bool hasFocus;
    private bool caretVisible = true;
    private float fontSizeEm = 28f;
    private Vector4 inkColor = Vector4.One;
    private Vector4 caretColor = Vector4.One;
    private Vector4 selectionColor = new(0.4f, 0.55f, 0.9f, 0.35f);
    private GlyphBundle? bundle;
    private float padX = 8f;
    private float padY = 6f;
    private bool singleLine;
    // Per-stroke hand-written jitter (0 = crisp). The seed is a fixed per-field value so typing does not
    // re-wobble letters already on screen.
    private float jitterStrength;
    private int jitterSeed;
    // Whole-character tilt in [-max, +max] degrees about the glyph box center, applied after jitter, paint-time only.
    private float rotationDegrees;
    private CuneiformGlow glow;
    // Multiplier on each stroke's authored weight; 1 = the Core weight.
    private float strokeWeightScale = 1f;
    private float glyphDrawScale = 1f;
    // Paint-time shrink of the caret bar's height within its lineHeightPx band; never affects layout,
    // hit-testing, or the caret's X.
    private float caretHeightScale = 1f;
    // Stroke-progression reveal: characters below the baseline are fully pressed; later ones press in as
    // elapsed advances. Inactive paints every stroke immediately.
    private bool revealActive;
    private int revealBaselineChars;
    private double revealElapsedMs;

    /// <summary>Opacity of the ghost lead-in, as a fraction of the ink alpha. While the reveal is active,
    /// strokes not yet pressed in are drawn once at this faint alpha so the full typed word shows ahead of the
    /// crisp fill. 0 disables it.</summary>
    private const float GhostLeadInOpacity = 0.22f;

    // Cached from PerformLayout so layout, paint, caret, and hit-testing share the same wrapped lines.
    private readonly List<CuneiformLine> lines = new();
    private float scale = 1f;       // grid units → pixels
    private float lineHeightPx = 28f;
    // Prefix-sum of per-character stroke-units for the whole buffer (length text.Length + 1, see
    // CumulativeStrokeUnits), rebuilt each layout and read by the reveal test in DrawStrokePass.
    private int[] revealCumulativeUnits = { 0 };

    public string Text { get => text; set => SetProperty(ref text, value ?? "", relayout: true); }
    public int Caret { get => caret; set => SetProperty(ref caret, value, repaint: true); }
    /// <summary>The selection's fixed endpoint; the highlighted range is [min,max] of this and
    /// <see cref="Caret"/>. Highlight only; the State owns the range.</summary>
    public int SelectionAnchor { get => selectionAnchor; set => SetProperty(ref selectionAnchor, value, repaint: true); }
    public bool FieldHasFocus { get => hasFocus; set => SetProperty(ref hasFocus, value, repaint: true); }
    /// <summary>Blink gate: false hides the caret bar (the OFF half of the blink, driven by the State's shared
    /// ticker). Focus still gates whether a caret exists.</summary>
    public bool CaretVisible { get => caretVisible; set => SetProperty(ref caretVisible, value, repaint: true); }
    public float FontSizeEm { get => fontSizeEm; set => SetProperty(ref fontSizeEm, value, relayout: true); }
    public Vector4 InkColor { get => inkColor; set => SetProperty(ref inkColor, value, repaint: true); }
    public Vector4 CaretColor { get => caretColor; set => SetProperty(ref caretColor, value, repaint: true); }
    public Vector4 SelectionColor { get => selectionColor; set => SetProperty(ref selectionColor, value, repaint: true); }
    public GlyphBundle? Bundle { get => bundle; set => SetProperty(ref bundle, value, relayout: true); }
    public float PadX { get => padX; set => SetProperty(ref padX, value, relayout: true); }
    public float PadY { get => padY; set => SetProperty(ref padY, value, relayout: true); }
    /// <summary>Single-line mode (title band): one unwrapped line at a fixed one-line height; overflow is
    /// hard-clipped by the enclosing clip. Default false = auto-growing multi-line.</summary>
    public bool SingleLine { get => singleLine; set => SetProperty(ref singleLine, value, relayout: true); }
    /// <summary>Jitter strength 0..1 (0 = crisp). Repaint only; never affects layout, caret, selection, or hit-testing.</summary>
    public float JitterStrength { get => jitterStrength; set => SetProperty(ref jitterStrength, Math.Clamp(value, 0f, 1f), repaint: true); }
    /// <summary>Fixed per-field jitter seed, combined with each stroke's identity so typing does not reseed prior letters.</summary>
    public int JitterSeed { get => jitterSeed; set => SetProperty(ref jitterSeed, value, repaint: true); }
    /// <summary>Whole-character rotation in degrees (>= 0). Repaint only, like jitter.</summary>
    public float RotationDegrees { get => rotationDegrees; set => SetProperty(ref rotationDegrees, Math.Max(0f, value), repaint: true); }
    /// <summary>Per-stroke outer glow, disabled by default. Repaint only.</summary>
    public CuneiformGlow Glow { get => glow; set => SetProperty(ref glow, value, repaint: true); }
    /// <summary>Multiplier on each stroke's weight (1 = Core weight), applied at fill only; caret, selection, and
    /// hit-testing read the un-scaled advance metrics. Repaint only.</summary>
    public float StrokeWeightScale { get => strokeWeightScale; set => SetProperty(ref strokeWeightScale, value <= 0f ? 1f : value, repaint: true); }
    /// <summary>Multiplier on the em height in PerformLayout's line-height formula (non-positive = 1). Relayouts;
    /// caret, selection, and hit-testing read the same shrunk `scale`, so they stay in sync with the ink.</summary>
    public float GlyphDrawScale { get => glyphDrawScale; set => SetProperty(ref glyphDrawScale, value <= 0f ? 1f : value, relayout: true); }
    /// <summary>Multiplier on the caret bar's drawn height within its `lineHeightPx` band (non-positive = 1),
    /// shrinking toward the band's vertical center. Repaint only; layout, hit-testing, and caret X are unaffected.</summary>
    public float CaretHeightScale { get => caretHeightScale; set => SetProperty(ref caretHeightScale, value <= 0f ? 1f : value, repaint: true); }
    /// <summary>Whether the stroke-progression reveal is active; false paints every stroke immediately. Repaint only.</summary>
    public bool RevealActive { get => revealActive; set => SetProperty(ref revealActive, value, repaint: true); }
    /// <summary>Characters (absolute index) already fully revealed: the run present before the current reveal
    /// began. Repaint only.</summary>
    public int RevealBaselineChars { get => revealBaselineChars; set => SetProperty(ref revealBaselineChars, value, repaint: true); }
    /// <summary>Elapsed reveal time in ms, driven by the field State's animation controller. Repaint only.</summary>
    public double RevealElapsedMs { get => revealElapsedMs; set => SetProperty(ref revealElapsedMs, value, repaint: true); }

    protected override void PerformLayout()
    {
        lines.Clear();

        // Height follows adjacent readable text's line-height ratio (not raw em) times glyphDrawScale. The grid
        // scale uses the fixed default grid size so it is independent of the (circular) laid-out line height.
        lineHeightPx = fontSizeEm * CuneiformMetrics.LineHeightRatio * glyphDrawScale;
        scale = (float)(lineHeightPx / CuneiformLineLayout.DefaultGridSize);

        float availWidth = float.IsPositiveInfinity(Constraints.MaxWidth) ? 300f : Constraints.MaxWidth;

        if (bundle is null)
        {
            Size = Constraints.Constrain(new Vector2(availWidth, lineHeightPx + padY * 2));
            return;
        }

        var layout = new CuneiformLineLayout(bundle);
        // Prefix-sum of stroke-units for the whole buffer (independent of wrapping), read by DrawStrokePass.
        revealCumulativeUnits = CuneiformReveal.CumulativeStrokeUnits(layout.StrokeUnitsFor(text));
        if (singleLine)
        {
            // Title band: one line, no wrap; overflow is hard-clipped by the enclosing clip (no '…' glyph).
            lines.Add(layout.Layout(text));
        }
        else
        {
            float textWidthPx = Math.Max(1f, availWidth - padX * 2);
            double maxWidthGrid = scale > 0f ? textWidthPx / scale : double.PositiveInfinity;
            lines.AddRange(layout.LayoutWrapped(text, maxWidthGrid));
            if (lines.Count == 0)
            {
                lines.Add(layout.Layout(""));
            }
        }

        float height = lines.Count * lineHeightPx + padY * 2;
        Size = Constraints.Constrain(new Vector2(availWidth, height));
    }

    protected override void PaintInternal(PaintingContext context)
    {
        base.PaintInternal(context);
        if (context.Canvas is null)
        {
            return;
        }

        // Selection highlight first, behind the strokes: one box per wrapped line, clamped to its source span,
        // using the same CaretXAt map as the caret.
        int selStart = Math.Min(caret, selectionAnchor);
        int selEnd = Math.Max(caret, selectionAnchor);
        if (hasFocus && selEnd > selStart)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                int lineStart = lines[i].SourceStart;
                int lineEnd = lineStart + (lines[i].CharBoundaries.Count - 1); // boundary count = char count + 1
                int a = Math.Max(selStart, lineStart);
                int b = Math.Min(selEnd, lineEnd);
                if (b <= a)
                {
                    continue;
                }

                float x1 = padX + (float)(lines[i].CaretXAt(a - lineStart) * scale);
                float x2 = padX + (float)(lines[i].CaretXAt(b - lineStart) * scale);
                float y = padY + i * lineHeightPx;
                context.DrawBox(new Vector2(x1, y), new Vector2(x2 - x1, lineHeightPx), selectionColor, Vector4.Zero, 0f, Vector4.Zero);
            }
        }

        SKPaint paint = context.SharedPaint;
        SKPaintStyle prevStyle = paint.Style;
        bool prevAntialias = paint.IsAntialias;
        paint.Style = SKPaintStyle.Fill;
        paint.IsAntialias = true;

        var schedule = new RevealSchedule(CuneiformReveal.PerStrokeMs);
        using var path = new SKPath();

        // Pass 1 (optional): every revealed stroke's blurred halo across ALL lines before any crisp ink, so the
        // crisp fills overwrite halos inside glyphs and overlapping strokes do not double their glow. Uses the
        // same jitter and reveal test as the crisp pass.
        if (glow.Enabled && CuneiformGlowMask.ForSigma(fontSizeEm * glow.BlurFraction) is { } mask)
        {
            paint.Color = glow.Color.ToSkColor();
            paint.MaskFilter = mask;
            // Optional directional offset (fraction of the em) translates only the halo pass; ink draws un-offset.
            float glowOffX = fontSizeEm * glow.OffsetXFraction;
            float glowOffY = fontSizeEm * glow.OffsetYFraction;
            bool offsetHalo = glowOffX != 0f || glowOffY != 0f;
            if (offsetHalo)
            {
                context.Canvas.Save();
                context.Canvas.Translate(glowOffX, glowOffY);
            }
            DrawStrokePass(context, path, schedule, StrokePass.Revealed);
            if (offsetHalo)
            {
                context.Canvas.Restore();
            }
            paint.MaskFilter = null;   // clear before the crisp pass (and before return) — SharedPaint hygiene.
        }

        // Ghost lead-in: while the reveal runs, paint the not-yet-pressed strokes once at faint alpha so the full
        // typed text shows ahead of the crisp fill. Disjoint from the crisp pass; no halo; tints the ink color's alpha.
        if (revealActive && GhostLeadInOpacity > 0f)
        {
            var ghostColor = inkColor;
            ghostColor.W *= GhostLeadInOpacity;
            paint.Color = ghostColor.ToSkColor();
            DrawStrokePass(context, path, schedule, StrokePass.Ghost);
        }

        // Pass 2: every revealed stroke's crisp ink fill, on top of the halos (and the ghost tail).
        paint.Color = inkColor.ToSkColor();
        DrawStrokePass(context, path, schedule, StrokePass.Revealed);

        // Leave the shared paint OPAQUE rather than restoring the inherited alpha-0 color left by
        // base.PaintInternal's resting box: DrawMaskedBox (the clay-texture draw) reuses SharedPaint.Color without
        // setting it, so an alpha-0 leftover would render the clay backdrop transparent.
        paint.Color = SKColors.White;
        paint.Style = prevStyle;
        paint.IsAntialias = prevAntialias;
        paint.MaskFilter = null;   // defensive: never leave a blur mask on the shared paint.

        // Synthetic caret: a thin bar at the caret boundary, CaretHeightScale of the line band, vertically
        // centered (paint-only).
        if (hasFocus && caretVisible && lines.Count > 0)
        {
            (int lineIndex, int localIndex) = CaretToLineLocal(caret);
            double caretXGrid = lines[lineIndex].CaretXAt(localIndex);
            float caretX = padX + (float)(caretXGrid * scale);
            float caretHeight = lineHeightPx * caretHeightScale;
            float caretY = padY + lineIndex * lineHeightPx + (lineHeightPx - caretHeight) / 2f;
            context.DrawBox(new Vector2(caretX, caretY), new Vector2(2f, caretHeight), caretColor, Vector4.Zero, 0f, Vector4.Zero);
        }
    }

    /// <summary>Which strokes a <see cref="DrawStrokePass"/> call paints. <see cref="Revealed"/> is the pressed-in
    /// ink (every stroke when the reveal is inactive); <see cref="Ghost"/> is the not-yet-pressed tail, requested
    /// only while the reveal is active.</summary>
    private enum StrokePass
    {
        Revealed,
        Ghost,
    }

    /// <summary>Draws one pass over every wrapped line's strokes with the shared paint's CURRENT color/mask,
    /// restricted to <paramref name="pass"/>. Halo, ghost, and ink share identical jitter/rotation geometry and
    /// reveal split; caret, selection, and hit-testing read the un-jittered layout.</summary>
    private void DrawStrokePass(PaintingContext context, SKPath path, RevealSchedule schedule, StrokePass pass)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            float originX = padX;
            float originY = padY + i * lineHeightPx;
            int lineStart = lines[i].SourceStart;
            foreach (PositionedStroke ps in lines[i].Strokes)
            {
                // Reveal split: each stroke is pressed-in or in the ghost tail, and the passes are disjoint. Keyed
                // off the ABSOLUTE character index so the schedule is continuous across wrapped lines; characters
                // below the baseline always count as revealed.
                bool strokeRevealed = !revealActive || CuneiformReveal.IsStrokeRevealed(
                    lineStart + ps.SourceCharIndex, ps.StrokeOrdinal, revealBaselineChars, revealElapsedMs,
                    schedule, revealCumulativeUnits);
                bool wantThisStroke = pass == StrokePass.Ghost ? !strokeRevealed : strokeRevealed;
                if (!wantThisStroke)
                {
                    continue;
                }

                GlyphStroke drawStroke = jitterStrength > 0f
                    ? GlyphStrokeJitter.Jitter(
                        ps.Stroke,
                        GlyphStrokeJitter.SeedFor(jitterSeed, ps.SourceCharIndex, ps.StrokeOrdinal),
                        jitterStrength,
                        ps.GridSize)
                    : ps.Stroke;
                if (rotationDegrees > 0f)
                {
                    // Rigid per-character tilt about the glyph box center, after jitter. Seeded without the stroke
                    // ordinal so every stroke of one character shares the angle.
                    drawStroke = GlyphStrokeRotation.Rotate(
                        drawStroke,
                        GlyphRotationPivot(lines[i], ps),
                        GlyphStrokeRotation.SeedFor(jitterSeed, ps.SourceCharIndex),
                        rotationDegrees);
                }
                // Per-view weight scale, applied after jitter/rotation and only when != 1 (keeps the common path
                // allocation-free). Changes painted thickness only.
                if (strokeWeightScale != 1f)
                {
                    drawStroke = new GlyphStroke(drawStroke.Start, drawStroke.End, drawStroke.Weight * strokeWeightScale);
                }
                Scribe.Core.Cuneiform.Vec2[] corners = drawStroke.Corners();

                // GlyphDrawScale is already folded into `scale` by PerformLayout; this is a plain grid-to-pixel conversion.
                path.Reset();
                path.MoveTo(originX + (float)(corners[0].X * scale), originY + (float)(corners[0].Y * scale));
                path.LineTo(originX + (float)(corners[1].X * scale), originY + (float)(corners[1].Y * scale));
                path.LineTo(originX + (float)(corners[2].X * scale), originY + (float)(corners[2].Y * scale));
                path.LineTo(originX + (float)(corners[3].X * scale), originY + (float)(corners[3].Y * scale));
                path.Close();
                context.Canvas!.DrawPath(path, paint: context.SharedPaint);
            }
        }
    }

    /// <summary>The pivot to rotate a character's strokes about, in grid units: X is the midpoint of the source
    /// character's advance span from the un-jittered layout, Y is half the glyph grid. Read from layout (never a
    /// transformed stroke) so rotation stays paint-only.</summary>
    private static Scribe.Core.Cuneiform.Vec2 GlyphRotationPivot(CuneiformLine line, PositionedStroke ps)
    {
        var b = line.CharBoundaries;
        int i = ps.SourceCharIndex;
        double left = i >= 0 && i < b.Count ? b[i] : ps.Stroke.Start.X;
        double right = i + 1 >= 0 && i + 1 < b.Count ? b[i + 1] : left;
        return new Scribe.Core.Cuneiform.Vec2((left + right) / 2.0, ps.GridSize / 2.0);
    }

    /// <summary>Map a flat source caret offset onto (wrapped line index, local boundary index within that
    /// line). A caret that lands in a separator dropped by soft-wrap (the break space between two lines)
    /// attaches to the end of the earlier line, mirroring the normal field's <c>CaretToLineCol</c>.</summary>
    private (int lineIndex, int localIndex) CaretToLineLocal(int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        for (int i = 0; i < lines.Count; i++)
        {
            int start = lines[i].SourceStart;
            int count = lines[i].CharBoundaries.Count - 1; // boundary count = char count + 1
            int end = start + count;

            if (offset <= end)
            {
                if (offset >= start)
                {
                    return (i, offset - start);
                }
                // Before this line's first char — in a dropped separator (or leading space). Attach to the
                // end of the previous line, or the very start if this is the first line.
                if (i == 0)
                {
                    return (0, 0);
                }
                int prevCount = lines[i - 1].CharBoundaries.Count - 1;
                return (i - 1, prevCount);
            }
        }

        int last = Math.Max(0, lines.Count - 1);
        int lastCount = lines.Count > 0 ? lines[last].CharBoundaries.Count - 1 : 0;
        return (last, lastCount);
    }

    public int OffsetAtPosition(Vector2 local)
    {
        if (lines.Count == 0)
        {
            return 0;
        }

        int line = (int)Math.Floor((local.Y - padY) / lineHeightPx);
        line = Math.Clamp(line, 0, lines.Count - 1);

        double localXGrid = scale > 0f ? (local.X - padX) / scale : 0.0;
        int localIndex = lines[line].NearestBoundary(localXGrid);
        return Math.Clamp(lines[line].SourceStart + localIndex, 0, text.Length);
    }

    public int CaretOffsetVertical(int fromCaret, int direction)
    {
        if (lines.Count == 0)
        {
            return 0;
        }

        (int line, int localIndex) = CaretToLineLocal(fromCaret);
        int targetLine = line + direction;
        if (targetLine < 0)
        {
            return 0;
        }
        if (targetLine >= lines.Count)
        {
            return text.Length;
        }

        double caretXGrid = lines[line].CaretXAt(localIndex);
        int targetLocal = lines[targetLine].NearestBoundary(caretXGrid);
        return Math.Clamp(lines[targetLine].SourceStart + targetLocal, 0, text.Length);
    }

    /// <summary>Caret vertical extent in LOCAL space (top + height), matching the (possibly shrunk) bar
    /// <see cref="PaintInternal"/> draws. Returns false before layout has produced a line height.</summary>
    public bool TryGetCaretRect(out float localTop, out float height)
    {
        if (lineHeightPx <= 0f)
        {
            localTop = 0f;
            height = 0f;
            return false;
        }

        int lineIndex = 0;
        if (lines.Count > 0)
        {
            (lineIndex, _) = CaretToLineLocal(caret);
        }
        height = lineHeightPx * caretHeightScale;
        localTop = padY + lineIndex * lineHeightPx + (lineHeightPx - height) / 2f;
        return true;
    }
}

/// <summary>RenderObjectWidget bridge: pushes text/caret/focus/colors, glyph bundle, and box styling into
/// <see cref="ScribeCuneiformFieldRender"/>.</summary>
internal sealed class ScribeCuneiformFieldRenderWidget : RenderObjectWidget
{
    public ScribeCuneiformFieldRenderWidget(
        string text, int caret, int selectionAnchor, bool hasFocus, float fontSizeEm, Vector4 inkColor,
        Vector4 caretColor, Vector4 selectionColor,
        GlyphBundle? bundle, float padX, float padY,
        Vector4 boxColor, Vector4 borderColor, float borderThickness, Vector4 cornerRadii,
        bool singleLine = false, bool caretVisible = true,
        float jitterStrength = 0f, int jitterSeed = 0, float rotationDegrees = 0f, CuneiformGlow glow = default,
        float strokeWeightScale = 1f, float glyphDrawScale = 1f, float caretHeightScale = 1f,
        bool revealActive = false, int revealBaselineChars = 0, double revealElapsedMs = 0)
    {
        Text = text;
        Caret = caret;
        SelectionAnchor = selectionAnchor;
        HasFocus = hasFocus;
        CaretVisible = caretVisible;
        FontSizeEm = fontSizeEm;
        InkColor = inkColor;
        CaretColor = caretColor;
        SelectionColor = selectionColor;
        Bundle = bundle;
        PadX = padX;
        PadY = padY;
        BoxColor = boxColor;
        BorderColor = borderColor;
        BorderThickness = borderThickness;
        CornerRadii = cornerRadii;
        SingleLine = singleLine;
        JitterStrength = jitterStrength;
        JitterSeed = jitterSeed;
        RotationDegrees = rotationDegrees;
        Glow = glow;
        StrokeWeightScale = strokeWeightScale;
        GlyphDrawScale = glyphDrawScale;
        CaretHeightScale = caretHeightScale;
        RevealActive = revealActive;
        RevealBaselineChars = revealBaselineChars;
        RevealElapsedMs = revealElapsedMs;
    }

    public string Text { get; }
    public int Caret { get; }
    public int SelectionAnchor { get; }
    public bool HasFocus { get; }
    public bool CaretVisible { get; }
    public float FontSizeEm { get; }
    public Vector4 InkColor { get; }
    public Vector4 CaretColor { get; }
    public Vector4 SelectionColor { get; }
    public GlyphBundle? Bundle { get; }
    public float PadX { get; }
    public float PadY { get; }
    public Vector4 BoxColor { get; }
    public Vector4 BorderColor { get; }
    public float BorderThickness { get; }
    public Vector4 CornerRadii { get; }
    public bool SingleLine { get; }
    public float JitterStrength { get; }
    public int JitterSeed { get; }
    public float RotationDegrees { get; }
    public CuneiformGlow Glow { get; }
    public float StrokeWeightScale { get; }
    public float GlyphDrawScale { get; }
    public float CaretHeightScale { get; }
    public bool RevealActive { get; }
    public int RevealBaselineChars { get; }
    public double RevealElapsedMs { get; }

    public override RenderObject CreateRenderObject() => new ScribeCuneiformFieldRender();

    public override void UpdateRenderObject(RenderObject renderObject)
    {
        var ro = (ScribeCuneiformFieldRender)renderObject;
        ro.Text = Text;
        ro.Caret = Caret;
        ro.SelectionAnchor = SelectionAnchor;
        ro.FieldHasFocus = HasFocus;
        ro.CaretVisible = CaretVisible;
        ro.FontSizeEm = FontSizeEm;
        ro.InkColor = InkColor;
        ro.CaretColor = CaretColor;
        ro.SelectionColor = SelectionColor;
        ro.Bundle = Bundle;
        ro.PadX = PadX;
        ro.PadY = PadY;
        ro.Color = BoxColor;
        ro.BorderColor = BorderColor;
        ro.BorderThickness = BorderThickness;
        ro.CornerRadii = CornerRadii;
        ro.SingleLine = SingleLine;
        ro.JitterStrength = JitterStrength;
        ro.JitterSeed = JitterSeed;
        ro.RotationDegrees = RotationDegrees;
        ro.Glow = Glow;
        ro.StrokeWeightScale = StrokeWeightScale;
        ro.GlyphDrawScale = GlyphDrawScale;
        ro.CaretHeightScale = CaretHeightScale;
        ro.RevealActive = RevealActive;
        ro.RevealBaselineChars = RevealBaselineChars;
        ro.RevealElapsedMs = RevealElapsedMs;
    }
}
