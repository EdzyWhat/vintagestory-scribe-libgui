namespace Scribe.Core.Cuneiform;

/// <summary>
/// Deterministic whole-character rotation, a sibling of <see cref="GlyphStrokeJitter"/>: tilts an entire
/// character rigidly about a pivot (weight unchanged) and returns a NEW <see cref="GlyphStroke"/>.
///
/// <para>The seed deliberately OMITS the stroke ordinal (unlike <see cref="GlyphStrokeJitter.SeedFor"/>) so
/// every stroke of one character draws the same angle and the character does not shear. Output depends
/// only on the inputs, so the tilt is stable frame to frame.</para>
///
/// <para>VISUAL only: apply at paint time after jitter and before <see cref="GlyphStroke.Corners()"/>;
/// never feed rotated strokes back into layout metrics. The Mod layer picks the base seed and pivot.</para>
/// </summary>
public static class GlyphStrokeRotation
{
    /// <summary>Default maximum tilt in degrees; the angle is uniform in [-max, +max].</summary>
    public const double DefaultMaxDegrees = 8.0;

    /// <summary>
    /// Derives a per-character seed from <paramref name="baseSeed"/> and
    /// <see cref="PositionedStroke.SourceCharIndex"/>. With no stroke ordinal, every stroke of a character
    /// gets the same tilt.
    /// </summary>
    public static int SeedFor(int baseSeed, int sourceCharIndex)
    {
        unchecked
        {
            uint h = (uint)baseSeed;
            h = GlyphSeedMix.Mix(h ^ (uint)sourceCharIndex);
            return (int)h;
        }
    }

    /// <summary>
    /// Returns <paramref name="stroke"/> rotated about <paramref name="pivot"/> by a seeded angle uniform in
    /// [-<paramref name="maxDegrees"/>, +<paramref name="maxDegrees"/>]. A <paramref name="maxDegrees"/> of 0
    /// or less returns the stroke unchanged.
    /// </summary>
    public static GlyphStroke Rotate(GlyphStroke stroke, Vec2 pivot, int seed, double maxDegrees)
    {
        if (maxDegrees <= 0.0) return stroke;

        var rand = new Random(seed);
        double angleDeg = (rand.NextDouble() * 2.0 - 1.0) * maxDegrees;
        double angleRad = angleDeg * Math.PI / 180.0;
        double cos = Math.Cos(angleRad);
        double sin = Math.Sin(angleRad);

        return new GlyphStroke(
            RotatePoint(stroke.Start, pivot, cos, sin),
            RotatePoint(stroke.End, pivot, cos, sin),
            stroke.Weight);
    }

    /// <summary>Rotates <paramref name="p"/> about <paramref name="pivot"/> by the angle with the given cosine/sine.</summary>
    private static Vec2 RotatePoint(Vec2 p, Vec2 pivot, double cos, double sin)
    {
        double dx = p.X - pivot.X;
        double dy = p.Y - pivot.Y;
        return new Vec2(
            pivot.X + dx * cos - dy * sin,
            pivot.Y + dx * sin + dy * cos);
    }
}
