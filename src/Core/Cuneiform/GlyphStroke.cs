namespace Scribe.Core.Cuneiform;

/// <summary>
/// One straight stroke on a glyph's em-grid (<see cref="Glyph.GridSize"/>): a centerline from
/// <see cref="Start"/> to <see cref="End"/> plus a <see cref="Weight"/>, rendered as a square-cut
/// rectangle (<see cref="Corners"/>) matching the glyph-forge export format.
///
/// Strokes are kept in AUTHORED CONSTRUCTION ORDER and must never be sorted or reordered: rendering
/// the first N strokes produces the partial reveal.
/// </summary>
public readonly struct GlyphStroke
{
    public Vec2 Start { get; }

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
    /// The rectangle's corners in the export format's fixed order: <c>Start + p</c>, <c>End + p</c>,
    /// <c>End - p</c>, <c>Start - p</c>, with <c>p = (-dy/len·w/2, dx/len·w/2)</c>. The result is an
    /// oriented rectangle, not an axis-aligned box. Matches <c>strokeCorners()</c> in the glyph-forge
    /// editor. A zero-length stroke collapses all four corners to the start point.
    /// </summary>
    public Vec2[] Corners()
    {
        double dx = End.X - Start.X;
        double dy = End.Y - Start.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);

        if (len == 0)
        {
            return new[] { Start, Start, Start, Start };
        }

        var p = new Vec2(-dy / len * Weight / 2, dx / len * Weight / 2);
        return new[] { Start + p, End + p, End - p, Start - p };
    }
}
