namespace Scribe.Core.Cuneiform;

/// <summary>
/// Per-letter stroke-progression timing for the "text presses in as you type" reveal
/// (add-cuneiform-handwriting-feel). The strokes of a single letter appear in quick succession
/// (<see cref="PerStrokeMs"/> apart), and there is a longer pause before the next letter begins
/// (<see cref="PerLetterMs"/> apart) — so a word looks hand-pressed rather than stamped whole. Kept small
/// and data-only; the Mod layer owns the concrete numbers (tuned in-game) and passes them in.
/// </summary>
public readonly struct RevealSchedule
{
    /// <summary>Milliseconds between successive strokes WITHIN one letter — the fast part.</summary>
    public double PerStrokeMs { get; }

    /// <summary>Milliseconds between the START of one letter and the START of the next — the longer,
    /// gap-producing part. Kept larger than a letter's worth of <see cref="PerStrokeMs"/> so a visible
    /// pause falls between letters.</summary>
    public double PerLetterMs { get; }

    public RevealSchedule(double perStrokeMs, double perLetterMs)
    {
        PerStrokeMs = perStrokeMs;
        PerLetterMs = perLetterMs;
    }
}

/// <summary>
/// Pure, VS-API-free timing for the stroke-progression reveal. Given a stroke's stable identity (its source
/// character index and glyph-local ordinal — see <see cref="PositionedStroke"/>), how many leading characters
/// are already fully revealed (the baseline, which never re-animates), and the elapsed animation time, it
/// answers "is this stroke visible yet?". The Mod render objects call <see cref="IsStrokeRevealed"/> in their
/// paint loop; the Mod State drives <c>elapsedMs</c> with an animation controller. Deterministic and
/// unit-testable, exactly like the rest of <c>Scribe.Core.Cuneiform</c>.
/// </summary>
public static class CuneiformReveal
{
    /// <summary>
    /// Whether the stroke belonging to source character <paramref name="globalCharIndex"/> (absolute index in
    /// the whole laid-out text) with glyph-local ordinal <paramref name="strokeOrdinal"/> is revealed yet.
    /// Characters below <paramref name="baselineChars"/> are ALWAYS revealed (they existed before the current
    /// reveal began and must not replay). A character at or above the baseline starts revealing at
    /// <c>(globalCharIndex - baselineChars) * PerLetterMs</c>, and its strokes follow at
    /// <c>+ strokeOrdinal * PerStrokeMs</c>, so within-letter strokes appear fast and letters are gapped.
    /// </summary>
    public static bool IsStrokeRevealed(
        int globalCharIndex, int strokeOrdinal, int baselineChars, double elapsedMs, RevealSchedule schedule)
    {
        if (globalCharIndex < baselineChars)
        {
            return true;
        }

        int letterOffset = globalCharIndex - baselineChars;
        double strokeTime = letterOffset * schedule.PerLetterMs + strokeOrdinal * schedule.PerStrokeMs;
        return elapsedMs >= strokeTime;
    }

    /// <summary>
    /// A generous upper bound (ms) on how long revealing characters <paramref name="baselineChars"/>..
    /// <paramref name="totalChars"/> takes, used to size the animation controller's duration. It assumes each
    /// letter occupies a <see cref="RevealSchedule.PerLetterMs"/> slot and adds a fixed tail so a letter with
    /// many strokes still finishes inside the window; the caller snaps to fully-revealed on completion, so a
    /// slight over-estimate only means the animation ends a touch early relative to the clock, never that a
    /// stroke is left hidden. Returns 0 when nothing new needs revealing.
    /// </summary>
    public static double TotalDurationMs(int baselineChars, int totalChars, RevealSchedule schedule)
    {
        int newChars = totalChars - baselineChars;
        if (newChars <= 0)
        {
            return 0.0;
        }

        // Last letter starts at (newChars-1)*PerLetterMs; add one more letter-slot of tail so its strokes
        // (however many) complete well inside the window.
        return (double)newChars * schedule.PerLetterMs;
    }
}
