namespace Scribe.Core;

/// <summary>Pure remainder mapping from a stored flavor seed onto a viewer-locale pool. Game-agnostic
/// so Core tests can prove a short pool never receives an out-of-range index without a VS install.</summary>
public static class HistoryFlavor
{
    /// <summary>Maps <paramref name="seed"/> into <c>[0, poolSize)</c> via
    /// <c>abs(seed) % poolSize</c>. Returns 0 when <paramref name="poolSize"/> is less than 1
    /// (empty viewer pool — the caller then uses a non-indexed fallback key rather than throwing).
    /// Uses a <see cref="long"/> abs so <see cref="int.MinValue"/> does not overflow.</summary>
    public static int Slot(int seed, int poolSize)
    {
        if (poolSize < 1) return 0;
        return (int)(Math.Abs((long)seed) % poolSize);
    }
}
