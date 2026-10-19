namespace Scribe.Core.Cuneiform;

/// <summary>
/// Timing carrier for the "text presses in as you type" reveal. The clock advances
/// <see cref="PerStrokeMs"/> per stroke-unit, so letter duration follows stroke count.
/// </summary>
public readonly struct RevealSchedule
{
    /// <summary>Milliseconds the reveal clock advances per stroke-unit; the model's single speed knob.</summary>
    public double PerStrokeMs { get; }

    public RevealSchedule(double perStrokeMs)
    {
        PerStrokeMs = perStrokeMs;
    }
}

/// <summary>
/// Timing for the stroke-progression reveal. A stroke's reveal time is
/// <c>(stroke-units before it, from the baseline) × PerStrokeMs</c>. Callers build per-character units with
/// <see cref="CuneiformLineLayout.StrokeUnitsFor"/> and a prefix-sum via <see cref="CumulativeStrokeUnits"/>,
/// which <see cref="IsStrokeRevealed"/> and <see cref="TotalDurationMs"/> both read.
/// </summary>
public static class CuneiformReveal
{
    /// <summary>Press-in speed (ms per stroke-unit); the one shared home for the reveal speed.</summary>
    public const double PerStrokeMs = 80.0;

    /// <summary>Stroke-units a space (or any whitespace) contributes: a pause that draws nothing.</summary>
    public const int SpaceStrokeUnits = 2;

    /// <summary>Stroke-units a character with no authored glyph contributes (a pause, not a zero-time
    /// skip that would let the next letter press in early).</summary>
    public const int MissingGlyphStrokeUnits = 2;

    /// <summary>
    /// Prefix-sum of a per-character stroke-unit list, length <c>Count + 1</c>: entry <c>i</c> is the
    /// total units of all characters before index <c>i</c>. Lets <see cref="IsStrokeRevealed"/> run in O(1).
    /// </summary>
    public static int[] CumulativeStrokeUnits(IReadOnlyList<int> perCharStrokeUnits)
    {
        var cumulative = new int[perCharStrokeUnits.Count + 1];
        for (int i = 0; i < perCharStrokeUnits.Count; i++)
        {
            cumulative[i + 1] = cumulative[i] + perCharStrokeUnits[i];
        }

        return cumulative;
    }

    /// <summary>
    /// Whether the stroke at (<paramref name="globalCharIndex"/> absolute in the laid-out text,
    /// <paramref name="strokeOrdinal"/>) is revealed. Characters below <paramref name="baselineChars"/> are
    /// always revealed (they must not replay); otherwise it reveals once <paramref name="elapsedMs"/>
    /// reaches <c>(units from the baseline up to this stroke) × PerStrokeMs</c>.
    /// </summary>
    public static bool IsStrokeRevealed(
        int globalCharIndex, int strokeOrdinal, int baselineChars, double elapsedMs,
        RevealSchedule schedule, IReadOnlyList<int> cumulativeStrokeUnits)
    {
        if (globalCharIndex < baselineChars)
        {
            return true;
        }

        int baseUnits = UnitsBefore(cumulativeStrokeUnits, baselineChars);
        int unitsBeforeChar = UnitsBefore(cumulativeStrokeUnits, globalCharIndex);
        double strokeTime = (unitsBeforeChar - baseUnits + strokeOrdinal) * schedule.PerStrokeMs;
        return elapsedMs >= strokeTime;
    }

    /// <summary>
    /// Duration of revealing from <paramref name="baselineChars"/> to the end (units in that run ×
    /// <see cref="RevealSchedule.PerStrokeMs"/>); 0 when nothing new needs revealing.
    /// </summary>
    public static double TotalDurationMs(int baselineChars, IReadOnlyList<int> cumulativeStrokeUnits, RevealSchedule schedule)
    {
        int total = cumulativeStrokeUnits.Count > 0 ? cumulativeStrokeUnits[^1] : 0;
        int baseUnits = UnitsBefore(cumulativeStrokeUnits, baselineChars);
        int newUnits = total - baseUnits;
        return newUnits <= 0 ? 0.0 : newUnits * schedule.PerStrokeMs;
    }

    /// <summary>Cumulative units before <paramref name="charIndex"/>, clamped so an out-of-range index is safe.</summary>
    private static int UnitsBefore(IReadOnlyList<int> cumulativeStrokeUnits, int charIndex)
    {
        if (cumulativeStrokeUnits.Count == 0)
        {
            return 0;
        }

        int idx = Math.Clamp(charIndex, 0, cumulativeStrokeUnits.Count - 1);
        return cumulativeStrokeUnits[idx];
    }
}
