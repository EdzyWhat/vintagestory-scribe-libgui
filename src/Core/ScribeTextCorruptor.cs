namespace Scribe.Core;

/// <summary>
/// Zalgo-style text corruption for the pinned-task HUD's temporal-instability effect
/// (hud-temporal-storm-corruption). Reproduces vanilla's "crazed" temporal-storm chat effect —
/// <c>EntityBehaviorTemporalStabilityAffected.destabilizeText</c> in VSSurvivalMod, which is
/// <c>private</c> and so not callable — by walking the string and, per base character, with
/// probability equal to the corruption strength, appending one random combining diacritic mark from
/// the same fixed 23-element code-point set vanilla uses. The marks stack over the preceding glyph
/// through the ordinary font path, so no GPU shader is involved.
///
/// <para>Pure and game-agnostic (no Vintage Story API): the randomness is driven by a caller-supplied
/// <paramref name="seed"/> via <see cref="System.Random"/>, so a given (text, strength, seed) always
/// produces the same output. That determinism is what makes it unit-testable without a game install
/// (the load-bearing <c>src/Core</c> invariant) and lets the Mod layer re-scramble simply by advancing
/// the seed. The Mod layer owns picking seeds and deciding the strength each refresh.</para>
/// </summary>
public static class ScribeTextCorruptor
{
    /// <summary>The exact combining-mark code points vanilla injects (decompiled from
    /// <c>destabilizeText</c>): a fixed 23-element set in the U+0300 combining-diacritics range plus
    /// U+0489 (combining cyrillic millions sign). Reused verbatim so Scribe's corruption reads at the
    /// same intensity and character as the game's own storm chat, and so it renders cleanly in the
    /// bundled fonts (these already render in the game's font pipeline).</summary>
    public static readonly char[] CombiningMarks =
    {
        '̕', '̛', '̀', '́', '͘', '̡', '̢', '̧', '̨', '̴',
        '̵', '̶', '͏', '͜', '͝', '͞', '͟', '͠', '͢', '̸',
        '̷', '͡', '҉',
    };

    /// <summary>
    /// Returns <paramref name="text"/> with a random combining mark injected after each base character
    /// at a per-character probability of <paramref name="strength"/> (clamped to 0..1). A
    /// <paramref name="strength"/> of 0 (or a null/empty input) returns the input unchanged; a strength
    /// of 1 marks every base character. Deterministic for a given <paramref name="seed"/>.
    /// </summary>
    /// <remarks>
    /// Mirrors vanilla's walk: each source character is emitted, then — if it is not itself already a
    /// combining mark — a mark is appended with probability <paramref name="strength"/>. Base characters
    /// are always preserved and never dropped; the transform only inserts marks. A character that is
    /// already one of <see cref="CombiningMarks"/> is passed through without a further roll, so
    /// re-corrupting already-corrupted text doesn't compound endlessly on the existing marks.
    /// </remarks>
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
            // Don't stack a mark onto an existing mark (matches vanilla's guard); only roll for base chars.
            if (Array.IndexOf(CombiningMarks, c) < 0 && rand.NextDouble() < strength)
            {
                sb.Append(CombiningMarks[rand.Next(CombiningMarks.Length)]);
            }
        }

        return sb.ToString();
    }
}
