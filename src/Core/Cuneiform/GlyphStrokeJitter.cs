namespace Scribe.Core.Cuneiform;

/// <summary>
/// Deterministic, VS-API-free per-stroke perturbation that makes cuneiform text read as hand-pressed rather
/// than printed. Given a stroke, an integer <c>seed</c>, and a <c>strength</c>, it nudges the stroke's two
/// endpoints (shifting its position, and thereby its angle and length) and its weight (width) by small
/// bounded amounts, returning a NEW <see cref="GlyphStroke"/>. It stays within the existing 2-point + weight
/// model — <see cref="GlyphStroke.Corners()"/> still derives the drawn rectangle — so no storage/format
/// migration is involved.
///
/// <para>Pure and reproducible, exactly like <see cref="Scribe.Core.ScribeTextCorruptor"/>: the randomness is
/// driven solely by the caller-supplied <c>seed</c> via <see cref="System.Random"/>, so a given (stroke,
/// seed, strength) always yields the same output. That determinism is what lets the renderer key the seed off
/// a stroke's stable identity (see <see cref="PositionedStroke.SourceCharIndex"/> /
/// <see cref="PositionedStroke.StrokeOrdinal"/>) so the SAME stroke jitters identically every frame — no
/// shimmer — while DIFFERENT strokes (including repeated copies of the same character) diverge. The Mod layer
/// owns picking the base seed; Core never invents one.</para>
///
/// <para>This is a VISUAL transform only. The renderer applies it at paint time to the drawn geometry; it
/// must never feed jittered strokes back into layout metrics (total width, character boundaries, caret, or
/// hit-testing), which continue to use the un-jittered layout.</para>
/// </summary>
public static class GlyphStrokeJitter
{
    /// <summary>Maximum endpoint displacement, per axis, at <c>strength == 1</c>, as a fraction of the
    /// glyph's grid size. Kept small: a few percent of an em reads as a hand-wobble without turning legible
    /// glyphs into scribbles. The actual offset is uniform in [-max, +max] and scales linearly with strength.</summary>
    public const double MaxPositionFraction = 0.05;

    /// <summary>Maximum weight (width) variation at <c>strength == 1</c>, as a fraction of the stroke's own
    /// weight. The actual multiplier is uniform in [1 - max, 1 + max] and scales linearly with strength, so a
    /// stroke never inverts or vanishes.</summary>
    public const double MaxWeightFraction = 0.25;

    /// <summary>
    /// Returns <paramref name="stroke"/> perturbed by a bounded random amount determined by
    /// <paramref name="seed"/> and <paramref name="strength"/>. A <paramref name="strength"/> of 0 (or less)
    /// returns the stroke unchanged (identity), preserving the exact authored geometry. Strength is clamped to
    /// 0..1. <paramref name="gridSize"/> is the stroke's glyph grid size (from
    /// <see cref="PositionedStroke.GridSize"/>), used to scale the position jitter so it is proportional to
    /// glyph size. Deterministic for a given (stroke, seed, strength, gridSize).
    /// </summary>
    /// <remarks>
    /// Each endpoint is offset independently in x and y (four draws) and the weight is scaled by one further
    /// draw, all from a single <see cref="System.Random"/> seeded with <paramref name="seed"/>, so the draw
    /// order is fixed and reproducible. Because both endpoints move independently, the stroke's angle and
    /// length shift as well as its position — the hand-drawn quality — while the result is always a valid
    /// oriented rectangle. The weight multiplier stays in [1 - <see cref="MaxWeightFraction"/>,
    /// 1 + <see cref="MaxWeightFraction"/>] (scaled by strength) so weight never goes negative.
    /// </remarks>
    /// <summary>
    /// Derives a per-stroke jitter seed from a per-field/document <paramref name="baseSeed"/> and the stroke's
    /// stable identity (<see cref="PositionedStroke.SourceCharIndex"/> and
    /// <see cref="PositionedStroke.StrokeOrdinal"/>), so the SAME stroke always seeds identically (no
    /// frame-to-frame shimmer) while DIFFERENT strokes — including repeated copies of the same character —
    /// diverge. The Mod layer supplies the base seed; combining it here keeps the mixing in one tested place.
    /// </summary>
    /// <remarks>
    /// A plain <c>baseSeed + index</c> would hand <see cref="System.Random"/> near-consecutive seeds for
    /// adjacent strokes, whose first draws can correlate; the finalizer below (the well-known "lowbias32"
    /// integer avalanche) scrambles the bits so neighbours look independent. Deterministic and allocation-free.
    /// </remarks>
    public static int SeedFor(int baseSeed, int sourceCharIndex, int strokeOrdinal)
    {
        unchecked
        {
            uint h = (uint)baseSeed;
            h = Mix(h ^ (uint)sourceCharIndex);
            h = Mix(h ^ (uint)strokeOrdinal);
            return (int)h;
        }
    }

    /// <summary>The "lowbias32" integer finalizer — a bijective bit-avalanche that turns a small/sequential
    /// input into a well-distributed 32-bit value, so consecutive stroke identities produce uncorrelated
    /// seeds. Pure and deterministic.</summary>
    private static uint Mix(uint x)
    {
        unchecked
        {
            x ^= x >> 16;
            x *= 0x7feb352dU;
            x ^= x >> 15;
            x *= 0x846ca68bU;
            x ^= x >> 16;
            return x;
        }
    }

    public static GlyphStroke Jitter(GlyphStroke stroke, int seed, double strength, double gridSize)
    {
        strength = Math.Clamp(strength, 0.0, 1.0);
        if (strength <= 0.0) return stroke;

        var rand = new Random(seed);

        double maxOffset = MaxPositionFraction * strength * gridSize;
        double maxWeight = MaxWeightFraction * strength;

        // Uniform in [-maxOffset, +maxOffset]; independent per axis per endpoint (four draws, fixed order).
        double Offset() => (rand.NextDouble() * 2.0 - 1.0) * maxOffset;

        var start = new Vec2(stroke.Start.X + Offset(), stroke.Start.Y + Offset());
        var end = new Vec2(stroke.End.X + Offset(), stroke.End.Y + Offset());

        // Uniform weight multiplier in [1 - maxWeight, 1 + maxWeight] (fifth draw). Clamp the low end at a
        // small positive floor so a stroke can never collapse to zero/negative width.
        double weightMul = 1.0 + (rand.NextDouble() * 2.0 - 1.0) * maxWeight;
        double weight = Math.Max(stroke.Weight * weightMul, 0.0);

        return new GlyphStroke(start, end, weight);
    }
}
