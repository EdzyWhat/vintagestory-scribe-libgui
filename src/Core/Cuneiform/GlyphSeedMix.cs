namespace Scribe.Core.Cuneiform;

/// <summary>
/// The shared integer bit-avalanche used to derive per-stroke and per-character random seeds from a
/// caller-supplied base seed and a stroke/character identity. Extracted so <see cref="GlyphStrokeJitter"/>
/// and <see cref="GlyphStrokeRotation"/> mix identically — the two seed streams avalanche the same way but
/// stay independent because they fold in different identity components (jitter folds in the stroke ordinal,
/// rotation does not). Pure, deterministic, allocation-free.
/// </summary>
internal static class GlyphSeedMix
{
    /// <summary>The "lowbias32" integer finalizer — a bijective bit-avalanche that turns a small/sequential
    /// input into a well-distributed 32-bit value, so consecutive stroke/character identities produce
    /// uncorrelated seeds. Pure and deterministic.</summary>
    public static uint Mix(uint x)
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
}
