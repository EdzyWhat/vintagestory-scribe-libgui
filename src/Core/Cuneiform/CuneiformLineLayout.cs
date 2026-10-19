namespace Scribe.Core.Cuneiform;

/// <summary>
/// One stroke of a laid-out line, in the line's grid-unit coordinate space (pen runs along x). Strokes
/// are in authored construction order across the line, so the first N entries are a partial reveal.
/// <see cref="SourceCharIndex"/> and <see cref="StrokeOrdinal"/> give a stable identity for per-stroke
/// jitter seeds and per-letter reveal schedules; they never affect geometry.
/// </summary>
public readonly struct PositionedStroke
{
    /// <summary>The stroke geometry, already offset into the line's coordinate space (grid units).</summary>
    public GlyphStroke Stroke { get; }

    /// <summary>The source glyph's em-grid size, for grid→pixel scaling.</summary>
    public double GridSize { get; }

    /// <summary>Index of the source character within THIS line (same index space as
    /// <see cref="CuneiformLine.CharBoundaries"/>).</summary>
    public int SourceCharIndex { get; }

    /// <summary>0-based ordinal within its glyph, in authored construction order.</summary>
    public int StrokeOrdinal { get; }

    public PositionedStroke(GlyphStroke stroke, double gridSize, int sourceCharIndex, int strokeOrdinal)
    {
        Stroke = stroke;
        GridSize = gridSize;
        SourceCharIndex = sourceCharIndex;
        StrokeOrdinal = strokeOrdinal;
    }
}

/// <summary>
/// A laid-out line: positioned strokes plus total advance width and height, all in grid units.
/// <see cref="CharBoundaries"/> (pen position per source-character boundary) lets callers place a
/// synthetic caret and hit-test clicks, since cuneiform has no native caret.
/// </summary>
public sealed class CuneiformLine
{
    /// <summary>Positioned strokes across the whole line, in authored construction order.</summary>
    public IReadOnlyList<PositionedStroke> Strokes { get; }

    /// <summary>Total advance width of the line in grid units (pen position after the last glyph).</summary>
    public double TotalWidth { get; }

    /// <summary>The line's height in grid units (the em height a renderer scales from).</summary>
    public double LineHeight { get; }

    /// <summary>Cumulative pen position (grid units) at each source-character boundary of THIS line.
    /// Length is character count + 1: index 0 is 0.0 and the last entry equals <see cref="TotalWidth"/>.
    /// Every character advances the pen, so caret indices stay stable across spaces and unknown characters.</summary>
    public IReadOnlyList<double> CharBoundaries { get; }

    /// <summary>Index, in the original laid-out string, of this line's first character.</summary>
    public int SourceStart { get; }

    public CuneiformLine(
        IReadOnlyList<PositionedStroke> strokes,
        double totalWidth,
        double lineHeight,
        IReadOnlyList<double> charBoundaries,
        int sourceStart)
    {
        Strokes = strokes;
        TotalWidth = totalWidth;
        LineHeight = lineHeight;
        CharBoundaries = charBoundaries;
        SourceStart = sourceStart;
    }

    /// <summary>Pen position (grid units) of the caret before the character at
    /// <paramref name="localCharIndex"/>; the index is clamped to a valid boundary.</summary>
    public double CaretXAt(int localCharIndex)
    {
        if (CharBoundaries.Count == 0)
        {
            return 0.0;
        }

        int idx = Math.Clamp(localCharIndex, 0, CharBoundaries.Count - 1);
        return CharBoundaries[idx];
    }

    /// <summary>The boundary index whose pen position is nearest <paramref name="x"/> (where a click
    /// should place the caret). Ties resolve to the lower index.</summary>
    public int NearestBoundary(double x)
    {
        if (CharBoundaries.Count == 0)
        {
            return 0;
        }

        int best = 0;
        double bestDist = Math.Abs(CharBoundaries[0] - x);
        for (int i = 1; i < CharBoundaries.Count; i++)
        {
            double dist = Math.Abs(CharBoundaries[i] - x);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }

        return best;
    }
}

/// <summary>
/// Lays text out into positioned cuneiform strokes with proportional advance. Absorbs the authored
/// set's quirks: uppercase-only (input is folded), no space glyph (a space advances a word gap),
/// and missing glyphs (a small gap, never a throw).
///
/// Positioning matches the glyph-forge export format: each glyph's <see cref="Glyph.LocalBoxLeft"/>
/// aligns with the pen, the pen advances by <see cref="Glyph.AdvanceWidth"/>, and neighbors are
/// separated by a padding floor (<c>rightPadding(prev) + leftPadding(next)</c>) that kerning may
/// widen but MUST NOT narrow.
/// </summary>
public sealed class CuneiformLineLayout
{
    private readonly GlyphBundle _bundle;

    /// <summary>
    /// Many-to-one alias map applied after uppercase-folding and before <see cref="GlyphBundle.Get"/>:
    /// a character with no glyph of its own renders as a look-alike authored glyph, with identical
    /// strokes and advance. An alias to an unauthored target falls through to the missing-glyph gap.
    /// </summary>
    private static readonly IReadOnlyDictionary<char, char> Aliases = new Dictionary<char, char>
    {
        ['['] = '(',
        ['{'] = '(',
        [']'] = ')',
        ['}'] = ')',
        ['&'] = '+',
    };

    /// <summary>Word-gap advance (grid units) for a space, which has no authored glyph.</summary>
    public double WordGapUnits { get; }

    /// <summary>Advance (grid units) for a character with no authored glyph; smaller than
    /// <see cref="WordGapUnits"/>.</summary>
    public double MissingGlyphGapUnits { get; }

    /// <summary>Fallback line height (grid units) when no glyph supplies a grid size.</summary>
    public const double DefaultGridSize = 100.0;

    public CuneiformLineLayout(GlyphBundle bundle, double wordGapUnits = 45.0, double missingGlyphGapUnits = 20.0)
    {
        _bundle = bundle;
        WordGapUnits = wordGapUnits;
        MissingGlyphGapUnits = missingGlyphGapUnits;
    }

    /// <summary>
    /// Lays <paramref name="text"/> out as a single <see cref="CuneiformLine"/>. Spaces advance
    /// <see cref="WordGapUnits"/>; characters with no glyph advance <see cref="MissingGlyphGapUnits"/>;
    /// neither emits strokes, and nothing throws.
    /// </summary>
    public CuneiformLine Layout(string text)
    {
        return LayoutSegment(text ?? string.Empty, 0);
    }

    /// <summary>
    /// Lays <paramref name="text"/> out as one or more lines. A hard <c>'\n'</c> always splits a line;
    /// within a paragraph, text soft-wraps at space/tab boundaries to fit <paramref name="maxWidthGridUnits"/>.
    /// A word wider than the maximum gets its own line (never split mid-glyph). A non-positive or infinite
    /// maximum disables soft wrapping. Each line's <see cref="CuneiformLine.SourceStart"/> is absolute in
    /// <paramref name="text"/>; its <see cref="CuneiformLine.CharBoundaries"/> are line-local.
    /// </summary>
    public IReadOnlyList<CuneiformLine> LayoutWrapped(string text, double maxWidthGridUnits)
    {
        string source = text ?? string.Empty;
        bool noSoftWrap = !(maxWidthGridUnits > 0.0) || double.IsInfinity(maxWidthGridUnits);

        // An empty paragraph (bare or trailing '\n') yields one empty line so a caret can sit on it.
        var lines = new List<CuneiformLine>();
        int paragraphStart = 0;
        foreach (string paragraph in source.Split('\n'))
        {
            WrapParagraph(paragraph, paragraphStart, maxWidthGridUnits, noSoftWrap, lines);
            paragraphStart += paragraph.Length + 1; // + the consumed '\n'
        }
        return lines;
    }

    /// <summary>
    /// Soft-wraps one paragraph into <paramref name="outLines"/>, stamping absolute
    /// <see cref="CuneiformLine.SourceStart"/> (<paramref name="globalStart"/> + in-paragraph offset).
    /// </summary>
    private void WrapParagraph(
        string paragraph, int globalStart, double maxWidthGridUnits, bool noSoftWrap, List<CuneiformLine> outLines)
    {
        if (noSoftWrap)
        {
            outLines.Add(LayoutSegment(paragraph, globalStart));
            return;
        }

        // Words are maximal runs of non-space/tab, as half-open [start, end). There is no kerning
        // across a space, so words lay out independently.
        var words = new List<(int Start, int End)>();
        int wordStart = -1;
        for (int i = 0; i < paragraph.Length; i++)
        {
            bool isSpace = paragraph[i] == ' ' || paragraph[i] == '\t';
            if (!isSpace)
            {
                if (wordStart < 0) wordStart = i;
            }
            else if (wordStart >= 0)
            {
                words.Add((wordStart, i));
                wordStart = -1;
            }
        }
        if (wordStart >= 0)
        {
            words.Add((wordStart, paragraph.Length));
        }

        // Empty or whitespace-only paragraph: one line, keeping its whitespace.
        if (words.Count == 0)
        {
            outLines.Add(LayoutSegment(paragraph, globalStart));
            return;
        }

        double WordWidth((int Start, int End) w) =>
            LayoutSegment(paragraph.Substring(w.Start, w.End - w.Start), 0).TotalWidth;

        int lineStart = words[0].Start;
        int lineEnd = words[0].End;
        double lineWidth = WordWidth(words[0]);

        for (int k = 1; k < words.Count; k++)
        {
            double wordWidth = WordWidth(words[k]);
            double candidate = lineWidth + WordGapUnits + wordWidth;

            if (candidate > maxWidthGridUnits)
            {
                // Overflow: flush the line; an over-long word becomes its own line.
                outLines.Add(LayoutSegment(paragraph.Substring(lineStart, lineEnd - lineStart), globalStart + lineStart));
                lineStart = words[k].Start;
                lineEnd = words[k].End;
                lineWidth = wordWidth;
            }
            else
            {
                lineEnd = words[k].End;
                lineWidth = candidate;
            }
        }

        // The final line runs to the paragraph end so a trailing space still advances the caret.
        // Interior break-spaces between wrapped lines are dropped.
        outLines.Add(LayoutSegment(paragraph.Substring(lineStart), globalStart + lineStart));
    }

    /// <summary>
    /// Per-character stroke-unit counts for <see cref="CuneiformReveal"/> timing, one entry per character
    /// of <paramref name="text"/>. Glyphs contribute their stroke count; spaces and missing glyphs
    /// contribute <see cref="CuneiformReveal.SpaceStrokeUnits"/> / <see cref="CuneiformReveal.MissingGlyphStrokeUnits"/>
    /// (no ink, but a beat). Uses the same fold + alias + lookup as <see cref="LayoutSegment"/>.
    /// </summary>
    public IReadOnlyList<int> StrokeUnitsFor(string text)
    {
        string source = text ?? string.Empty;
        var units = new int[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            char raw = source[i];
            if (raw == ' ' || raw == '\t' || raw == '\n')
            {
                units[i] = CuneiformReveal.SpaceStrokeUnits;
                continue;
            }

            char c = char.ToUpperInvariant(raw);
            if (Aliases.TryGetValue(c, out char alias))
            {
                c = alias;
            }
            Glyph? glyph = _bundle.Get(c);
            units[i] = glyph is null ? CuneiformReveal.MissingGlyphStrokeUnits : glyph.Strokes.Count;
        }

        return units;
    }

    /// <summary>Lays one segment out; <paramref name="sourceStart"/> becomes <see cref="CuneiformLine.SourceStart"/>.</summary>
    private CuneiformLine LayoutSegment(string text, int sourceStart)
    {
        var positioned = new List<PositionedStroke>();
        var boundaries = new List<double>(text.Length + 1) { 0.0 };
        double pen = 0.0;
        double lineHeight = DefaultGridSize;
        Glyph? prev = null;

        foreach (char raw in text)
        {
            if (raw == ' ' || raw == '\t')
            {
                pen += WordGapUnits;
                prev = null; // no glyph on either side of a space to kern against
                boundaries.Add(pen);
                continue;
            }

            char c = char.ToUpperInvariant(raw);
            if (Aliases.TryGetValue(c, out char alias))
            {
                c = alias;
            }
            Glyph? glyph = _bundle.Get(c);

            if (glyph is null)
            {
                pen += MissingGlyphGapUnits;
                prev = null;
                boundaries.Add(pen);
                continue;
            }

            lineHeight = Math.Max(lineHeight, glyph.GridSize);

            if (prev is not null)
            {
                pen += GapBetween(prev, glyph);
            }

            // boundaries starts as {0.0} and gains one entry per character, so Count - 1 is this
            // character's index.
            int sourceCharIndex = boundaries.Count - 1;

            // Align the glyph's local box-left edge with the pen: renderedX = strokeX - localBoxLeft + pen.
            double xOffset = pen - glyph.LocalBoxLeft;
            int strokeOrdinal = 0;
            foreach (GlyphStroke s in glyph.Strokes)
            {
                var shifted = new GlyphStroke(
                    new Vec2(s.Start.X + xOffset, s.Start.Y),
                    new Vec2(s.End.X + xOffset, s.End.Y),
                    s.Weight);
                positioned.Add(new PositionedStroke(shifted, glyph.GridSize, sourceCharIndex, strokeOrdinal));
                strokeOrdinal++;
            }

            pen += glyph.AdvanceWidth;
            prev = glyph;
            boundaries.Add(pen);
        }

        return new CuneiformLine(positioned, pen, lineHeight, boundaries, sourceStart);
    }

    /// <summary>
    /// Gap between an ordered pair: the padding floor widened by kerning; kerning never narrows below the floor.
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
