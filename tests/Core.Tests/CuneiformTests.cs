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
