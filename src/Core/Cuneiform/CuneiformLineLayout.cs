namespace Scribe.Core.Cuneiform;

/// <summary>
/// One stroke of a laid-out line, positioned in the line's own grid-unit coordinate space (the pen
/// runs along x; y is the glyph's own grid y). Carries the source glyph's <see cref="GridSize"/> so a
/// renderer can scale grid units → pixels per glyph. Produced in AUTHORED CONSTRUCTION ORDER across the
/// whole line, so rendering the first N entries yields a partial reveal.
/// </summary>
public readonly struct PositionedStroke
{
    /// <summary>The stroke geometry, already offset into the line's coordinate space (grid units).</summary>
    public GlyphStroke Stroke { get; }

    /// <summary>The source glyph's em-grid size, for grid→pixel scaling.</summary>
    public double GridSize { get; }

    public PositionedStroke(GlyphStroke stroke, double gridSize)
    {
        Stroke = stroke;
        GridSize = gridSize;
    }
}

/// <summary>
/// The result of laying a string out: the positioned strokes (in construction order) plus the line's
/// total advance width and its height, all in grid units. <see cref="TotalWidth"/> is the pen position
/// after the last glyph; <see cref="LineHeight"/> is the em height a renderer sizes the line box to.
/// </summary>
public sealed class CuneiformLine
{
    /// <summary>Positioned strokes across the whole line, in authored construction order.</summary>
    public IReadOnlyList<PositionedStroke> Strokes { get; }

    /// <summary>Total advance width of the line in grid units (pen position after the last glyph).</summary>
    public double TotalWidth { get; }

    /// <summary>The line's height in grid units (the em height a renderer scales from).</summary>
    public double LineHeight { get; }

    public CuneiformLine(IReadOnlyList<PositionedStroke> strokes, double totalWidth, double lineHeight)
    {
        Strokes = strokes;
        TotalWidth = totalWidth;
        LineHeight = lineHeight;
    }
}

/// <summary>
/// Lays a single line of text out into positioned cuneiform strokes with proportional advance, using a
/// <see cref="GlyphBundle"/>. The heart of the cuneiform "font": it absorbs the quirks of the authored
/// set — uppercase-only (input is folded to uppercase before lookup), no space glyph (a space advances
/// a fixed word gap and emits no strokes), and possibly-missing glyphs (a small gap, never a throw) —
/// so callers pass ordinary text and get back drawable geometry.
///
/// Positioning matches the glyph-forge export format exactly: each glyph's strokes are offset so the
/// glyph's own local box-left edge (<see cref="Glyph.LocalBoxLeft"/>) aligns with the pen, the pen then
/// advances by the glyph's footprint (<see cref="Glyph.AdvanceWidth"/>), and neighbors are separated by
/// a hard padding floor (<c>rightPadding(prev) + leftPadding(next)</c>) that a per-pair kerning value
/// MAY widen but MUST NOT narrow past the floor. Game-agnostic (pure BCL), so the spacing/kerning math
/// is unit-testable on CI without a game install.
/// </summary>
public sealed class CuneiformLineLayout
{
    private readonly GlyphBundle _bundle;

    /// <summary>Word-gap advance (grid units) for a space character, which has no authored glyph.
    /// Tunable against the in-game harness. Roughly half an em reads as a clear word break without the
    /// script feeling loose.</summary>
    public double WordGapUnits { get; }

    /// <summary>Advance (grid units) for a character with no authored glyph and no special handling — a
    /// small gap so an unknown character leaves a subtle space rather than crashing or vanishing
    /// silently. Distinct from (and smaller than) <see cref="WordGapUnits"/>.</summary>
    public double MissingGlyphGapUnits { get; }

    /// <summary>Fallback line height (grid units) when the bundle is empty and no glyph supplies a
    /// grid size; matches the authored em-grid.</summary>
    public const double DefaultGridSize = 100.0;

    public CuneiformLineLayout(GlyphBundle bundle, double wordGapUnits = 45.0, double missingGlyphGapUnits = 20.0)
    {
        _bundle = bundle;
        WordGapUnits = wordGapUnits;
        MissingGlyphGapUnits = missingGlyphGapUnits;
    }

    /// <summary>
    /// Lays <paramref name="text"/> out into a <see cref="CuneiformLine"/>. Input is folded to uppercase
    /// before glyph lookup (the authored set is uppercase-only); spaces advance <see cref="WordGapUnits"/>
    /// with no strokes; a character with no authored glyph advances <see cref="MissingGlyphGapUnits"/>
    /// with no strokes (never throws). Strokes are emitted in authored construction order across the line.
    /// </summary>
    public CuneiformLine Layout(string text)
    {
        var positioned = new List<PositionedStroke>();
        double pen = 0.0;
        double lineHeight = DefaultGridSize;
        Glyph? prev = null;

        foreach (char raw in text ?? string.Empty)
        {
            if (raw == ' ' || raw == '\t')
            {
                pen += WordGapUnits;
                prev = null; // no glyph on either side of a space to kern against
                continue;
            }

            char c = char.ToUpperInvariant(raw);
            Glyph? glyph = _bundle.Get(c);

            if (glyph is null)
            {
                pen += MissingGlyphGapUnits;
                prev = null;
                continue;
            }

            lineHeight = Math.Max(lineHeight, glyph.GridSize);

            // Separate this glyph from the previous one by the hard padding floor, widened (never
            // narrowed) by any authored kerning for the ordered pair.
            if (prev is not null)
            {
                pen += GapBetween(prev, glyph);
            }

            // Align the glyph's local box-left edge with the pen: renderedX = strokeX - localBoxLeft + pen.
            double xOffset = pen - glyph.LocalBoxLeft;
            foreach (GlyphStroke s in glyph.Strokes)
            {
                var shifted = new GlyphStroke(
                    new Vec2(s.Start.X + xOffset, s.Start.Y),
                    new Vec2(s.End.X + xOffset, s.End.Y),
                    s.Weight);
                positioned.Add(new PositionedStroke(shifted, glyph.GridSize));
            }

            pen += glyph.AdvanceWidth;
            prev = glyph;
        }

        return new CuneiformLine(positioned, pen, lineHeight);
    }

    /// <summary>
    /// The inter-glyph gap between an ordered pair: the padding floor (<c>rightPadding(prev) +
    /// leftPadding(next)</c>) widened by <paramref name="prev"/>'s kerning for <paramref name="next"/>
    /// when that widens it. A kerning value that would narrow the gap below the floor is clamped to the
    /// floor (kerning only ever widens here — it never pulls neighbors closer than their paddings allow).
    /// </summary>
    private static double GapBetween(Glyph prev, Glyph next)
    {
        double floor = prev.RightPadding + next.LeftPadding;
        if (prev.Kerning.TryGetValue(next.Character, out double kern))
        {
            return Math.Max(floor, floor + kern);
        }

        return floor;
    }
}
