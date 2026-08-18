using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for TSV field escaping: structural quoting (tabs/newlines/quotes/edge-space) and
// spreadsheet formula-injection defang. Every case must round-trip exactly.
public class ScribeTsvSafeTests
{
    [Theory]
    [InlineData("plain text")]
    [InlineData("")]
    [InlineData("game:ingot-copper")]
    [InlineData("with \"quotes\" inside")]
    [InlineData("tab\there")]
    [InlineData("line\nbreak")]
    [InlineData("carriage\rreturn")]
    [InlineData("  leading and trailing  ")]
    [InlineData("=formula")]
    [InlineData("+plus")]
    [InlineData("-minus")]
    [InlineData("@at")]
    [InlineData("'already an apostrophe")]
    [InlineData("'=apostrophe then equals")]
    [InlineData("normal - dash in middle is fine")]
    public void EscapeThenUnescape_RoundTripsExactly(string original)
    {
        string escaped = ScribeTsvSafe.Escape(original);
        string restored = ScribeTsvSafe.Unescape(escaped);

        Assert.Equal(original, restored);
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+cmd")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1)")]
    public void Escape_DefangsFormulaLeadingCells(string original)
    {
        string escaped = ScribeTsvSafe.Escape(original);

        // A defanged cell no longer begins with the formula trigger (a spreadsheet won't evaluate it)...
        Assert.StartsWith("'", escaped.TrimStart('"'));
        // ...and the original text comes back on import.
        Assert.Equal(original, ScribeTsvSafe.Unescape(escaped));
    }

    [Fact]
    public void Escape_QuotesFieldsWithStructuralCharacters()
    {
        Assert.StartsWith("\"", ScribeTsvSafe.Escape("has\ttab"));
        Assert.StartsWith("\"", ScribeTsvSafe.Escape("has\nnewline"));
        Assert.StartsWith("\"", ScribeTsvSafe.Escape(" edge space"));
        // A plain field is left bare.
        Assert.Equal("plain", ScribeTsvSafe.Escape("plain"));
    }
}
