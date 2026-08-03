// Live cuneiform INPUT render object (add-tablet-cuneiform-chrome). This is the render half of the
// tablet's "type in cuneiform" chrome: it is driven by the SAME ScribeMultilineFieldState that drives
// the normal editable field (buffer + all keyboard/caret/navigation handling), but instead of drawing
// a TTF typeface it paints the buffer as filled cuneiform STROKES (like CuneiformText) and draws a
// SYNTHETIC caret bar at the current character boundary — cuneiform has no native caret.
//
// The State is decoupled from rendering: it pushes (text, caret, focus) integers in and asks the
// render object two geometry questions — OffsetAtPosition (click → source index) and
// CaretOffsetVertical (vertical caret motion). Those two queries are the whole contract, captured by
// IScribeEditableTextRender so the State can talk to EITHER render object without knowing which. All the
// hard positioning (per-character advance, word-wrap) lives in Scribe.Core.Cuneiform and is unit-tested
// there; this file only converts grid units ↔ pixels, stacks wrapped lines, and draws.
//
// Selection (shift-arrow / drag / double-click word / triple-click line) is owned by the shared
// ScribeMultilineFieldState — the same range that drives the normal field. This render object paints a
// highlight box behind the strokes for that range (explore-cuneiform-text-selection), reusing the same
// per-character CaretXAt map the caret uses (a selection is just two boundaries).

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
/// The geometry contract <see cref="ScribeMultilineFieldState"/> depends on to place the caret from a
/// click and to move it vertically. Implemented by BOTH the normal text render object
/// (<see cref="ScribeMultilineFieldRender"/>) and the cuneiform one (<see cref="ScribeCuneiformFieldRender"/>),
/// so the same field State can drive either without a concrete-type cast. All coordinates are in the
/// render object's LOCAL space; offsets are flat indices into the source text.
/// </summary>
internal interface IScribeEditableTextRender
{
    /// <summary>Map a click point in local space onto the nearest flat caret offset in the source text.</summary>
    int OffsetAtPosition(Vector2 local);

    /// <summary>Move a caret offset one visual line up (<paramref name="direction"/> = -1) or down (+1),
    /// landing on the column nearest the caret's current X.</summary>
    int CaretOffsetVertical(int fromCaret, int direction);
}

/// <summary>
/// The render object: lays the buffer out through the Core cuneiform engine (word-wrapped to the
/// available width), paints each line's strokes as filled quads, and draws a synthetic caret bar at the
/// current character boundary when focused. Auto-grows its height to the wrapped line count, exactly
/// like <see cref="ScribeMultilineFieldRender"/>, so it drops into the same row without layout changes.
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
    // Per-stroke hand-written jitter (add-cuneiform-handwriting-feel). 0 = crisp authored geometry. The seed
    // is a FIXED per-field value (not derived from the buffer), so typing a character does not reseed — and
    // thus does not re-wobble — the letters already on screen.
    private float jitterStrength;
    private int jitterSeed;

    // Cached from the last PerformLayout, reused by PaintInternal and the geometry queries so layout,
    // paint, caret, and hit-testing all agree on the same wrapped lines.
    private readonly List<CuneiformLine> lines = new();
    private float scale = 1f;       // grid units → pixels
    private float lineHeightPx = 28f;

    public string Text { get => text; set => SetProperty(ref text, value ?? "", relayout: true); }
    public int Caret { get => caret; set => SetProperty(ref caret, value, repaint: true); }
    /// <summary>The selection's other endpoint (the fixed anchor); the live selection is
    /// [min(anchor,caret), max(anchor,caret)], collapsed when equal to <see cref="Caret"/>. Drives the
    /// highlight only — the shared <see cref="ScribeMultilineFieldState"/> owns the range and gestures.</summary>
    public int SelectionAnchor { get => selectionAnchor; set => SetProperty(ref selectionAnchor, value, repaint: true); }
    public bool FieldHasFocus { get => hasFocus; set => SetProperty(ref hasFocus, value, repaint: true); }
    /// <summary>Blink gate: when false the synthetic caret bar is not painted (the OFF half of the blink),
    /// driven by <see cref="ScribeMultilineFieldState"/>'s shared caret ticker so this caret blinks at the
    /// same cadence as the normal editable field. Focus still gates whether a caret exists at all.</summary>
    public bool CaretVisible { get => caretVisible; set => SetProperty(ref caretVisible, value, repaint: true); }
    public float FontSizeEm { get => fontSizeEm; set => SetProperty(ref fontSizeEm, value, relayout: true); }
    public Vector4 InkColor { get => inkColor; set => SetProperty(ref inkColor, value, repaint: true); }
    public Vector4 CaretColor { get => caretColor; set => SetProperty(ref caretColor, value, repaint: true); }
    /// <summary>Fill color of the selection highlight box painted behind the strokes.</summary>
    public Vector4 SelectionColor { get => selectionColor; set => SetProperty(ref selectionColor, value, repaint: true); }
    public GlyphBundle? Bundle { get => bundle; set => SetProperty(ref bundle, value, relayout: true); }
    public float PadX { get => padX; set => SetProperty(ref padX, value, relayout: true); }
    public float PadY { get => padY; set => SetProperty(ref padY, value, relayout: true); }
    /// <summary>Single-line mode (the title band): lay the whole buffer out as ONE line (never word-wrap)
    /// and keep a fixed one-line height, so a long title is hard-clipped at the band's inner width by the
    /// enclosing clip rather than growing the band taller (design D5/D-Q1). Default false = the auto-growing
    /// multi-line row behavior.</summary>
    public bool SingleLine { get => singleLine; set => SetProperty(ref singleLine, value, relayout: true); }
    /// <summary>Hand-written jitter strength (0..1; 0 = crisp authored geometry). Repaint only — jitter is a
    /// visual transform on the drawn quads and never touches layout, caret, selection, or hit-testing.</summary>
    public float JitterStrength { get => jitterStrength; set => SetProperty(ref jitterStrength, Math.Clamp(value, 0f, 1f), repaint: true); }
    /// <summary>Fixed per-field base seed for the jitter; combined with each stroke's stable identity so the
    /// same character position wobbles the same way and typing doesn't reseed prior letters. Repaint only.</summary>
    public int JitterSeed { get => jitterSeed; set => SetProperty(ref jitterSeed, value, repaint: true); }

    protected override void PerformLayout()
    {
        lines.Clear();

        // Match the rendered cuneiform height to adjacent readable text's line-height, not its raw em, so
        // the tablet's live rows/title read at the same height as normal text (D7 — same global ratio the
        // display CuneiformText uses). One em of grid maps to this boosted height; use the fixed default
        // grid size for the scale so it is stable and independent of the (circular) laid-out line height.
        lineHeightPx = fontSizeEm * CuneiformMetrics.LineHeightRatio;
        scale = (float)(lineHeightPx / CuneiformLineLayout.DefaultGridSize);

        float availWidth = float.IsPositiveInfinity(Constraints.MaxWidth) ? 300f : Constraints.MaxWidth;

        if (bundle is null)
        {
            Size = Constraints.Constrain(new Vector2(availWidth, lineHeightPx + padY * 2));
            return;
        }

        var layout = new CuneiformLineLayout(bundle);
        if (singleLine)
        {
            // Title band: one line, no wrap. Overflow is hard-clipped by the enclosing clip at the band's
            // inner width (cuneiform has no '…' glyph), and the band height stays a single line.
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
        // Draw the box (fill/border/corners) first, then the strokes on top — mirrors both sibling fields.
        base.PaintInternal(context);
        if (context.Canvas is null)
        {
            return;
        }

        // Selection highlight first, so it sits BEHIND the strokes (mirrors ScribeMultilineFieldRender).
        // A selection is just two caret boundaries, so this reuses the same CaretXAt map the caret uses —
        // one DrawBox per wrapped line, clamping the range to each line's source span.
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
        SKColor prevColor = paint.Color;
        SKPaintStyle prevStyle = paint.Style;
        paint.Style = SKPaintStyle.Fill;
        paint.Color = inkColor.ToSkColor();
        paint.IsAntialias = true;

        using var path = new SKPath();
        for (int i = 0; i < lines.Count; i++)
        {
            float originX = padX;
            float originY = padY + i * lineHeightPx;
            foreach (PositionedStroke ps in lines[i].Strokes)
            {
                // Hand-written wobble (visual only): perturb the drawn geometry, keyed off the stroke's
                // stable identity so the SAME character position jitters identically each frame — no shimmer.
                // The caret/selection/hit-testing below all read the UN-jittered layout, untouched.
                GlyphStroke drawStroke = jitterStrength > 0f
                    ? GlyphStrokeJitter.Jitter(
                        ps.Stroke,
                        GlyphStrokeJitter.SeedFor(jitterSeed, ps.SourceCharIndex, ps.StrokeOrdinal),
                        jitterStrength,
                        ps.GridSize)
                    : ps.Stroke;
                Scribe.Core.Cuneiform.Vec2[] corners = drawStroke.Corners();
                path.Reset();
                path.MoveTo(originX + (float)(corners[0].X * scale), originY + (float)(corners[0].Y * scale));
                path.LineTo(originX + (float)(corners[1].X * scale), originY + (float)(corners[1].Y * scale));
                path.LineTo(originX + (float)(corners[2].X * scale), originY + (float)(corners[2].Y * scale));
                path.LineTo(originX + (float)(corners[3].X * scale), originY + (float)(corners[3].Y * scale));
                path.Close();
                context.Canvas.DrawPath(path, paint);
            }
        }

        paint.Color = prevColor;
        paint.Style = prevStyle;

        // Synthetic caret: cuneiform has no native caret, so draw a thin bar at the current character
        // boundary on its wrapped line (same DrawBox the normal field uses for its caret).
        if (hasFocus && caretVisible && lines.Count > 0)
        {
            (int lineIndex, int localIndex) = CaretToLineLocal(caret);
            double caretXGrid = lines[lineIndex].CaretXAt(localIndex);
            float caretX = padX + (float)(caretXGrid * scale);
            float caretY = padY + lineIndex * lineHeightPx;
            context.DrawBox(new Vector2(caretX, caretY), new Vector2(2f, lineHeightPx), caretColor, Vector4.Zero, 0f, Vector4.Zero);
        }
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
}

/// <summary>RenderObjectWidget bridge for the cuneiform editable field: pushes text/caret/focus/colors,
/// the glyph bundle, and the box styling into <see cref="ScribeCuneiformFieldRender"/>.</summary>
internal sealed class ScribeCuneiformFieldRenderWidget : RenderObjectWidget
{
    public ScribeCuneiformFieldRenderWidget(
        string text, int caret, int selectionAnchor, bool hasFocus, float fontSizeEm, Vector4 inkColor,
        Vector4 caretColor, Vector4 selectionColor,
        GlyphBundle? bundle, float padX, float padY,
        Vector4 boxColor, Vector4 borderColor, float borderThickness, Vector4 cornerRadii,
        bool singleLine = false, bool caretVisible = true,
        float jitterStrength = 0f, int jitterSeed = 0)
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
    }
}
