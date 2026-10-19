namespace Scribe.Core;

/// <summary>
/// Zalgo-style text corruption for the pinned-task HUD's temporal-instability effect. Reimplements
/// vanilla's private <c>EntityBehaviorTemporalStabilityAffected.destabilizeText</c> (VSSurvivalMod): per
/// base character, with probability equal to the strength, append one random combining mark from the
/// same fixed 23-code-point set. The marks render through the ordinary font path.
///
/// <para>Pure (no Vintage Story API) and deterministic: a given (text, strength, seed) always yields the
/// same output, so the Mod layer re-scrambles by advancing the seed.</para>
/// </summary>
public static class ScribeTextCorruptor
{
    /// <summary>The combining-mark code points vanilla injects, reused verbatim so the effect matches the
    /// game's own storm chat.</summary>
    public static readonly char[] CombiningMarks =
    {
        '̕', '̛', '̀', '́', '͘', '̡', '̢', '̧', '̨', '̴',
        '̵', '̶', '͏', '͜', '͝', '͞', '͟', '͠', '͢', '̸',
        '̷', '͡', '҉',
    };

    /// <summary>
    /// Returns <paramref name="text"/> with a random combining mark appended after each base character at
    /// probability <paramref name="strength"/> (clamped to 0..1). Strength 0 or null/empty input returns the
    /// input unchanged. Only inserts marks; characters already in <see cref="CombiningMarks"/> get no
    /// further roll, so re-corrupting does not pile onto existing marks.
    /// </summary>
    public static string Corrupt(string? text, double strength, int seed)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

        strength = Math.Clamp(strength, 0.0, 1.0);
        if (strength <= 0.0) return text;

        var rand = new Random(seed);
        var sb = new System.Text.StringBuilder(text.Length * 2);

        foreach (char c in text)
        {
            sb.Append(c);
            if (Array.IndexOf(CombiningMarks, c) < 0 && rand.NextDouble() < strength)
            {
                sb.Append(CombiningMarks[rand.Next(CombiningMarks.Length)]);
            }
        }

        return sb.ToString();
    }
}
