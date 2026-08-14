using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for the typed-arrow substitution rule (add-arrow-substitution-and-cuneiform-glyphs). The whole
// transform — the digraph table AND the caret math — is pure Core, so it is exercised here without the
// editor widget or a game install. The editor is a thin caller that swaps in TryApply's returned
// text/caret; only the in-game render is left to a manual smoke test.
public class ScribeArrowDigraphTests
{
    // --- TryComplete(): the fixed two-entry table ---

    [Theory]
    [InlineData('>', '-', '→')] // -> completes to a right arrow
    [InlineData('-', '<', '←')] // <- completes to a left arrow
    public void TryComplete_KnownDigraphs_ProduceArrows(char justTyped, char before, char expected)
    {
        Assert.True(ScribeArrowDigraph.TryComplete(justTyped, before, out char arrow));
        Assert.Equal(expected, arrow);
    }

    [Theory]
    [InlineData('<', '\0')] // a lone first char (nothing before) never completes
    [InlineData('-', '\0')]
    [InlineData('>', '<')]  // <> is not a digraph
    [InlineData('-', '-')]  // -- is not a digraph
    [InlineData('=', '-')]  // -= is not a digraph
    [InlineData('a', 'b')]  // ordinary text
    public void TryComplete_NonDigraphs_ReturnFalse(char justTyped, char before)
    {
        Assert.False(ScribeArrowDigraph.TryComplete(justTyped, before, out char arrow));
        Assert.Equal('\0', arrow);
    }

    // --- TryApply(): text + caret transform ---

    [Fact]
    public void TryApply_RightArrow_AtEndOfBuffer()
    {
        // "a-" caret after the '-' (index 2); typing '>' collapses to "a→" caret after the arrow.
        Assert.True(ScribeArrowDigraph.TryApply("a-", 2, '>', out string text, out int caret));
        Assert.Equal("a→", text);
        Assert.Equal(2, caret);
    }

    [Fact]
    public void TryApply_LeftArrow_AtEndOfBuffer()
    {
        Assert.True(ScribeArrowDigraph.TryApply("a<", 2, '-', out string text, out int caret));
        Assert.Equal("a←", text);
        Assert.Equal(2, caret);
    }

    [Fact]
    public void TryApply_MidText_WithTrailingContent()
    {
        // "a-b" with caret between '-' and 'b' (index 2); typing '>' → "a→b", caret still before 'b'.
        Assert.True(ScribeArrowDigraph.TryApply("a-b", 2, '>', out string text, out int caret));
        Assert.Equal("a→b", text);
        Assert.Equal(2, caret);
    }

    [Fact]
    public void TryApply_RightAfterAnExistingArrow()
    {
        // A completed arrow immediately followed by another digraph: "→-" caret at end (index 2),
        // typing '>' → "→→".
        Assert.True(ScribeArrowDigraph.TryApply("→-", 2, '>', out string text, out int caret));
        Assert.Equal("→→", text);
        Assert.Equal(2, caret);
    }

    [Fact]
    public void TryApply_LengthNeutral()
    {
        ScribeArrowDigraph.TryApply("x-", 2, '>', out string text, out _);
        Assert.Equal(2, text.Length); // two chars in, two chars out (digraph char replaced, not appended)
    }

    [Fact]
    public void TryApply_DoesNotRewriteADigraphElsewhere()
    {
        // A literal "->" sits earlier; the caret is at the end after a lone '<'. Typing '-' completes only
        // the run at the caret ("<-" → "←"), leaving the earlier "->" untouched.
        Assert.True(ScribeArrowDigraph.TryApply("a->b<", 5, '-', out string text, out int caret));
        Assert.Equal("a->b←", text);
        Assert.Equal(5, caret);
    }

    [Theory]
    [InlineData("", 0, '>')]     // empty buffer, nothing before the caret
    [InlineData("a", 0, '>')]    // caret at start, nothing before it
    [InlineData("a-", 2, '=')]   // '-' before but the typed char isn't '>'
    [InlineData("a- ", 3, '>')]  // a space separates '-' from the caret → not adjacent
    [InlineData("a<", 2, '>')]   // '<' before but '>' doesn't complete "<>"
    public void TryApply_NonTriggers_LeaveTextAndCaretUnchanged(string input, int caret, char justTyped)
    {
        Assert.False(ScribeArrowDigraph.TryApply(input, caret, justTyped, out string text, out int newCaret));
        Assert.Equal(input, text);
        Assert.Equal(caret, newCaret);
    }
}
