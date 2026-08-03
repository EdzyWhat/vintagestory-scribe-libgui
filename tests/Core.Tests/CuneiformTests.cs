using Scribe.Core.Cuneiform;

namespace Scribe.Core.Tests;

// Tests for the game-agnostic cuneiform glyph model: stroke corner geometry, the glyph-forge
// migration ladder, JSON bundle parsing, and the proportional line-layout engine (advance, padding
// floor, widening-only kerning, uppercase folding, word gap, and missing-glyph safety). All pure/BCL
// — no VS API, so this runs on CI with no game install.
public class CuneiformTests
{
    // ---- Stroke corner geometry -------------------------------------------------------------

    [Fact]
    public void Corners_DiagonalStroke_IsRotatedRectangleNotAabb()
    {
        // start(0,0) -> end(3,4), weight 10: len=5, half-weight=5, p=(-4,3). Clean integer corners.
        var stroke = new GlyphStroke(new Vec2(0, 0), new Vec2(3, 4), 10);

        Vec2[] c = stroke.Corners();

        // Order is start+p, end+p, end-p, start-p per the export format.
        AssertVec(new Vec2(-4, 3), c[0]);
        AssertVec(new Vec2(-1, 7), c[1]);
        AssertVec(new Vec2(7, 1), c[2]);
        AssertVec(new Vec2(4, -3), c[3]);

        // A rotated rectangle: no pair of opposite corners shares an axis (an AABB would).
        Assert.NotEqual(c[0].X, c[3].X, 6);
        Assert.NotEqual(c[0].Y, c[1].Y, 6);
    }

    [Fact]
    public void Corners_HorizontalStroke_HasVerticalThickness()
    {
        // A horizontal centerline's perpendicular is vertical: this stroke IS an axis-aligned rect.
        var stroke = new GlyphStroke(new Vec2(0, 0), new Vec2(10, 0), 4);

        Vec2[] c = stroke.Corners();

        AssertVec(new Vec2(0, 2), c[0]);
        AssertVec(new Vec2(10, 2), c[1]);
        AssertVec(new Vec2(10, -2), c[2]);
        AssertVec(new Vec2(0, -2), c[3]);
    }

    [Fact]
    public void Corners_ZeroLengthStroke_CollapsesWithoutNaN()
    {
        var stroke = new GlyphStroke(new Vec2(5, 5), new Vec2(5, 5), 8);

        Vec2[] c = stroke.Corners();

        foreach (Vec2 corner in c)
        {
            Assert.False(double.IsNaN(corner.X));
            Assert.False(double.IsNaN(corner.Y));
            AssertVec(new Vec2(5, 5), corner);
        }
    }

    // ---- Migration ladder (Glyph.FromRaw) ---------------------------------------------------

    [Fact]
    public void FromRaw_LeftRightWidthPresent_UsedAsIs()
    {
        Glyph g = Glyph.FromRaw('A', 100, leftWidth: 22, rightWidth: 24,
            width: null, advanceWidth: null, leftPadding: 1, rightPadding: 2,
            strokes: Array.Empty<GlyphStroke>());

        Assert.Equal(22, g.LeftWidth, 6);
        Assert.Equal(24, g.RightWidth, 6);
        Assert.Equal(46, g.AdvanceWidth, 6);
    }

    [Fact]
    public void FromRaw_WidthOnly_SplitsEvenly()
    {
        Glyph g = Glyph.FromRaw('A', 100, leftWidth: null, rightWidth: null,
            width: 40, advanceWidth: null, leftPadding: 3, rightPadding: 3,
            strokes: Array.Empty<GlyphStroke>());

        Assert.Equal(20, g.LeftWidth, 6);
        Assert.Equal(20, g.RightWidth, 6);
    }

    [Fact]
    public void FromRaw_AdvanceWidthOnly_SplitsEvenlyPaddingsZero()
    {
        Glyph g = Glyph.FromRaw('A', 100, leftWidth: null, rightWidth: null,
            width: null, advanceWidth: 50, leftPadding: null, rightPadding: null,
            strokes: Array.Empty<GlyphStroke>());

        Assert.Equal(25, g.LeftWidth, 6);
        Assert.Equal(25, g.RightWidth, 6);
        Assert.Equal(0, g.LeftPadding, 6);
        Assert.Equal(0, g.RightPadding, 6);
    }

    [Fact]
    public void FromRaw_StrokesOnly_DerivesWidthsFromBounds()
    {
        // Strokes span x 30..70 on a 100 grid (center 50): left = 50-30 = 20, right = 70-50 = 20.
        var strokes = new[]
        {
            new GlyphStroke(new Vec2(30, 10), new Vec2(70, 90), 6),
        };

        Glyph g = Glyph.FromRaw('A', 100, leftWidth: null, rightWidth: null,
            width: null, advanceWidth: null, leftPadding: null, rightPadding: null, strokes: strokes);

        Assert.Equal(20, g.LeftWidth, 6);
        Assert.Equal(20, g.RightWidth, 6);
    }

    [Fact]
    public void FromRaw_StrokesEntirelyOneSide_ClampsEmptySideToZero()
    {
        // Strokes span x 60..80 on a 100 grid (center 50): left clamps to 0, right = 80-50 = 30.
        var strokes = new[]
        {
            new GlyphStroke(new Vec2(60, 10), new Vec2(80, 90), 6),
        };

        Glyph g = Glyph.FromRaw('A', 100, leftWidth: null, rightWidth: null,
            width: null, advanceWidth: null, leftPadding: null, rightPadding: null, strokes: strokes);

        Assert.Equal(0, g.LeftWidth, 6);
        Assert.Equal(30, g.RightWidth, 6);
    }

    [Fact]
    public void FromRaw_EmptyGlyph_UsesDefaultHalfWidth()
    {
        Glyph g = Glyph.FromRaw('A', 100, leftWidth: null, rightWidth: null,
            width: null, advanceWidth: null, leftPadding: null, rightPadding: null,
            strokes: Array.Empty<GlyphStroke>());

        Assert.Equal(Glyph.DefaultHalfWidth, g.LeftWidth, 6);
        Assert.Equal(Glyph.DefaultHalfWidth, g.RightWidth, 6);
    }

    // ---- Bundle parse -----------------------------------------------------------------------

    [Fact]
    public void Parse_ReadsCharacterCountStrokesAndWidths()
    {
        GlyphBundle bundle = GlyphBundle.Parse(SampleBundleJson);

        Assert.Equal(4, bundle.CharacterCount);
        Assert.True(bundle.Contains('A'));

        Glyph a = bundle.Get('A')!;
        Assert.Single(a.Strokes);
        Assert.Equal(20, a.LeftWidth, 6);
        Assert.Equal(20, a.RightWidth, 6);
        Assert.Equal(6, a.Strokes[0].Weight, 6);
    }

    [Fact]
    public void Parse_ShippedBundle_ContainsAllAuthoredCharacters()
    {
        // The real committed artifact (copied into the test output) must parse and carry the full
        // authored set: A–Z (26) + 0–9 (10) + 18 punctuation = 54 — no lowercase, no space glyph.
        // (The punctuation set grew from 11 to 18 with the glyph-forge symbol sync adding + / = % # * @.)
        string path = Path.Combine(AppContext.BaseDirectory, "cuneiform-glyphs-1.json");
        Assert.True(File.Exists(path), $"Shipped glyph bundle not found at {path}");

        GlyphBundle bundle = GlyphBundle.Parse(File.ReadAllText(path));

        Assert.Equal(54, bundle.CharacterCount);
        Assert.True(bundle.Contains('A'));
        Assert.True(bundle.Contains('Z'));
        Assert.True(bundle.Contains('0'));
        Assert.True(bundle.Contains('?'));
        Assert.True(bundle.Contains('+'), "the glyph-forge symbol sync added '+'");
        Assert.True(bundle.Contains('@'), "the glyph-forge symbol sync added '@'");
        Assert.False(bundle.Contains('a'), "authored set is uppercase-only");
        Assert.False(bundle.Contains(' '), "no space glyph is authored");

        // A known glyph has real ink (A is authored with 3 strokes in the source).
        Glyph a = bundle.Get('A')!;
        Assert.Equal(3, a.Strokes.Count);
    }

    [Fact]
    public void Parse_MissingCharactersObject_Throws()
    {
        Assert.ThrowsAny<Exception>(() => GlyphBundle.Parse("{\"generatedFrom\":\"x\"}"));
    }

    [Fact]
    public void Get_UnauthoredCharacter_ReturnsNull()
    {
        GlyphBundle bundle = GlyphBundle.Parse(SampleBundleJson);

        Assert.Null(bundle.Get('@'));
        Assert.False(bundle.Contains('@'));
    }

    // ---- Line layout: advance + padding + kerning -------------------------------------------

    [Fact]
    public void Layout_AdjacentGlyphs_AdvanceByFootprintPlusPaddingFloor()
    {
        // A: advance 40 (20+20), padding 5/5. "AA" = 40 + (5+5) + 40 = 90.
        CuneiformLine line = Layout("AA");

        Assert.Equal(90, line.TotalWidth, 6);
        Assert.Equal(2, line.Strokes.Count); // one stroke per A
    }

    [Fact]
    public void Layout_PositiveKerning_WidensGapBeyondFloor()
    {
        // X kerns +10 before Y: gap = floor(5+5) + 10 = 20. "XY" = 40 + 20 + 40 = 100.
        CuneiformLine line = Layout("XY");

        Assert.Equal(100, line.TotalWidth, 6);
    }

    [Fact]
    public void Layout_NegativeKerning_ClampsToPaddingFloor()
    {
        // Z kerns -100 before Y: gap clamps to the floor (5+5)=10, never narrower. "ZY" = 40 + 10 + 40 = 90.
        CuneiformLine line = Layout("ZY");

        Assert.Equal(90, line.TotalWidth, 6);
    }

    // ---- Line layout: folding, spaces, missing glyphs ---------------------------------------

    [Fact]
    public void Layout_LowercaseInput_UsesUppercaseGlyph()
    {
        CuneiformLine upper = Layout("A");
        CuneiformLine lower = Layout("a");

        Assert.Equal(upper.Strokes.Count, lower.Strokes.Count);
        Assert.Single(lower.Strokes);
        Assert.Equal(upper.TotalWidth, lower.TotalWidth, 6);
    }

    [Fact]
    public void Layout_Space_AdvancesWordGapWithNoStrokes()
    {
        var layout = new CuneiformLineLayout(GlyphBundle.Parse(SampleBundleJson));

        CuneiformLine line = layout.Layout(" ");

        Assert.Empty(line.Strokes);
        Assert.Equal(layout.WordGapUnits, line.TotalWidth, 6);
    }

    [Fact]
    public void Layout_UnauthoredCharacter_AdvancesSmallGapWithoutThrowing()
    {
        var layout = new CuneiformLineLayout(GlyphBundle.Parse(SampleBundleJson));

        CuneiformLine line = layout.Layout("@");

        Assert.Empty(line.Strokes);
        Assert.Equal(layout.MissingGlyphGapUnits, line.TotalWidth, 6);
    }

    [Fact]
    public void Layout_EmptyString_ProducesEmptyLine()
    {
        CuneiformLine line = Layout("");

        Assert.Empty(line.Strokes);
        Assert.Equal(0, line.TotalWidth, 6);
    }

    [Fact]
    public void Layout_StrokeOrder_IsPreservedAcrossLineForReveal()
    {
        // "AA": the first stroke belongs to the first A (offset less), the second to the second A.
        CuneiformLine line = Layout("AA");

        Assert.Equal(2, line.Strokes.Count);
        Assert.True(line.Strokes[0].Stroke.Start.X < line.Strokes[1].Stroke.Start.X);
    }

    // ---- Char-boundary map (caret placement + click hit-testing) ----------------------------

    [Fact]
    public void CharBoundaries_LengthIsTextLengthPlusOne_StartsAtZeroEndsAtTotalWidth()
    {
        // "AA" = 90 wide, boundaries at 0 (before first A), 40 (between), 90 (after second A).
        CuneiformLine line = Layout("AA");

        Assert.Equal(3, line.CharBoundaries.Count);
        Assert.Equal(0, line.CharBoundaries[0], 6);
        Assert.Equal(40, line.CharBoundaries[1], 6);
        Assert.Equal(90, line.CharBoundaries[2], 6);
        Assert.Equal(line.TotalWidth, line.CharBoundaries[^1], 6);
    }

    [Fact]
    public void CaretXAt_ReturnsBoundaryAndClampsOutOfRange()
    {
        CuneiformLine line = Layout("AA");

        Assert.Equal(0, line.CaretXAt(0), 6);
        Assert.Equal(40, line.CaretXAt(1), 6);
        Assert.Equal(90, line.CaretXAt(2), 6);
        // Clamped both ways rather than throwing.
        Assert.Equal(0, line.CaretXAt(-5), 6);
        Assert.Equal(90, line.CaretXAt(99), 6);
    }

    [Fact]
    public void CharBoundaries_UppercaseFold_MatchesUppercaseLayout()
    {
        // Folded input must produce the SAME boundary map as its uppercase form (indices stay stable).
        CuneiformLine upper = Layout("XY");
        CuneiformLine lower = Layout("xy");

        Assert.Equal(upper.CharBoundaries.Count, lower.CharBoundaries.Count);
        for (int i = 0; i < upper.CharBoundaries.Count; i++)
        {
            Assert.Equal(upper.CharBoundaries[i], lower.CharBoundaries[i], 6);
        }
    }

    [Fact]
    public void CharBoundaries_SpacesAndMissingGlyphs_StayMonotonicWithOneEntryPerChar()
    {
        var layout = new CuneiformLineLayout(GlyphBundle.Parse(SampleBundleJson));

        // "A @A": glyph A, space, missing '@', glyph A — every source char advances the pen.
        CuneiformLine line = layout.Layout("A @A");

        Assert.Equal(5, line.CharBoundaries.Count); // 4 chars + 1
        Assert.Equal(0, line.CharBoundaries[0], 6);
        Assert.Equal(40, line.CharBoundaries[1], 6);                                    // after A
        Assert.Equal(40 + layout.WordGapUnits, line.CharBoundaries[2], 6);              // after space
        Assert.Equal(40 + layout.WordGapUnits + layout.MissingGlyphGapUnits,
            line.CharBoundaries[3], 6);                                                 // after '@'
        // Strictly increasing (no zero-width step even across space + missing glyph).
        for (int i = 1; i < line.CharBoundaries.Count; i++)
        {
            Assert.True(line.CharBoundaries[i] > line.CharBoundaries[i - 1]);
        }
    }

    [Fact]
    public void NearestBoundary_ResolvesClicksToNearestIndex_TiesGoLower()
    {
        // "AA": boundaries at 0, 40, 90.
        CuneiformLine line = Layout("AA");

        Assert.Equal(0, line.NearestBoundary(-10)); // left of start clamps to 0
        Assert.Equal(0, line.NearestBoundary(15));  // nearer 0 than 40
        Assert.Equal(1, line.NearestBoundary(30));  // nearer 40 than 0
        Assert.Equal(0, line.NearestBoundary(20));  // exactly between 0 and 40 → tie resolves to lower index
        Assert.Equal(2, line.NearestBoundary(200)); // right of end clamps to last
    }

    [Fact]
    public void CharBoundaries_EmptyString_HasSingleZeroBoundary()
    {
        CuneiformLine line = Layout("");

        Assert.Single(line.CharBoundaries);
        Assert.Equal(0, line.CharBoundaries[0], 6);
        Assert.Equal(0, line.CaretXAt(0), 6);
        Assert.Equal(0, line.NearestBoundary(50));
    }

    // ---- Wrap (soft-wrap at word boundaries) ------------------------------------------------

    [Fact]
    public void LayoutWrapped_NoMaxWidth_ProducesOneLineIdenticalToSingleLine()
    {
        var layout = new CuneiformLineLayout(GlyphBundle.Parse(SampleBundleJson));

        CuneiformLine single = layout.Layout("AA AA");
        IReadOnlyList<CuneiformLine> wrapped = layout.LayoutWrapped("AA AA", 0.0);

        Assert.Single(wrapped);
        Assert.Equal(single.TotalWidth, wrapped[0].TotalWidth, 6);
        Assert.Equal(single.Strokes.Count, wrapped[0].Strokes.Count);
        Assert.Equal(0, wrapped[0].SourceStart);
    }

    [Fact]
    public void LayoutWrapped_InfiniteMaxWidth_ProducesOneLine()
    {
        var layout = new CuneiformLineLayout(GlyphBundle.Parse(SampleBundleJson));

        IReadOnlyList<CuneiformLine> wrapped = layout.LayoutWrapped("AA AA", double.PositiveInfinity);

        Assert.Single(wrapped);
    }

    [Fact]
    public void LayoutWrapped_LongString_BreaksAtWordBoundaries()
    {
        // Each "AA" word is 90 wide; word gap 45. "AA AA AA" on a single line = 90+45+90+45+90 = 360.
        // Max width 200 forces a break after the first (or second) word.
        var layout = new CuneiformLineLayout(GlyphBundle.Parse(SampleBundleJson));

        IReadOnlyList<CuneiformLine> lines = layout.LayoutWrapped("AA AA AA", 200.0);

        Assert.True(lines.Count >= 2, "a 360-wide string must wrap under a 200 max");
        // No line exceeds the max (every word is 90 < 200, so a break point always exists).
        foreach (CuneiformLine l in lines)
        {
            Assert.True(l.TotalWidth <= 200.0 + 1e-6, $"line width {l.TotalWidth} exceeds max");
        }
        // The second line resumes at the source index just past the break space.
        const string source = "AA AA AA";
        Assert.True(lines[1].SourceStart > 0);
        Assert.Equal(' ', source[lines[1].SourceStart - 1]); // char before the resume point is the break space
    }

    [Fact]
    public void LayoutWrapped_OverlongWord_OccupiesItsOwnLineWithoutThrowing()
    {
        // A single word (no interior space) far wider than the max: must not throw and must not split.
        var layout = new CuneiformLineLayout(GlyphBundle.Parse(SampleBundleJson));

        IReadOnlyList<CuneiformLine> lines = layout.LayoutWrapped("AAAAAAAA", 50.0);

        Assert.Single(lines);
        Assert.True(lines[0].TotalWidth > 50.0); // occupies its own line, over-width, not mid-glyph split
    }

    [Fact]
    public void LayoutWrapped_EmptyAndWhitespace_DoNotThrow()
    {
        var layout = new CuneiformLineLayout(GlyphBundle.Parse(SampleBundleJson));

        Assert.Single(layout.LayoutWrapped("", 100.0));
        Assert.Single(layout.LayoutWrapped("   ", 100.0));
    }

    // ---- Trailing-space caret advance (task 8.3) --------------------------------------------

    [Fact]
    public void Layout_TrailingSpace_AdvancesCaretImmediately()
    {
        // A single-line layout already counts every source char: "A " has a boundary past the glyph
        // AND past the trailing space, so a caret at the end sits WordGapUnits beyond the A.
        var layout = new CuneiformLineLayout(GlyphBundle.Parse(SampleBundleJson));

        CuneiformLine line = layout.Layout("A ");

        Assert.Equal(3, line.CharBoundaries.Count); // 'A', ' ', +1
        Assert.Equal(40, line.CharBoundaries[1], 6);                        // after A
        Assert.Equal(40 + layout.WordGapUnits, line.CharBoundaries[2], 6);  // after trailing space
        Assert.Equal(40 + layout.WordGapUnits, line.CaretXAt(2), 6);        // caret past the space
    }

    [Fact]
    public void Layout_ConsecutiveTrailingSpaces_EachAdvanceTheCaret()
    {
        var layout = new CuneiformLineLayout(GlyphBundle.Parse(SampleBundleJson));

        CuneiformLine line = layout.Layout("A   ");

        Assert.Equal(5, line.CharBoundaries.Count); // 'A' + three spaces + 1
        Assert.Equal(40 + layout.WordGapUnits * 3, line.CharBoundaries[^1], 6);
        // Strictly increasing — every trailing space moves the caret, none collapse to zero width.
        for (int i = 1; i < line.CharBoundaries.Count; i++)
        {
            Assert.True(line.CharBoundaries[i] > line.CharBoundaries[i - 1]);
        }
    }

    [Fact]
    public void LayoutWrapped_TrailingSpaceOnFinalLine_IsKeptSoCaretCanAdvance()
    {
        // The wrap path tokenizes into words and would otherwise flush the final line at the last WORD's
        // end, dropping trailing spaces so a space typed at the very end of a row never moved the caret.
        // The final line must now run to the source end and keep its trailing whitespace.
        var layout = new CuneiformLineLayout(GlyphBundle.Parse(SampleBundleJson));

        IReadOnlyList<CuneiformLine> lines = layout.LayoutWrapped("AA ", 500.0);

        Assert.Single(lines); // fits on one line
        CuneiformLine last = lines[^1];
        // "AA" is 90 wide; the trailing space adds one WordGapUnits and its own boundary entry.
        Assert.Equal(4, last.CharBoundaries.Count); // 'A','A',' ', +1
        Assert.Equal(90 + layout.WordGapUnits, last.TotalWidth, 6);
        Assert.Equal(90 + layout.WordGapUnits, last.CaretXAt(3), 6); // caret sits past the trailing space
    }

    [Fact]
    public void LayoutWrapped_InteriorBreakSpacesStayDropped_OnlyFinalLineKeepsTrailing()
    {
        // Regression guard for the trailing-space fix: the space that CAUSES a wrap is still consumed
        // as a separator (not re-added to either line), so only the genuine trailing space survives.
        // "AA AA " wraps after the first word under a tight max; line 0 ends at the first "AA" (no
        // trailing gap), and the final line keeps its one trailing space.
        var layout = new CuneiformLineLayout(GlyphBundle.Parse(SampleBundleJson));

        IReadOnlyList<CuneiformLine> lines = layout.LayoutWrapped("AA AA ", 100.0);

        Assert.True(lines.Count >= 2, "a 225-wide string must wrap under a 100 max");
        // First line is exactly the first word — the break space is dropped, not trailing.
        Assert.Equal(90, lines[0].TotalWidth, 6);
        // Final line keeps its trailing space (word 90 + one word gap).
        Assert.Equal(90 + layout.WordGapUnits, lines[^1].TotalWidth, 6);
    }

    // ---- Hard line breaks ('\n' / Shift+Enter, task 8.2) ------------------------------------

    [Fact]
    public void LayoutWrapped_HardNewline_SplitsIntoLinesEvenWithoutSoftWrap()
    {
        // A '\n' (Shift+Enter in a row) must always start a new line, even when soft-wrap is disabled.
        var layout = new CuneiformLineLayout(GlyphBundle.Parse(SampleBundleJson));

        IReadOnlyList<CuneiformLine> lines = layout.LayoutWrapped("AA\nAA", 0.0);

        Assert.Equal(2, lines.Count);
        Assert.Equal(0, lines[0].SourceStart);
        Assert.Equal(3, lines[1].SourceStart); // past "AA" (2) + the consumed '\n' (1)
        Assert.Equal(90, lines[0].TotalWidth, 6);
        Assert.Equal(90, lines[1].TotalWidth, 6);
    }

    [Fact]
    public void LayoutWrapped_BlankParagraph_YieldsAnEmptyLineForTheCaret()
    {
        // A bare/consecutive '\n' produces an empty line so a caret can rest on the new blank line.
        var layout = new CuneiformLineLayout(GlyphBundle.Parse(SampleBundleJson));

        IReadOnlyList<CuneiformLine> lines = layout.LayoutWrapped("A\n\nA", 0.0);

        Assert.Equal(3, lines.Count);
        Assert.Empty(lines[1].Strokes);        // the blank middle line
        Assert.Equal(0, lines[1].TotalWidth, 6);
        Assert.Equal(2, lines[1].SourceStart); // 'A' (1) + '\n' (1)
        Assert.Equal(3, lines[2].SourceStart); // + the blank line's '\n'
    }

    [Fact]
    public void LayoutWrapped_HardNewlineThenSoftWrap_BothApply()
    {
        // Each paragraph independently soft-wraps: "AA AA\nAA" with a tight max wraps the first
        // paragraph AND keeps the '\n' split, so SourceStarts stay absolute across the newline.
        var layout = new CuneiformLineLayout(GlyphBundle.Parse(SampleBundleJson));

        IReadOnlyList<CuneiformLine> lines = layout.LayoutWrapped("AA AA\nAA", 100.0);

        Assert.True(lines.Count >= 3, "first paragraph wraps to 2 lines + the 3rd after '\\n'");
        // The last line is the post-newline "AA": its source start is past "AA AA\n" (= index 6).
        CuneiformLine last = lines[^1];
        Assert.Equal(6, last.SourceStart);
        Assert.Equal(90, last.TotalWidth, 6);
    }

    // ---- Character-coverage alias map (add-cuneiform-character-coverage) ---------------------

    [Fact]
    public void Layout_BracketAliases_RenderIdenticallyToOpenParen()
    {
        // '[' and '{' alias to the authored '(' at the pre-lookup layer, so each lays out to byte-identical
        // strokes and advance as '(' — real ink, not the missing-glyph gap. Uses the shipped bundle (the
        // sample bundle has no '(' glyph); '(' is one of the 47 original authored punctuation marks.
        var layout = ShippedLayout();

        AssertSameLayout(layout.Layout("("), layout.Layout("["));
        AssertSameLayout(layout.Layout("("), layout.Layout("{"));
    }

    [Fact]
    public void Layout_BraceAndBracketCloseAliases_RenderIdenticallyToCloseParen()
    {
        // ']' and '}' alias to the authored ')'.
        var layout = ShippedLayout();

        AssertSameLayout(layout.Layout(")"), layout.Layout("]"));
        AssertSameLayout(layout.Layout(")"), layout.Layout("}"));
    }

    [Fact]
    public void Layout_AmpersandAlias_RendersIdenticallyToPlus()
    {
        // '&' aliases to '+'. The '+' glyph landed with the glyph-forge symbol sync (the shipped bundle now
        // carries + / = % # * @), so this alias resolves to a real glyph today rather than falling through.
        var layout = ShippedLayout();
        Assert.True(GlyphBundle.Parse(ShippedBundleJson()).Contains('+'), "the '&' alias requires an authored '+'");

        AssertSameLayout(layout.Layout("+"), layout.Layout("&"));
    }

    [Fact]
    public void Layout_UnaliasedUnauthoredCharacter_StillDegradesToMissingGap()
    {
        // Guards the fall-through: a character that is neither authored nor aliased (e.g. '~') still advances
        // the small missing-glyph gap with no strokes and no throw — the alias step must not disturb this.
        var layout = new CuneiformLineLayout(GlyphBundle.Parse(SampleBundleJson));

        CuneiformLine line = layout.Layout("~");

        Assert.Empty(line.Strokes);
        Assert.Equal(layout.MissingGlyphGapUnits, line.TotalWidth, 6);
    }

    [Fact]
    public void ShippedBundle_ContainsAliasTargets()
    {
        // The shippable aliases must point at glyphs the bundle actually carries, so they render real ink
        // from the moment they ship: '(' and ')' (bracket/brace targets) and '+' (the '&' target).
        GlyphBundle bundle = GlyphBundle.Parse(ShippedBundleJson());

        Assert.True(bundle.Contains('('), "'[' '{' alias to '('");
        Assert.True(bundle.Contains(')'), "']' '}' alias to ')'");
        Assert.True(bundle.Contains('+'), "'&' aliases to '+'");
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private static CuneiformLine Layout(string text) =>
        new CuneiformLineLayout(GlyphBundle.Parse(SampleBundleJson)).Layout(text);

    private static string ShippedBundleJson()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "cuneiform-glyphs-1.json");
        Assert.True(File.Exists(path), $"Shipped glyph bundle not found at {path}");
        return File.ReadAllText(path);
    }

    private static CuneiformLineLayout ShippedLayout() =>
        new CuneiformLineLayout(GlyphBundle.Parse(ShippedBundleJson()));

    /// <summary>Assert two laid-out lines are byte-identical in advance width and positioned strokes —
    /// the contract for an aliased character rendering exactly as its target glyph.</summary>
    private static void AssertSameLayout(CuneiformLine expected, CuneiformLine actual)
    {
        Assert.Equal(expected.TotalWidth, actual.TotalWidth, 6);
        Assert.Equal(expected.Strokes.Count, actual.Strokes.Count);
        for (int i = 0; i < expected.Strokes.Count; i++)
        {
            Vec2[] e = expected.Strokes[i].Stroke.Corners();
            Vec2[] a = actual.Strokes[i].Stroke.Corners();
            for (int c = 0; c < e.Length; c++)
            {
                Assert.Equal(e[c].X, a[c].X, 6);
                Assert.Equal(e[c].Y, a[c].Y, 6);
            }
        }
    }

    private static void AssertVec(Vec2 expected, Vec2 actual)
    {
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
    }

    // ---- Stroke identity threaded through layout (add-cuneiform-handwriting-feel task 1) ------

    [Fact]
    public void Layout_PositionedStroke_CarriesSourceCharIndexAndOrdinal()
    {
        // "AA X A": each 'A' has one stroke; the space and the (missing-glyph) gap emit none. The
        // SourceCharIndex must be the LOCAL character index of each stroke's source glyph, and the
        // StrokeOrdinal 0 (single-stroke glyphs). 'X' also has one stroke.
        var layout = new CuneiformLineLayout(GlyphBundle.Parse(SampleBundleJson));

        CuneiformLine line = layout.Layout("AA X A");

        // Strokes come from indices: 0 (A), 1 (A), 3 (X); indices 2 (space) and 4 (space) emit nothing,
        // index 5 (A) emits one. So the emitted strokes map to source chars 0, 1, 3, 5.
        int[] expectedChars = { 0, 1, 3, 5 };
        Assert.Equal(expectedChars.Length, line.Strokes.Count);
        for (int i = 0; i < expectedChars.Length; i++)
        {
            Assert.Equal(expectedChars[i], line.Strokes[i].SourceCharIndex);
            Assert.Equal(0, line.Strokes[i].StrokeOrdinal); // every sample glyph has a single stroke
        }
    }

    [Fact]
    public void Layout_StrokeOrdinal_CountsWithinGlyph()
    {
        // A glyph with three strokes: the ordinals must be 0,1,2 in authored construction order, and the
        // single character's SourceCharIndex must be 0 for all three.
        const string threeStrokeBundle = """
            {
              "generatedFrom": "test", "characterCount": 1,
              "characters": {
                "A": { "character": "A", "gridSize": 100, "leftWidth": 20, "rightWidth": 20,
                       "leftPadding": 5, "rightPadding": 5,
                       "strokes": [
                         { "start": { "x": 0, "y": 0 }, "end": { "x": 10, "y": 0 }, "weight": 4 },
                         { "start": { "x": 0, "y": 5 }, "end": { "x": 10, "y": 5 }, "weight": 4 },
                         { "start": { "x": 0, "y": 9 }, "end": { "x": 10, "y": 9 }, "weight": 4 }
                       ] }
              }
            }
            """;
        var layout = new CuneiformLineLayout(GlyphBundle.Parse(threeStrokeBundle));

        CuneiformLine line = layout.Layout("A");

        Assert.Equal(3, line.Strokes.Count);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(0, line.Strokes[i].SourceCharIndex);
            Assert.Equal(i, line.Strokes[i].StrokeOrdinal);
        }
    }

    // ---- Deterministic jitter transform (add-cuneiform-handwriting-feel task 2) ---------------

    [Fact]
    public void Jitter_StrengthZero_IsIdentity()
    {
        var stroke = new GlyphStroke(new Vec2(10, 20), new Vec2(40, 55), 8);

        GlyphStroke result = GlyphStrokeJitter.Jitter(stroke, seed: 12345, strength: 0.0, gridSize: 100);

        AssertVec(stroke.Start, result.Start);
        AssertVec(stroke.End, result.End);
        Assert.Equal(stroke.Weight, result.Weight, 6);
    }

    [Fact]
    public void Jitter_NegativeStrength_IsIdentity()
    {
        var stroke = new GlyphStroke(new Vec2(10, 20), new Vec2(40, 55), 8);

        GlyphStroke result = GlyphStrokeJitter.Jitter(stroke, seed: 7, strength: -0.5, gridSize: 100);

        AssertVec(stroke.Start, result.Start);
        AssertVec(stroke.End, result.End);
        Assert.Equal(stroke.Weight, result.Weight, 6);
    }

    [Fact]
    public void Jitter_SameInputs_AreReproducible()
    {
        var stroke = new GlyphStroke(new Vec2(10, 20), new Vec2(40, 55), 8);

        GlyphStroke a = GlyphStrokeJitter.Jitter(stroke, seed: 99, strength: 0.6, gridSize: 100);
        GlyphStroke b = GlyphStrokeJitter.Jitter(stroke, seed: 99, strength: 0.6, gridSize: 100);

        AssertVec(a.Start, b.Start);
        AssertVec(a.End, b.End);
        Assert.Equal(a.Weight, b.Weight, 6);
    }

    [Fact]
    public void Jitter_DifferentSeeds_Diverge()
    {
        var stroke = new GlyphStroke(new Vec2(10, 20), new Vec2(40, 55), 8);

        GlyphStroke a = GlyphStrokeJitter.Jitter(stroke, seed: 1, strength: 0.6, gridSize: 100);
        GlyphStroke b = GlyphStrokeJitter.Jitter(stroke, seed: 2, strength: 0.6, gridSize: 100);

        bool differs = Math.Abs(a.Start.X - b.Start.X) > 1e-9
            || Math.Abs(a.Start.Y - b.Start.Y) > 1e-9
            || Math.Abs(a.End.X - b.End.X) > 1e-9
            || Math.Abs(a.End.Y - b.End.Y) > 1e-9
            || Math.Abs(a.Weight - b.Weight) > 1e-9;
        Assert.True(differs, "different seeds should perturb the stroke differently");
    }

    [Fact]
    public void Jitter_Displacement_StaysWithinBounds()
    {
        var stroke = new GlyphStroke(new Vec2(10, 20), new Vec2(40, 55), 8);
        const double gridSize = 100;
        const double strength = 1.0;
        double maxOffset = GlyphStrokeJitter.MaxPositionFraction * strength * gridSize;
        double maxWeight = GlyphStrokeJitter.MaxWeightFraction * strength;

        // Sweep many seeds; every result must stay inside the strength-bounded envelope.
        for (int seed = 0; seed < 500; seed++)
        {
            GlyphStroke r = GlyphStrokeJitter.Jitter(stroke, seed, strength, gridSize);

            Assert.True(Math.Abs(r.Start.X - stroke.Start.X) <= maxOffset + 1e-9);
            Assert.True(Math.Abs(r.Start.Y - stroke.Start.Y) <= maxOffset + 1e-9);
            Assert.True(Math.Abs(r.End.X - stroke.End.X) <= maxOffset + 1e-9);
            Assert.True(Math.Abs(r.End.Y - stroke.End.Y) <= maxOffset + 1e-9);

            Assert.True(r.Weight >= stroke.Weight * (1 - maxWeight) - 1e-9);
            Assert.True(r.Weight <= stroke.Weight * (1 + maxWeight) + 1e-9);
            Assert.True(r.Weight >= 0.0);
        }
    }

    [Fact]
    public void Jitter_ScalesWithGridSize()
    {
        // Position jitter is a fraction of grid size, so a larger grid permits a proportionally larger
        // maximum displacement. Verify the observed max over a seed sweep tracks grid size.
        var stroke = new GlyphStroke(new Vec2(50, 50), new Vec2(60, 60), 8);

        double MaxDx(double gridSize)
        {
            double max = 0;
            for (int seed = 0; seed < 300; seed++)
            {
                GlyphStroke r = GlyphStrokeJitter.Jitter(stroke, seed, strength: 1.0, gridSize: gridSize);
                max = Math.Max(max, Math.Abs(r.Start.X - stroke.Start.X));
            }
            return max;
        }

        Assert.True(MaxDx(200) > MaxDx(100), "larger grid size should allow larger position jitter");
    }

    [Fact]
    public void SeedFor_IsDeterministic()
    {
        Assert.Equal(
            GlyphStrokeJitter.SeedFor(12345, 3, 2),
            GlyphStrokeJitter.SeedFor(12345, 3, 2));
    }

    [Fact]
    public void SeedFor_DistinctIdentitiesDiverge()
    {
        // Adjacent strokes within one glyph, adjacent glyphs, and different base seeds must all produce
        // distinct seeds — the whole point of mixing identity in (no shimmer across strokes that happen to
        // sit at neighbouring indices).
        int baseSeed = 777;
        var seeds = new System.Collections.Generic.HashSet<int>();
        for (int ch = 0; ch < 8; ch++)
        {
            for (int ord = 0; ord < 8; ord++)
            {
                Assert.True(seeds.Add(GlyphStrokeJitter.SeedFor(baseSeed, ch, ord)),
                    $"collision at char {ch}, ordinal {ord}");
            }
        }

        // A different base seed shifts the whole family.
        Assert.NotEqual(GlyphStrokeJitter.SeedFor(1, 0, 0), GlyphStrokeJitter.SeedFor(2, 0, 0));
    }

    [Fact]
    public void Jitter_DoesNotAlterLayoutMetrics()
    {
        // The renderer applies jitter to a COPY of each stroke at paint time; layout must be untouched.
        // Prove it here at the Core level: laying the same text out is byte-identical regardless of any
        // jitter a caller might apply downstream (the layout never calls Jitter), and jittering a stroke
        // leaves the ORIGINAL PositionedStroke — the one layout/caret/hit-testing read — unchanged.
        var layout = new CuneiformLineLayout(GlyphBundle.Parse(SampleBundleJson));
        CuneiformLine line = layout.Layout("AXA");
        double widthBefore = line.TotalWidth;
        var boundariesBefore = new System.Collections.Generic.List<double>(line.CharBoundaries);

        foreach (PositionedStroke ps in line.Strokes)
        {
            Vec2 sourceStartBefore = ps.Stroke.Start;
            GlyphStroke jittered = GlyphStrokeJitter.Jitter(
                ps.Stroke, GlyphStrokeJitter.SeedFor(42, ps.SourceCharIndex, ps.StrokeOrdinal),
                strength: 1.0, gridSize: ps.GridSize);
            // Jitter returns a NEW stroke; the source stroke that layout/caret/hit-testing read is unchanged.
            AssertVec(sourceStartBefore, ps.Stroke.Start);
            _ = jittered;
        }

        Assert.Equal(widthBefore, line.TotalWidth);
        Assert.Equal(boundariesBefore.Count, line.CharBoundaries.Count);
        for (int i = 0; i < boundariesBefore.Count; i++)
        {
            Assert.Equal(boundariesBefore[i], line.CharBoundaries[i], 9);
        }
    }

    // ---- Deterministic whole-character rotation transform (tune-tablet-jitter-add-rotation task 2) ------

    [Fact]
    public void Rotate_MaxDegreesZero_IsIdentity()
    {
        var stroke = new GlyphStroke(new Vec2(10, 20), new Vec2(40, 55), 8);
        var pivot = new Vec2(25, 50);

        GlyphStroke result = GlyphStrokeRotation.Rotate(stroke, pivot, seed: 12345, maxDegrees: 0.0);

        AssertVec(stroke.Start, result.Start);
        AssertVec(stroke.End, result.End);
        Assert.Equal(stroke.Weight, result.Weight, 6);
    }

    [Fact]
    public void Rotate_NegativeMaxDegrees_IsIdentity()
    {
        var stroke = new GlyphStroke(new Vec2(10, 20), new Vec2(40, 55), 8);
        var pivot = new Vec2(25, 50);

        GlyphStroke result = GlyphStrokeRotation.Rotate(stroke, pivot, seed: 7, maxDegrees: -8.0);

        AssertVec(stroke.Start, result.Start);
        AssertVec(stroke.End, result.End);
        Assert.Equal(stroke.Weight, result.Weight, 6);
    }

    [Fact]
    public void Rotate_SameInputs_AreReproducible()
    {
        var stroke = new GlyphStroke(new Vec2(10, 20), new Vec2(40, 55), 8);
        var pivot = new Vec2(25, 50);

        GlyphStroke a = GlyphStrokeRotation.Rotate(stroke, pivot, seed: 99, maxDegrees: 8.0);
        GlyphStroke b = GlyphStrokeRotation.Rotate(stroke, pivot, seed: 99, maxDegrees: 8.0);

        AssertVec(a.Start, b.Start);
        AssertVec(a.End, b.End);
        Assert.Equal(a.Weight, b.Weight, 6);
    }

    [Fact]
    public void Rotate_PreservesWeightAndLength()
    {
        // A rigid rotation moves the endpoints but changes neither the stroke width nor the distance
        // between the endpoints — it is an isometry.
        var stroke = new GlyphStroke(new Vec2(10, 20), new Vec2(40, 55), 8);
        var pivot = new Vec2(25, 50);
        double lenBefore = Dist(stroke.Start, stroke.End);

        GlyphStroke r = GlyphStrokeRotation.Rotate(stroke, pivot, seed: 3, maxDegrees: 8.0);

        Assert.Equal(stroke.Weight, r.Weight, 9);
        Assert.Equal(lenBefore, Dist(r.Start, r.End), 6);
    }

    [Fact]
    public void Rotate_AllStrokesOfOneCharacter_ShareAngleAndPivot()
    {
        // The whole point of omitting the stroke ordinal from the seed: every stroke of one character
        // tilts by the same angle about the same pivot, so the glyph rotates as a rigid unit (no shear).
        // Rotating two different strokes with the same seed+pivot must apply the identical transform,
        // which we verify by checking the rotation angle recovered from each is equal.
        var pivot = new Vec2(50, 50);
        int seed = GlyphStrokeRotation.SeedFor(baseSeed: 42, sourceCharIndex: 3);

        var s1 = new GlyphStroke(new Vec2(10, 10), new Vec2(90, 10), 4);
        var s2 = new GlyphStroke(new Vec2(30, 70), new Vec2(70, 20), 4);

        GlyphStroke r1 = GlyphStrokeRotation.Rotate(s1, pivot, seed, maxDegrees: 8.0);
        GlyphStroke r2 = GlyphStrokeRotation.Rotate(s2, pivot, seed, maxDegrees: 8.0);

        double angle1 = AngleAbout(s1.Start, r1.Start, pivot);
        double angle2 = AngleAbout(s2.Start, r2.Start, pivot);
        Assert.Equal(angle1, angle2, 9);
    }

    [Fact]
    public void Rotate_AngleStaysWithinBounds()
    {
        // Sweep many seeds; the recovered rotation angle must always lie inside [-max, +max].
        var stroke = new GlyphStroke(new Vec2(10, 20), new Vec2(90, 20), 4);
        var pivot = new Vec2(50, 50);
        const double maxDegrees = 8.0;
        double maxRad = maxDegrees * Math.PI / 180.0;

        for (int seed = 0; seed < 500; seed++)
        {
            GlyphStroke r = GlyphStrokeRotation.Rotate(stroke, pivot, seed, maxDegrees);
            double angle = AngleAbout(stroke.Start, r.Start, pivot);
            Assert.True(Math.Abs(angle) <= maxRad + 1e-9, $"angle {angle} exceeded ±{maxRad} at seed {seed}");
        }
    }

    [Fact]
    public void RotationSeedFor_IsDeterministic()
    {
        Assert.Equal(
            GlyphStrokeRotation.SeedFor(12345, 3),
            GlyphStrokeRotation.SeedFor(12345, 3));
    }

    [Fact]
    public void RotationSeedFor_DistinctCharactersDiverge()
    {
        // Adjacent characters (and repeated copies of one character at different source indices) must
        // seed distinctly, so no two neighbouring glyphs share the same tilt by construction.
        int baseSeed = 777;
        var seeds = new System.Collections.Generic.HashSet<int>();
        for (int ch = 0; ch < 32; ch++)
        {
            Assert.True(seeds.Add(GlyphStrokeRotation.SeedFor(baseSeed, ch)), $"collision at char {ch}");
        }

        // A different base seed shifts the whole family.
        Assert.NotEqual(GlyphStrokeRotation.SeedFor(1, 0), GlyphStrokeRotation.SeedFor(2, 0));
    }

    private static double Dist(Vec2 a, Vec2 b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>The signed angle (radians) that rotates the vector (pivot→before) onto (pivot→after).</summary>
    private static double AngleAbout(Vec2 before, Vec2 after, Vec2 pivot)
    {
        double a1 = Math.Atan2(before.Y - pivot.Y, before.X - pivot.X);
        double a2 = Math.Atan2(after.Y - pivot.Y, after.X - pivot.X);
        double d = a2 - a1;
        // Normalize to (-π, π] so small tilts near ±180° don't wrap.
        while (d > Math.PI) d -= 2 * Math.PI;
        while (d <= -Math.PI) d += 2 * Math.PI;
        return d;
    }

    [Fact]
    public void Reveal_BaselineChars_AreAlwaysShown()
    {
        var schedule = new RevealSchedule(perStrokeMs: 30, perLetterMs: 100);
        // Char 0 and 1 are below the baseline of 2 → always revealed, regardless of elapsed (even 0).
        Assert.True(CuneiformReveal.IsStrokeRevealed(0, 0, baselineChars: 2, elapsedMs: 0, schedule));
        Assert.True(CuneiformReveal.IsStrokeRevealed(1, 5, baselineChars: 2, elapsedMs: 0, schedule));
    }

    [Fact]
    public void Reveal_NewLetters_PressInOnSchedule()
    {
        var schedule = new RevealSchedule(perStrokeMs: 30, perLetterMs: 100);
        // Baseline 0: char 0 starts at t=0, char 1 at t=100, char 2 at t=200.
        // Char 1, stroke ordinal 0, needs elapsed >= 100.
        Assert.False(CuneiformReveal.IsStrokeRevealed(1, 0, baselineChars: 0, elapsedMs: 99, schedule));
        Assert.True(CuneiformReveal.IsStrokeRevealed(1, 0, baselineChars: 0, elapsedMs: 100, schedule));
        // Within a letter, ordinal 2 of char 1 needs 100 + 2*30 = 160.
        Assert.False(CuneiformReveal.IsStrokeRevealed(1, 2, baselineChars: 0, elapsedMs: 159, schedule));
        Assert.True(CuneiformReveal.IsStrokeRevealed(1, 2, baselineChars: 0, elapsedMs: 160, schedule));
    }

    [Fact]
    public void Reveal_StrokeOrdinalOffsetsFromLetterStart()
    {
        var schedule = new RevealSchedule(perStrokeMs: 30, perLetterMs: 100);
        // Each stroke of a letter presses in at letterStart + ordinal*perStrokeMs. Char 1 starts at 100;
        // its stroke ordinal 0 is in at 100, ordinal 1 at 130 — the second stroke lags the first.
        Assert.True(CuneiformReveal.IsStrokeRevealed(1, 0, baselineChars: 0, elapsedMs: 100, schedule));
        Assert.False(CuneiformReveal.IsStrokeRevealed(1, 1, baselineChars: 0, elapsedMs: 100, schedule));
        Assert.True(CuneiformReveal.IsStrokeRevealed(1, 1, baselineChars: 0, elapsedMs: 130, schedule));
    }

    [Fact]
    public void Reveal_BaselineOffsetsTheSchedule()
    {
        var schedule = new RevealSchedule(perStrokeMs: 30, perLetterMs: 100);
        // With baseline 3, char 3 is the first new letter (offset 0 → starts at t=0), char 4 at t=100.
        Assert.True(CuneiformReveal.IsStrokeRevealed(3, 0, baselineChars: 3, elapsedMs: 0, schedule));
        Assert.False(CuneiformReveal.IsStrokeRevealed(4, 0, baselineChars: 3, elapsedMs: 99, schedule));
        Assert.True(CuneiformReveal.IsStrokeRevealed(4, 0, baselineChars: 3, elapsedMs: 100, schedule));
    }

    [Fact]
    public void Reveal_TotalDuration_IsZeroWhenNothingNew()
    {
        var schedule = new RevealSchedule(perStrokeMs: 30, perLetterMs: 100);
        Assert.Equal(0.0, CuneiformReveal.TotalDurationMs(baselineChars: 5, totalChars: 5, schedule));
        Assert.Equal(0.0, CuneiformReveal.TotalDurationMs(baselineChars: 8, totalChars: 5, schedule));
    }

    [Fact]
    public void Reveal_TotalDuration_CoversAllNewLetters()
    {
        var schedule = new RevealSchedule(perStrokeMs: 30, perLetterMs: 100);
        // 3 new letters (5 - 2); last starts at 2*100=200. Duration must be >= the last letter's start so
        // the whole run has time to complete inside the window.
        double d = CuneiformReveal.TotalDurationMs(baselineChars: 2, totalChars: 5, schedule);
        Assert.True(d >= 200, $"duration {d} should cover the last new letter's start (200ms)");
    }

    // A tiny hand-authored bundle: A (plain), X (kerns +10 before Y), Z (kerns -100 before Y), Y.
    // All share gridSize 100, widths 20/20, paddings 5/5, so advance = 40 and the floor gap = 10.
    private const string SampleBundleJson = """
        {
          "generatedFrom": "test",
          "characterCount": 4,
          "characters": {
            "A": { "character": "A", "gridSize": 100, "leftWidth": 20, "rightWidth": 20,
                   "leftPadding": 5, "rightPadding": 5,
                   "strokes": [ { "start": { "x": 40, "y": 10 }, "end": { "x": 60, "y": 90 }, "weight": 6 } ] },
            "X": { "character": "X", "gridSize": 100, "leftWidth": 20, "rightWidth": 20,
                   "leftPadding": 5, "rightPadding": 5, "kerning": { "Y": 10 },
                   "strokes": [ { "start": { "x": 40, "y": 10 }, "end": { "x": 60, "y": 90 }, "weight": 6 } ] },
            "Z": { "character": "Z", "gridSize": 100, "leftWidth": 20, "rightWidth": 20,
                   "leftPadding": 5, "rightPadding": 5, "kerning": { "Y": -100 },
                   "strokes": [] },
            "Y": { "character": "Y", "gridSize": 100, "leftWidth": 20, "rightWidth": 20,
                   "leftPadding": 5, "rightPadding": 5,
                   "strokes": [] }
          }
        }
        """;
}
