namespace Scribe.Core.Cuneiform;

/// <summary>
/// The bit-avalanche shared by <see cref="GlyphStrokeJitter"/> and <see cref="GlyphStrokeRotation"/> seed
/// derivation. The two streams stay independent because they fold in different identity components
/// (jitter folds in the stroke ordinal, rotation does not).
/// </summary>
internal static class GlyphSeedMix
{
    /// <summary>The "lowbias32" finalizer: turns small/sequential input into well-distributed bits so
    /// consecutive identities produce uncorrelated seeds.</summary>
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
