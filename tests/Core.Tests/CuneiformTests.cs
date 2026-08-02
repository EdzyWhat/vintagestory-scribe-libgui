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
    public void Parse_ShippedBundle_ContainsAll47AuthoredCharacters()
    {
        // The real committed artifact (copied into the test output) must parse and carry the full
        // authored set: A–Z (26) + 0–9 (10) + 11 punctuation = 47 — no lowercase, no space glyph.
        string path = Path.Combine(AppContext.BaseDirectory, "cuneiform-glyphs-1.json");
        Assert.True(File.Exists(path), $"Shipped glyph bundle not found at {path}");

        GlyphBundle bundle = GlyphBundle.Parse(File.ReadAllText(path));

        Assert.Equal(47, bundle.CharacterCount);
        Assert.True(bundle.Contains('A'));
        Assert.True(bundle.Contains('Z'));
        Assert.True(bundle.Contains('0'));
        Assert.True(bundle.Contains('?'));
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

    // ---- Helpers ----------------------------------------------------------------------------

    private static CuneiformLine Layout(string text) =>
        new CuneiformLineLayout(GlyphBundle.Parse(SampleBundleJson)).Layout(text);

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
