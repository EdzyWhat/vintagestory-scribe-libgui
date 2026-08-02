namespace Scribe.Core.Cuneiform;

/// <summary>
/// One cuneiform character's geometry: an ordered list of <see cref="GlyphStroke"/>s on a square
/// em-grid (<see cref="GridSize"/>), plus the character's own horizontal footprint
/// (<see cref="LeftWidth"/>/<see cref="RightWidth"/>, measured from grid center) and its minimum
/// clearance to neighbors (<see cref="LeftPadding"/>/<see cref="RightPadding"/>). An optional
/// <see cref="Kerning"/> map narrows/widens the gap to a specific following character. Mirrors the
/// glyph-forge export format (see <c>glyph-forge/EXPORT-FORMAT.md</c>).
///
/// Game-agnostic (pure BCL). Construct the normalized shape through <see cref="FromRaw"/>, which
/// applies the format-version migration ladder so an older/partial glyph shape yields sensible
/// widths rather than being rejected.
/// </summary>
public sealed class Glyph
{
    /// <summary>The single character this glyph represents (already uppercase for A–Z).</summary>
    public char Character { get; }

    /// <summary>The em-grid size: coordinates range roughly 0..<see cref="GridSize"/> on each axis.
    /// Treated as an em-square (design units), not literal pixels. Defaults to 100 in the authored set.</summary>
    public double GridSize { get; }

    /// <summary>Distance from grid center (<c>GridSize/2</c>) to the box's LEFT edge. The box spans local
    /// <c>[GridSize/2 - LeftWidth, GridSize/2 + RightWidth]</c>. Measured from center because a glyph's
    /// ink is drawn near the grid's middle, not from local x=0.</summary>
    public double LeftWidth { get; }

    /// <summary>Distance from grid center to the box's RIGHT edge (see <see cref="LeftWidth"/>).</summary>
    public double RightWidth { get; }

    /// <summary>Minimum clearance beyond the LEFT edge a neighbor's ink can't cross. A hard floor:
    /// kerning may widen the gap but never narrow it past the adjoining paddings' sum. May be negative
    /// (deliberate overlap).</summary>
    public double LeftPadding { get; }

    /// <summary>Minimum clearance beyond the RIGHT edge (see <see cref="LeftPadding"/>).</summary>
    public double RightPadding { get; }

    /// <summary>Sparse per-pair kerning: maps a FOLLOWING character to a grid-unit adjustment applied
    /// only when this glyph is immediately followed by that character. Positive widens the gap; a value
    /// that would narrow past the padding floor is clamped to the floor by the layout engine. Never null
    /// (empty when unauthored).</summary>
    public IReadOnlyDictionary<char, double> Kerning { get; }

    /// <summary>This glyph's strokes in AUTHORED CONSTRUCTION ORDER — never reorder (see
    /// <see cref="GlyphStroke"/>). May be empty (a valid glyph with no ink yet).</summary>
    public IReadOnlyList<GlyphStroke> Strokes { get; }

    /// <summary>The full horizontal footprint (<c>LeftWidth + RightWidth</c>) the pen advances by when
    /// this glyph is placed in a line.</summary>
    public double AdvanceWidth => LeftWidth + RightWidth;

    /// <summary>Left edge of the box in LOCAL glyph coordinates (<c>GridSize/2 - LeftWidth</c>). A glyph's
    /// strokes are positioned in a line by aligning this local edge with the pen (see the layout engine).</summary>
    public double LocalBoxLeft => GridSize / 2.0 - LeftWidth;

    /// <summary>Fallback <see cref="LeftWidth"/>/<see cref="RightWidth"/> for a brand-new glyph with no
    /// strokes and no legacy width field (export-format migration ladder, final rung).</summary>
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
    /// Builds a normalized <see cref="Glyph"/> from raw (possibly older/partial) fields, applying the
    /// export-format migration ladder for widths so an older shape is migrated on read rather than
    /// rejected:
    /// <list type="bullet">
    /// <item><see cref="LeftWidth"/>/<see cref="RightWidth"/> already present → used as-is.</item>
    /// <item>only <c>width</c> (interim, measured from local x=0) → <c>left = right = width/2</c>.</item>
    /// <item>only <c>advanceWidth</c> (oldest) → <c>left = right = advanceWidth/2</c>, paddings 0.</item>
    /// <item>none of the above but has strokes → widths derived from the stroke bounding box's distance
    ///   from grid center (clamped to ≥ 0 per side).</item>
    /// <item>no widths and no strokes → <c>left = right =</c> <see cref="DefaultHalfWidth"/>.</item>
    /// </list>
    /// The migration is lossless with respect to total footprint but not guaranteed to center the box on
    /// the ink for the <c>width</c>/<c>advanceWidth</c> cases (the older shapes recorded no split).
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
    /// Derives <see cref="LeftWidth"/>/<see cref="RightWidth"/> from the strokes' bounding box: the box's
    /// left edge is the min stroke x, its right edge the max stroke x, and the widths are those edges'
    /// distances from grid center, each clamped to ≥ 0 (a glyph whose ink sits entirely on one side of
    /// center gets 0 on the empty side rather than a negative width). The bounds use stroke ENDPOINTS
    /// (not the weighted rectangle) — the same approximation the editor's overlay uses.
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
