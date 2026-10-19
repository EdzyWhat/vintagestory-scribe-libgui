namespace Scribe.Core.Cuneiform;

/// <summary>
/// One cuneiform character's geometry: ordered <see cref="GlyphStroke"/>s on a square em-grid
/// (<see cref="GridSize"/>), its horizontal footprint (<see cref="LeftWidth"/>/<see cref="RightWidth"/>
/// from grid center), minimum neighbor clearance (paddings), and optional per-pair <see cref="Kerning"/>.
/// Mirrors the glyph-forge export format (<c>glyph-forge/EXPORT-FORMAT.md</c>). Build normalized
/// glyphs through <see cref="FromRaw"/>, which migrates older/partial shapes.
/// </summary>
public sealed class Glyph
{
    /// <summary>The single character this glyph represents (already uppercase for A–Z).</summary>
    public char Character { get; }

    /// <summary>The em-grid size: coordinates range roughly 0..GridSize per axis (design units, not pixels).</summary>
    public double GridSize { get; }

    /// <summary>Distance from grid center (<c>GridSize/2</c>) to the box's LEFT edge. The box spans local
    /// <c>[GridSize/2 - LeftWidth, GridSize/2 + RightWidth]</c>.</summary>
    public double LeftWidth { get; }

    /// <summary>Distance from grid center to the box's RIGHT edge (see <see cref="LeftWidth"/>).</summary>
    public double RightWidth { get; }

    /// <summary>Minimum clearance beyond the LEFT edge; a hard floor kerning cannot narrow past.
    /// May be negative (deliberate overlap).</summary>
    public double LeftPadding { get; }

    /// <summary>Minimum clearance beyond the RIGHT edge (see <see cref="LeftPadding"/>).</summary>
    public double RightPadding { get; }

    /// <summary>Sparse kerning: following character → grid-unit gap adjustment. Never null.</summary>
    public IReadOnlyDictionary<char, double> Kerning { get; }

    /// <summary>Strokes in AUTHORED CONSTRUCTION ORDER — never reorder. May be empty.</summary>
    public IReadOnlyList<GlyphStroke> Strokes { get; }

    /// <summary>Horizontal footprint (<c>LeftWidth + RightWidth</c>) the pen advances by.</summary>
    public double AdvanceWidth => LeftWidth + RightWidth;

    /// <summary>Left edge of the box in LOCAL glyph coordinates; the layout aligns it with the pen.</summary>
    public double LocalBoxLeft => GridSize / 2.0 - LeftWidth;

    /// <summary>Fallback half-width for a glyph with no strokes and no legacy width field.</summary>
    public const double DefaultHalfWidth = 25.0;

    public Glyph(
        char character,
        double gridSize,
        double leftWidth,
        double rightWidth,
        double leftPadding,
        double rightPadding,
        IReadOnlyList<GlyphStroke> strokes,
        IReadOnlyDictionary<char, double>? kerning = null)
    {
        Character = character;
        GridSize = gridSize;
        LeftWidth = leftWidth;
        RightWidth = rightWidth;
        LeftPadding = leftPadding;
        RightPadding = rightPadding;
        Strokes = strokes;
        Kerning = kerning ?? EmptyKerning;
    }

    private static readonly IReadOnlyDictionary<char, double> EmptyKerning =
        new Dictionary<char, double>();

    /// <summary>
    /// Builds a <see cref="Glyph"/> from raw (possibly older/partial) fields via the width migration ladder:
    /// <list type="bullet">
    /// <item><see cref="LeftWidth"/>/<see cref="RightWidth"/> already present → used as-is.</item>
    /// <item>only <c>width</c> (interim, measured from local x=0) → <c>left = right = width/2</c>.</item>
    /// <item>only <c>advanceWidth</c> (oldest) → <c>left = right = advanceWidth/2</c>, paddings 0.</item>
    /// <item>none of the above but has strokes → widths derived from the stroke bounding box's distance
    ///   from grid center (clamped to ≥ 0 per side).</item>
    /// <item>no widths and no strokes → <c>left = right =</c> <see cref="DefaultHalfWidth"/>.</item>
    /// </list>
    /// Total footprint is preserved, but the <c>width</c>/<c>advanceWidth</c> cases may not center the box on the ink.
    /// </summary>
    public static Glyph FromRaw(
        char character,
        double gridSize,
        double? leftWidth,
        double? rightWidth,
        double? width,
        double? advanceWidth,
        double? leftPadding,
        double? rightPadding,
        IReadOnlyList<GlyphStroke> strokes,
        IReadOnlyDictionary<char, double>? kerning = null)
    {
        double grid = gridSize > 0 ? gridSize : 100.0;
        double lp = leftPadding ?? 0.0;
        double rp = rightPadding ?? 0.0;
        double left;
        double right;

        if (leftWidth.HasValue && rightWidth.HasValue)
        {
            left = leftWidth.Value;
            right = rightWidth.Value;
        }
        else if (width.HasValue)
        {
            left = right = width.Value / 2.0;
        }
        else if (advanceWidth.HasValue)
        {
            left = right = advanceWidth.Value / 2.0;
            // The oldest shape recorded no padding; the ladder resets paddings to 0 for it.
            lp = leftPadding ?? 0.0;
            rp = rightPadding ?? 0.0;
        }
        else if (strokes.Count > 0)
        {
            (left, right) = WidthsFromStrokeBounds(strokes, grid);
        }
        else
        {
            left = right = DefaultHalfWidth;
        }

        return new Glyph(character, grid, left, right, lp, rp, strokes, kerning);
    }

    /// <summary>
    /// Derives widths from the strokes' min/max endpoint x (not the weighted rectangle), as distances
    /// from grid center clamped to ≥ 0.
    /// </summary>
    private static (double left, double right) WidthsFromStrokeBounds(
        IReadOnlyList<GlyphStroke> strokes, double gridSize)
    {
        double minX = double.MaxValue;
        double maxX = double.MinValue;
        foreach (var s in strokes)
        {
            minX = Math.Min(minX, Math.Min(s.Start.X, s.End.X));
            maxX = Math.Max(maxX, Math.Max(s.Start.X, s.End.X));
        }

        double center = gridSize / 2.0;
        double left = Math.Max(0.0, center - minX);
        double right = Math.Max(0.0, maxX - center);
        return (left, right);
    }
}
