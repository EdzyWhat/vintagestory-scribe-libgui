namespace Scribe.Core.Cuneiform;

/// <summary>
/// A minimal 2D point/vector in glyph grid units (see <see cref="Glyph.GridSize"/>), not pixels.
/// Pure BCL because <c>Scribe.Core</c> must not reference the Vintage Story API or a Skia/OpenTK vector
/// type; the Mod layer maps it onto its own render vectors.
/// </summary>
public readonly struct Vec2
{
    public double X { get; }

    public double Y { get; }

    public Vec2(double x, double y)
    {
        X = x;
        Y = y;
    }

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);

    public override string ToString() => $"({X}, {Y})";
}
