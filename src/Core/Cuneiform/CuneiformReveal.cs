namespace Scribe.Core.Cuneiform;

/// <summary>
/// Timing carrier for the "text presses in as you type" reveal (add-cuneiform-handwriting-feel). Time is a
/// pure running total of STROKES: the clock advances <see cref="PerStrokeMs"/> per stroke-unit, so a
/// complex 5-stroke letter genuinely takes longer to press in than a simple 2-stroke one, and a space is a
/// fixed <see cref="CuneiformReveal.SpaceStrokeUnits"/>-unit pause that draws nothing. There is no per-letter
/// slot — the earlier fixed-slot model (which made every letter take the same wall-clock time regardless of
/// its stroke count) was replaced because it read as flat and un-hand-like. Kept data-only; the Mod passes in
/// the tuned <see cref="PerStrokeMs"/> (from <see cref="CuneiformReveal.PerStrokeMs"/>).
/// </summary>
public readonly struct RevealSchedule
{
    /// <summary>Milliseconds the reveal clock advances per stroke-unit (a glyph's stroke, or a fraction of a
    /// space's pause). The whole model's single speed knob.</summary>
    public double PerStrokeMs { get; }

    public RevealSchedule(double perStrokeMs)
    {
        PerStrokeMs = perStrokeMs;
    }
}

/// <summary>
/// Pure, VS-API-free timing for the stroke-progression reveal. Time is a cumulative count of stroke-units
/// from the baseline: each glyph contributes its authored stroke count, each space
/// <see cref="SpaceStrokeUnits"/>, and a missing glyph <see cref="MissingGlyphStrokeUnits"/>. A stroke's
/// reveal time is <c>(stroke-units before it, from the baseline) × PerStrokeMs</c>. The Mod builds the
/// per-character stroke-unit list from the laid-out text (<see cref="CuneiformLineLayout.StrokeUnitsFor"/>),
/// turns it into a prefix-sum via <see cref="CumulativeStrokeUnits"/>, and passes that in so both
/// <see cref="IsStrokeRevealed"/> (per stroke, in the paint loop) and <see cref="TotalDurationMs"/> (once, to
/// size the animation controller) read the same running total. Deterministic and unit-testable, like the
/// rest of <c>Scribe.Core.Cuneiform</c>.
/// </summary>
public static class CuneiformReveal
{
    /// <summary>The tuned press-in speed (ms per stroke-unit), shared by the editor rows and the title band
    /// so the two can't drift. This is the ONE home for the reveal speed (the Mod field/title states and the
    /// render object all read it). Slowed to 80 ms/stroke per the 2026-08-05 playtest ask.</summary>
    public const double PerStrokeMs = 80.0;

    /// <summary>Stroke-units a space (or any whitespace) contributes — a fixed pause that draws nothing, so a
    /// word break reads as a beat between letters. Two units ≈ a short simple letter's worth of time.</summary>
    public const int SpaceStrokeUnits = 2;

    /// <summary>Stroke-units a character with no authored glyph contributes. It draws nothing (like a space),
    /// so it matches <see cref="SpaceStrokeUnits"/> — an unknown character reads as a small pause rather than
    /// a zero-time skip that would let the next letter press in early.</summary>
    public const int MissingGlyphStrokeUnits = 2;

    /// <summary>
    /// Prefix-sum of a per-character stroke-unit list: the returned array has length
    /// <c>perCharStrokeUnits.Count + 1</c>, where entry <c>i</c> is the total stroke-units of all characters
    /// BEFORE index <c>i</c> (entry 0 is 0; the last entry is the whole line's stroke-unit total). Computing
    /// it once lets <see cref="IsStrokeRevealed"/> answer each stroke in O(1) instead of re-summing a prefix.
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
    /// Whether the stroke belonging to source character <paramref name="globalCharIndex"/> (absolute index in
    /// the whole laid-out text) with glyph-local ordinal <paramref name="strokeOrdinal"/> is revealed yet.
    /// Characters below <paramref name="baselineChars"/> are ALWAYS revealed (they existed before the current
    /// reveal began and must not replay). Otherwise the stroke reveals once <paramref name="elapsedMs"/>
    /// reaches <c>(stroke-units from the baseline up to this stroke) × PerStrokeMs</c>, where the stroke-unit
    /// count comes from <paramref name="cumulativeStrokeUnits"/> (see <see cref="CumulativeStrokeUnits"/>)
    /// plus this stroke's ordinal within its own glyph.
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
    /// How long revealing the run from <paramref name="baselineChars"/> to the end takes: the stroke-units in
    /// that run × <see cref="RevealSchedule.PerStrokeMs"/>. The last stroke reveals at exactly this time
    /// (the sum already covers every stroke — no extra tail is needed, unlike the old fixed-slot model).
    /// Returns 0 when nothing new needs revealing (baseline at or past the end).
    /// </summary>
    public static double TotalDurationMs(int baselineChars, IReadOnlyList<int> cumulativeStrokeUnits, RevealSchedule schedule)
    {
        int total = cumulativeStrokeUnits.Count > 0 ? cumulativeStrokeUnits[^1] : 0;
        int baseUnits = UnitsBefore(cumulativeStrokeUnits, baselineChars);
        int newUnits = total - baseUnits;
        return newUnits <= 0 ? 0.0 : newUnits * schedule.PerStrokeMs;
    }

    /// <summary>Cumulative stroke-units before source character <paramref name="charIndex"/>, clamped to the
    /// valid range of the prefix-sum array so an out-of-range index (e.g. a baseline past the end) is safe.</summary>
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
