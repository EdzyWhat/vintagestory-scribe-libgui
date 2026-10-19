namespace Scribe.Core;

/// <summary>Maps a stored flavor seed onto a viewer-locale pool by remainder.</summary>
public static class HistoryFlavor
{
    /// <summary>Maps <paramref name="seed"/> into <c>[0, poolSize)</c>; returns 0 for an empty pool (the
    /// caller then uses a non-indexed fallback key). The <see cref="long"/> abs avoids
    /// <see cref="int.MinValue"/> overflow.</summary>
    public static int Slot(int seed, int poolSize)
    {
        if (poolSize < 1) return 0;
        return (int)(Math.Abs((long)seed) % poolSize);
    }
}
