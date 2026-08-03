namespace Scribe.Core.Cuneiform;

/// <summary>
/// One stroke of a laid-out line, positioned in the line's own grid-unit coordinate space (the pen
/// runs along x; y is the glyph's own grid y). Carries the source glyph's <see cref="GridSize"/> so a
/// renderer can scale grid units → pixels per glyph. Produced in AUTHORED CONSTRUCTION ORDER across the
/// whole line, so rendering the first N entries yields a partial reveal.
///
/// It also carries a STABLE IDENTITY — the source character index within the line
/// (<see cref="SourceCharIndex"/>) and the stroke's ordinal within its glyph (<see cref="StrokeOrdinal"/>)
/// — so a renderer can derive a per-stroke seed (for deterministic hand-written jitter) or a per-letter
/// reveal schedule that stays stable frame to frame, rather than keying off a frame counter. The identity
/// does not affect the emitted geometry or order in any way.
/// </summary>
public readonly struct PositionedStroke
{
    /// <summary>The stroke geometry, already offset into the line's coordinate space (grid units).</summary>
    public GlyphStroke Stroke { get; }

    /// <summary>The source glyph's em-grid size, for grid→pixel scaling.</summary>
    public double GridSize { get; }

    /// <summary>Index of the source character (within THIS line) this stroke belongs to — i.e. the same
    /// local index space as <see cref="CuneiformLine.CharBoundaries"/>. Stable identity for a per-stroke
    /// jitter seed and a per-letter reveal schedule; never affects geometry.</summary>
    public int SourceCharIndex { get; }

    /// <summary>This stroke's ordinal within its own glyph (0-based, in authored construction order).
    /// Combined with <see cref="SourceCharIndex"/> it uniquely and stably identifies a stroke within a
    /// line without depending on frame or wall-clock state.</summary>
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
/// The result of laying a string out: the positioned strokes (in construction order) plus the line's
/// total advance width and its height, all in grid units. <see cref="TotalWidth"/> is the pen position
/// after the last glyph; <see cref="LineHeight"/> is the em height a renderer sizes the line box to.
///
/// For editable text, the line also carries a <see cref="CharBoundaries"/> map — the cumulative pen
/// position (grid units) at each source-character boundary — so a caller can place a synthetic caret
/// and hit-test clicks against cuneiform (which has no native caret). <see cref="SourceStart"/> is the
/// index, in the ORIGINAL laid-out string, of this line's first character (0 for a single line;
/// non-zero for a wrapped continuation line), so a global character index maps to a line + local index.
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
    /// Length is the line's character count + 1: index 0 is the pen before the first character (always
    /// 0.0) and the last entry equals <see cref="TotalWidth"/>. Every source character advances the pen
    /// (a space by the word gap, a missing glyph by the missing-glyph gap, a glyph by its footprint plus
    /// its leading inter-glyph gap), so a caret index maps to a stable position even across spaces and
    /// unknown characters.</summary>
    public IReadOnlyList<double> CharBoundaries { get; }

    /// <summary>Index, in the original laid-out string, of this line's first character. 0 for a single
    /// (unwrapped) line; the source offset of the break for a wrapped continuation line.</summary>
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

    /// <summary>The pen position (grid units) of the caret sitting BEFORE the character at
    /// <paramref name="localCharIndex"/> within this line. The index is clamped to a valid boundary, so
    /// 0 returns the line start and the character count returns <see cref="TotalWidth"/>.</summary>
    public double CaretXAt(int localCharIndex)
    {
        if (CharBoundaries.Count == 0)
        {
            return 0.0;
        }

        int idx = Math.Clamp(localCharIndex, 0, CharBoundaries.Count - 1);
        return CharBoundaries[idx];
    }

    /// <summary>The local character-boundary index (0..character count) whose pen position is nearest to
    /// <paramref name="x"/> grid units — i.e. where a click at <paramref name="x"/> should place the
    /// caret. Ties resolve to the lower index.</summary>
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

    /// <summary>
    /// Many-to-one character alias map, applied at the SAME pre-lookup layer as uppercase-folding (after
    /// folding, before <see cref="GlyphBundle.Get"/>). It redirects a character with no authored glyph of
    /// its own onto an existing authored glyph it visually resembles, so more everyday characters render
    /// as real ink instead of the missing-glyph gap. Because the substitution happens before lookup, an
    /// aliased character produces byte-identical strokes and advance to its target (same glyph, same math).
    ///
    /// The map is pure DATA so entries are trivial to add/remove without touching layout logic. Entries are
    /// grouped by dependency:
    ///   • Ship-now (target already authored): the bracket/brace forms reuse the parenthesis glyphs.
    ///   • Was waits-on-art, now shippable: <c>&amp; → +</c>. The <c>+</c> glyph landed with the glyph-forge
    ///     symbol sync (the shipped bundle now carries + / = % # * @), so the ampersand alias resolves to a
    ///     real glyph today. If a target is ever absent, its alias simply falls through to the safe
    ///     missing-glyph gap (no crash), so an alias to an unauthored target is harmless but pointless.
    /// </summary>
    private static readonly IReadOnlyDictionary<char, char> Aliases = new Dictionary<char, char>
    {
        ['['] = '(',
        ['{'] = '(',
        [']'] = ')',
        ['}'] = ')',
        ['&'] = '+',
    };

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
    /// Lays <paramref name="text"/> out into a single <see cref="CuneiformLine"/>. Input is folded to
    /// uppercase before glyph lookup (the authored set is uppercase-only); spaces advance
    /// <see cref="WordGapUnits"/> with no strokes; a character with no authored glyph advances
    /// <see cref="MissingGlyphGapUnits"/> with no strokes (never throws). Strokes are emitted in authored
    /// construction order across the line. The returned line also carries a per-character advance map
    /// (<see cref="CuneiformLine.CharBoundaries"/>) for caret placement and click hit-testing.
    /// </summary>
    public CuneiformLine Layout(string text)
    {
        return LayoutSegment(text ?? string.Empty, 0);
    }

    /// <summary>
    /// Lays <paramref name="text"/> out into ONE OR MORE <see cref="CuneiformLine"/>s. A hard line break
    /// (<c>'\n'</c>, e.g. from Shift+Enter) ALWAYS splits into a new line — even when soft-wrap is
    /// disabled — so an edited row grows a line per paragraph. Within each paragraph the text
    /// soft-wraps at word-gap (space/tab) boundaries so that no line exceeds
    /// <paramref name="maxWidthGridUnits"/> grid units where a break point exists. This is the cuneiform
    /// analogue of the normal field's wrap (which likewise splits paragraphs on <c>'\n'</c> first, then
    /// word-wraps each).
    ///
    /// A word that is itself wider than the maximum occupies its own line rather than being split
    /// mid-glyph (layout never throws). A non-positive or infinite <paramref name="maxWidthGridUnits"/>
    /// disables SOFT wrapping (hard <c>'\n'</c> breaks still apply) and returns one line per paragraph,
    /// each identical to <see cref="Layout"/>. Each returned line's <see cref="CuneiformLine.SourceStart"/>
    /// is the index of its first character in <paramref name="text"/> (accounting for the <c>'\n'</c>
    /// separators consumed between paragraphs), and its <see cref="CuneiformLine.CharBoundaries"/> are
    /// local to that line.
    /// </summary>
    public IReadOnlyList<CuneiformLine> LayoutWrapped(string text, double maxWidthGridUnits)
    {
        string source = text ?? string.Empty;
        bool noSoftWrap = !(maxWidthGridUnits > 0.0) || double.IsInfinity(maxWidthGridUnits);

        // Split on hard line breaks first (like the normal field's WrapInto), tracking each paragraph's
        // global start so wrapped-line SourceStarts stay absolute. An empty paragraph (a bare '\n' or a
        // trailing '\n') yields one empty line, so a caret can sit on the new blank line.
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
    /// Soft-wraps a single paragraph (no interior <c>'\n'</c>) into <paramref name="outLines"/>, stamping
    /// each line's <see cref="CuneiformLine.SourceStart"/> with its ABSOLUTE index (<paramref name="globalStart"/>
    /// plus the in-paragraph offset). When <paramref name="noSoftWrap"/> is set, the whole paragraph is one
    /// line. The final line runs to the paragraph end so trailing whitespace is kept (task 8.3).
    /// </summary>
    private void WrapParagraph(
        string paragraph, int globalStart, double maxWidthGridUnits, bool noSoftWrap, List<CuneiformLine> outLines)
    {
        if (noSoftWrap)
        {
            outLines.Add(LayoutSegment(paragraph, globalStart));
            return;
        }

        // Tokenize into words — maximal runs of non-space/tab characters — recording each word's
        // half-open [start, end) range within the paragraph. Wrapping happens between words only (there
        // is no kerning across a space, so words lay out independently), so this is the natural unit.
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

        // Empty or whitespace-only paragraph: one line, keeping its whitespace so the caret can sit in it.
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
                // Overflow: flush the current line and start a new one with this word. An over-long
                // word (wider than max on its own) simply becomes a one-word line — never split.
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

        // The FINAL line runs to the end of the paragraph (not just the last word's end) so a trailing
        // run of spaces still advances the caret — a space typed at the very end must move the caret
        // immediately, not vanish until a following glyph forces a new word token. Interior break-spaces
        // between wrapped lines stay dropped (they are the wrap separators consumed above); only this
        // last line keeps its trailing whitespace.
        outLines.Add(LayoutSegment(paragraph.Substring(lineStart), globalStart + lineStart));
    }

    /// <summary>
    /// Lays a single segment out, recording the cumulative pen position at every source-character
    /// boundary. <paramref name="sourceStart"/> is stamped onto the result as
    /// <see cref="CuneiformLine.SourceStart"/> so wrapped lines can be mapped back to the original string.
    /// </summary>
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
            // Alias substitution: at the same pre-lookup layer as folding, redirect a character onto the
            // authored glyph it resembles (e.g. '[' → '('). Anything neither authored nor aliased still
            // falls through to the safe missing-glyph gap below.
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

            // Separate this glyph from the previous one by the hard padding floor, widened (never
            // narrowed) by any authored kerning for the ordered pair.
            if (prev is not null)
            {
                pen += GapBetween(prev, glyph);
            }

            // This glyph's source-character index within the line. boundaries starts as {0.0} and appends
            // exactly one entry per source character, so before this char's boundary is added its count
            // minus one is the current character index (0 for the first char, 1 for the second, …). Used as
            // stable identity on each emitted stroke — it never affects geometry.
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
