namespace Scribe.Core;

/// <summary>
/// The typed-arrow substitution rule shared by every Scribe editor: as the player types, the ASCII
/// digraph <c>-&gt;</c> becomes <c>→</c> (U+2192) and <c>&lt;-</c> becomes <c>←</c> (U+2190). Pure text
/// logic, no VS API — so the whole transform (including the caret math) is unit-testable without a game
/// install; the editor widget (<c>ScribeMultilineFieldState</c>) is a thin caller that swaps in the
/// returned text/caret.
///
/// <para>Design notes (add-arrow-substitution-and-cuneiform-glyphs):</para>
/// <list type="bullet">
/// <item>The table is deliberately FIXED to exactly these two horizontal arrows — this is not a general
/// autocorrect/emoji engine. Vertical/bidirectional arrows and the <c>&lt;-&gt;</c> triple are out of
/// scope.</item>
/// <item>Substitution is length-neutral (two chars → one char), so it never trips a max-length cap and the
/// caller can bypass its length check for this path.</item>
/// <item>Only the single character immediately before the caret is examined, together with the just-typed
/// character — so a matching digraph elsewhere in the buffer is never rewritten, and a mid-text edit is
/// safe.</item>
/// </list>
/// </summary>
public static class ScribeArrowDigraph
{
    /// <summary>U+2192 RIGHTWARDS ARROW — the result of completing <c>-&gt;</c>.</summary>
    public const char RightArrow = '→';

    /// <summary>U+2190 LEFTWARDS ARROW — the result of completing <c>&lt;-</c>.</summary>
    public const char LeftArrow = '←';

    /// <summary>The fixed two-entry digraph table: given the character just typed and the character
    /// immediately before the caret, report whether they complete an arrow digraph and, if so, which arrow
    /// replaces them. <paramref name="justTyped"/> is the SECOND character of the digraph (the completing
    /// keystroke), <paramref name="before"/> the first (already in the buffer). Returns false for anything
    /// else, including a lone first character.</summary>
    public static bool TryComplete(char justTyped, char before, out char arrow)
    {
        // "->" : a '-' already in the buffer, completed by typing '>'.
        if (before == '-' && justTyped == '>') { arrow = RightArrow; return true; }
        // "<-" : a '<' already in the buffer, completed by typing '-'.
        if (before == '<' && justTyped == '-') { arrow = LeftArrow; return true; }
        arrow = '\0';
        return false;
    }

    /// <summary>Apply the substitution for a typed character against the editor buffer, as if
    /// <paramref name="justTyped"/> were about to be inserted at <paramref name="caret"/>. When it completes
    /// a digraph with the character immediately before the caret, returns true with
    /// <paramref name="newText"/>/<paramref name="newCaret"/> holding the buffer with the two-character
    /// digraph replaced by the single arrow and the caret sitting immediately after the arrow (a net
    /// advance of one character across the two keystrokes, not two). Returns false — leaving the outputs
    /// equal to the inputs — when no substitution applies, in which case the caller inserts the character
    /// normally.
    ///
    /// <para>The caller should skip this path when a selection is active (the "character before the caret"
    /// is ambiguous while typing over a selection); this helper does not know about selections.</para></summary>
    public static bool TryApply(string text, int caret, char justTyped, out string newText, out int newCaret)
    {
        text ??= "";
        if (caret > 0 && caret <= text.Length && TryComplete(justTyped, text[caret - 1], out char arrow))
        {
            // Replace the first digraph char (at caret-1) with the arrow; the just-typed char is consumed
            // into it rather than inserted. One char removed and one added at the same spot, so every
            // offset from the caret onward is unchanged and the caret index stays put — now just past the
            // arrow.
            newText = text.Remove(caret - 1, 1).Insert(caret - 1, arrow.ToString());
            newCaret = caret;
            return true;
        }

        newText = text;
        newCaret = caret;
        return false;
    }
}
