namespace Scribe.Core.Cuneiform;

/// <summary>
/// One straight stroke of a cuneiform glyph, expressed on the glyph's em-grid (see
/// <see cref="Glyph.GridSize"/>): a centerline from <see cref="Start"/> to <see cref="End"/> plus a
/// <see cref="Weight"/> (the stroke's width, in the same grid units as the coordinates). A stroke is
/// rendered as a filled rectangle of that width, centered on the centerline, with square-cut ends —
/// see <see cref="Corners"/> — matching the glyph-forge export format exactly.
///
/// Strokes are stored and laid out in AUTHORED CONSTRUCTION ORDER (the carving/reveal sequence): they
/// must never be sorted or reordered, because rendering the first N strokes is what produces a partial
/// reveal. Game-agnostic (pure BCL).
/// </summary>
public readonly struct GlyphStroke
{
    /// <summary>The centerline's start point, in grid units.</summary>
    public Vec2 Start { get; }

    /// <summary>The centerline's end point, in grid units.</summary>
    public Vec2 End { get; }

    /// <summary>The stroke's width, in the same grid units as the coordinates (not pixels).</summary>
    public double Weight { get; }

    public GlyphStroke(Vec2 start, Vec2 end, double weight)
    {
        Start = start;
        End = end;
        Weight = weight;
    }

    /// <summary>
    /// The four corners of this stroke's rectangle, in the export format's fixed order:
    /// <c>Start + p</c>, <c>End + p</c>, <c>End - p</c>, <c>Start - p</c>, where <c>p</c> is the unit
    /// perpendicular to the centerline scaled to half the weight
    /// (<c>p = (-dy/len·w/2, dx/len·w/2)</c>). This yields an oriented (arbitrary-angle) rectangle with
    /// square-cut ends — NOT an axis-aligned bounding box — so a diagonal stroke renders as a rotated
    /// rectangle. Matches <c>strokeCorners()</c> in the glyph-forge editor.
    ///
    /// A degenerate (zero-length) stroke has no defined direction; its four corners collapse to the
    /// start point rather than producing NaNs from the divide-by-zero.
    /// </summary>
    public Vec2[] Corners()
    {
        double dx = End.X - Start.X;
        double dy = End.Y - Start.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);

        if (len == 0)
        {
            // No direction to take a perpendicular of; avoid dividing by zero.
            return new[] { Start, Start, Start, Start };
        }

        var p = new Vec2(-dy / len * Weight / 2, dx / len * Weight / 2);
        return new[] { Start + p, End + p, End - p, Start - p };
    }
}
