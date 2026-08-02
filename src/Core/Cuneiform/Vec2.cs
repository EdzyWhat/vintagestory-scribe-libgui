namespace Scribe.Core.Cuneiform;

/// <summary>
/// A minimal 2D point/vector in glyph grid units. Deliberately tiny and pure-BCL: the cuneiform
/// glyph model lives in <c>Scribe.Core</c>, which MUST NOT reference the Vintage Story API (nor a
/// Skia/OpenTK vector type), so it stays unit-testable without a game install. The Mod layer maps
/// these grid-unit coordinates onto its own render vectors when painting.
///
/// Coordinates are expressed on a glyph's square em-grid (see <see cref="Glyph.GridSize"/>), not in
/// pixels. A readonly value type: layout produces new instances rather than mutating in place.
/// </summary>
public readonly struct Vec2
{
    /// <summary>Horizontal grid-unit coordinate.</summary>
    public double X { get; }

    /// <summary>Vertical grid-unit coordinate.</summary>
    public double Y { get; }

    public Vec2(double x, double y)
    {
        X = x;
        Y = y;
    }

    /// <summary>Component-wise sum.</summary>
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

    /// <summary>Component-wise difference.</summary>
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);

    public override string ToString() => $"({X}, {Y})";
}
