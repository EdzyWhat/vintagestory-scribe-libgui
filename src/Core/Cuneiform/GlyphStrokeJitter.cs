namespace Scribe.Core.Cuneiform;

/// <summary>
/// Deterministic per-stroke perturbation that makes cuneiform read as hand-pressed. Nudges a stroke's
/// endpoints and weight by small bounded amounts and returns a NEW <see cref="GlyphStroke"/>.
///
/// <para>Output depends only on the caller's seed (via <see cref="System.Random"/>), so a stroke keyed
/// by its stable identity (<see cref="PositionedStroke.SourceCharIndex"/> /
/// <see cref="PositionedStroke.StrokeOrdinal"/>) jitters identically every frame while different strokes
/// diverge. The Mod layer picks the base seed.</para>
///
/// <para>VISUAL only: apply at paint time and never feed jittered strokes back into layout metrics
/// (width, boundaries, caret, hit-testing).</para>
/// </summary>
public static class GlyphStrokeJitter
{
    /// <summary>Maximum endpoint displacement per axis at <c>strength == 1</c>, as a fraction of the
    /// glyph's grid size; the offset is uniform in [-max, +max] and scales linearly with strength.
    /// Kept small because it stacks with whole-character rotation.</summary>
    public const double MaxPositionFraction = 0.0375;

    /// <summary>Maximum weight variation at <c>strength == 1</c>, as a fraction of the stroke's weight;
    /// the multiplier is uniform in [1 - max, 1 + max] and scales with strength.</summary>
    public const double MaxWeightFraction = 0.25;

    /// <summary>
    /// Derives a per-stroke seed from <paramref name="baseSeed"/> and the stroke's stable identity, so the
    /// same stroke always seeds identically while different strokes diverge. A plain
    /// <c>baseSeed + index</c> would give <see cref="System.Random"/> near-consecutive seeds with correlated
    /// first draws, hence <see cref="GlyphSeedMix.Mix"/>.
    /// </summary>
    public static int SeedFor(int baseSeed, int sourceCharIndex, int strokeOrdinal)
    {
        unchecked
        {
            uint h = (uint)baseSeed;
            h = GlyphSeedMix.Mix(h ^ (uint)sourceCharIndex);
            h = GlyphSeedMix.Mix(h ^ (uint)strokeOrdinal);
            return (int)h;
        }
    }

    /// <summary>
    /// Returns <paramref name="stroke"/> with both endpoints (x and y independently) and the weight
    /// perturbed by seeded, bounded amounts. <paramref name="strength"/> is clamped to 0..1 and 0 returns the
    /// stroke unchanged; <paramref name="gridSize"/> scales the position jitter. The draw order is fixed, so
    /// output is deterministic for a given input.
    /// </summary>
    public static GlyphStroke Jitter(GlyphStroke stroke, int seed, double strength, double gridSize)
    {
        strength = Math.Clamp(strength, 0.0, 1.0);
        if (strength <= 0.0) return stroke;

        var rand = new Random(seed);

        double maxOffset = MaxPositionFraction * strength * gridSize;
        double maxWeight = MaxWeightFraction * strength;

        double Offset() => (rand.NextDouble() * 2.0 - 1.0) * maxOffset;

        var start = new Vec2(stroke.Start.X + Offset(), stroke.Start.Y + Offset());
        var end = new Vec2(stroke.End.X + Offset(), stroke.End.Y + Offset());

        // Fifth draw; the clamp keeps width from going negative.
        double weightMul = 1.0 + (rand.NextDouble() * 2.0 - 1.0) * maxWeight;
        double weight = Math.Max(stroke.Weight * weightMul, 0.0);

        return new GlyphStroke(start, end, weight);
    }
}
