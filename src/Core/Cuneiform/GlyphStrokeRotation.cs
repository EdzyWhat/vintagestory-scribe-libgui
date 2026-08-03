namespace Scribe.Core.Cuneiform;

/// <summary>
/// Deterministic, VS-API-free whole-character rotation for cuneiform glyphs — a visual sibling of
/// <see cref="GlyphStrokeJitter"/>. Where jitter nudges each endpoint independently (a wobble WITHIN a
/// glyph), rotation tilts an entire character rigidly about its box center, so a hand-pressed glyph reads
/// as slightly askew rather than mechanically upright. Given a stroke, a pivot, an integer <c>seed</c>, and
/// a maximum tilt in degrees, it rotates the stroke's two endpoints about <paramref name="pivot"/> (leaving
/// <see cref="GlyphStroke.Weight"/> unchanged) and returns a NEW <see cref="GlyphStroke"/>.
///
/// <para>The seed deliberately OMITS the stroke ordinal (unlike <see cref="GlyphStrokeJitter.SeedFor"/>):
/// every stroke of one character draws the SAME angle about the SAME pivot, so the character tilts as a
/// rigid unit and does not shear. Two copies of the same character at different positions still diverge
/// (their source-char indices differ). Pure and reproducible, exactly like jitter: a given (stroke, pivot,
/// seed, maxDegrees) always yields the same output, so the SAME character tilts identically every frame —
/// no shimmer.</para>
///
/// <para>This is a VISUAL transform only. The renderer applies it at paint time to the drawn geometry, after
/// jitter and before <see cref="GlyphStroke.Corners()"/>; it must never feed rotated strokes back into
/// layout metrics (total width, character boundaries, caret, or hit-testing), which continue to use the
/// un-jittered, un-rotated layout. The Mod layer owns picking the base seed and the pivot; Core never
/// invents them.</para>
/// </summary>
public static class GlyphStrokeRotation
{
    /// <summary>Default maximum tilt, in degrees, at full strength. The actual angle is uniform in
    /// [-max, +max]. Kept modest: a few degrees reads as a hand-pressed lean without making the text look
    /// like it is falling over. Prototyped in the glyph-forge tool at ±8°.</summary>
    public const double DefaultMaxDegrees = 8.0;

    /// <summary>
    /// Derives a per-character rotation seed from a per-field/document <paramref name="baseSeed"/> and the
    /// character's stable identity (<see cref="PositionedStroke.SourceCharIndex"/>). Unlike
    /// <see cref="GlyphStrokeJitter.SeedFor"/> it takes NO stroke ordinal, so every stroke of one character
    /// derives the same seed (and thus the same tilt) — the character rotates rigidly. Reuses the shared
    /// <see cref="GlyphSeedMix.Mix"/> avalanche so the seed stream is well-distributed but independent of the
    /// jitter stream (they fold in different identity components). Deterministic and allocation-free.
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
    /// Returns <paramref name="stroke"/> rotated about <paramref name="pivot"/> by a bounded random angle
    /// determined by <paramref name="seed"/> and <paramref name="maxDegrees"/>. The angle is uniform in
    /// [-<paramref name="maxDegrees"/>, +<paramref name="maxDegrees"/>]. A <paramref name="maxDegrees"/> of 0
    /// (or less) returns the stroke unchanged (identity), preserving the exact upright geometry.
    /// <see cref="GlyphStroke.Weight"/> is unaffected (rotation preserves stroke width). Deterministic for a
    /// given (stroke, pivot, seed, maxDegrees).
    /// </summary>
    /// <remarks>
    /// A single <see cref="System.Random"/> seeded with <paramref name="seed"/> draws the one angle, so the
    /// draw is fixed and reproducible. Both endpoints rotate about the same pivot by the same angle — a rigid
    /// rotation — using the standard 2D rotation matrix.
    /// </remarks>
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

    /// <summary>Standard 2D rotation of <paramref name="p"/> about <paramref name="pivot"/> by the angle whose
    /// cosine/sine are given: translate to the pivot's frame, apply the rotation matrix, translate back.</summary>
    private static Vec2 RotatePoint(Vec2 p, Vec2 pivot, double cos, double sin)
    {
        double dx = p.X - pivot.X;
        double dy = p.Y - pivot.Y;
        return new Vec2(
            pivot.X + dx * cos - dy * sin,
            pivot.Y + dx * sin + dy * cos);
    }
}
