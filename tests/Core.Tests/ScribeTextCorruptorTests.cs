using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for the pure Zalgo-style text corruptor backing the HUD's temporal-instability effect
// (hud-temporal-storm-corruption). All game-agnostic: the corruptor is seed-driven, so its behavior is
// fully deterministic and testable without a game install.
public class ScribeTextCorruptorTests
{
    private const string Sample = "Survive the Storm";

    [Fact]
    public void StrengthZero_ReturnsInputUnchanged()
    {
        Assert.Equal(Sample, ScribeTextCorruptor.Corrupt(Sample, 0.0, seed: 1));
    }

    [Fact]
    public void NegativeStrength_ClampsToZero_ReturnsInputUnchanged()
    {
        Assert.Equal(Sample, ScribeTextCorruptor.Corrupt(Sample, -5.0, seed: 1));
    }

    [Fact]
    public void StrengthOne_MarksEveryCharacter()
    {
        string result = ScribeTextCorruptor.Corrupt(Sample, 1.0, seed: 1);

        // Every base character gets exactly one mark appended (none is already a mark), so the output is
        // exactly twice the length and alternates base, mark, base, mark, ...
        Assert.Equal(Sample.Length * 2, result.Length);
        for (int i = 0; i < Sample.Length; i++)
        {
            Assert.Equal(Sample[i], result[i * 2]);
            Assert.Contains(result[i * 2 + 1], ScribeTextCorruptor.CombiningMarks);
        }
    }

    [Fact]
    public void SameSeed_ProducesSameOutput()
    {
        string a = ScribeTextCorruptor.Corrupt(Sample, 0.5, seed: 42);
        string b = ScribeTextCorruptor.Corrupt(Sample, 0.5, seed: 42);

        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentOutput()
    {
        string a = ScribeTextCorruptor.Corrupt(Sample, 0.5, seed: 1);
        string b = ScribeTextCorruptor.Corrupt(Sample, 0.5, seed: 2);

        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullOrEmpty_ReturnsEmpty(string? input)
    {
        Assert.Equal(string.Empty, ScribeTextCorruptor.Corrupt(input, 1.0, seed: 1));
    }

    [Fact]
    public void Whitespace_IsHandled_AndBaseCharsPreserved()
    {
        const string ws = "   ";
        string result = ScribeTextCorruptor.Corrupt(ws, 1.0, seed: 1);

        // The three spaces are preserved (each followed by a mark at strength 1); stripping marks yields
        // the original whitespace back.
        Assert.Equal(ws, StripMarks(result));
    }

    [Fact]
    public void BaseCharacters_ArePreserved_MarksOnlyInserted()
    {
        // Regardless of strength/seed, removing the injected marks recovers the exact input — the
        // transform never drops or alters a base character, it only inserts marks between them.
        foreach (int seed in new[] { 1, 7, 999 })
        {
            string result = ScribeTextCorruptor.Corrupt(Sample, 0.7, seed);
            Assert.Equal(Sample, StripMarks(result));
        }
    }

    // Removes any combining mark from the corruptor's set, leaving the base characters.
    private static string StripMarks(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (Array.IndexOf(ScribeTextCorruptor.CombiningMarks, c) < 0) sb.Append(c);
        }
        return sb.ToString();
    }
}
