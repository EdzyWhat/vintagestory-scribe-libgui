namespace Scribe.Core;

/// <summary>
/// The typed-arrow substitution shared by every Scribe editor: typing <c>-&gt;</c> yields <c>→</c> and
/// <c>&lt;-</c> yields <c>←</c>. The table is deliberately fixed to these two arrows. The substitution
/// is length-neutral, so callers can bypass their max-length check, and only the character before the
/// caret is examined, so digraphs elsewhere in the buffer are never rewritten.
/// </summary>
public static class ScribeArrowDigraph
{
    /// <summary>U+2192, the result of completing <c>-&gt;</c>.</summary>
    public const char RightArrow = '→';

    /// <summary>U+2190, the result of completing <c>&lt;-</c>.</summary>
    public const char LeftArrow = '←';

    /// <summary>Whether <paramref name="before"/> (already in the buffer) plus <paramref name="justTyped"/>
    /// (the completing keystroke) form an arrow digraph, and which arrow replaces them.</summary>
    public static bool TryComplete(char justTyped, char before, out char arrow)
    {
        if (before == '-' && justTyped == '>') { arrow = RightArrow; return true; }
        if (before == '<' && justTyped == '-') { arrow = LeftArrow; return true; }
        arrow = '\0';
        return false;
    }

    /// <summary>Applies the substitution as if <paramref name="justTyped"/> were inserted at
    /// <paramref name="caret"/>. On a completed digraph returns true with the digraph replaced by the arrow
    /// and the caret just after it. Otherwise returns false with outputs equal to the inputs and the caller
    /// inserts the character normally.
    ///
    /// <para>Callers must skip this while a selection is active; it does not know about selections.</para></summary>
    public static bool TryApply(string text, int caret, char justTyped, out string newText, out int newCaret)
    {
        text ??= "";
        if (caret > 0 && caret <= text.Length && TryComplete(justTyped, text[caret - 1], out char arrow))
        {
            // The arrow replaces the char at caret-1 and the typed char is consumed, so offsets from
            // the caret onward are unchanged.
            newText = text.Remove(caret - 1, 1).Insert(caret - 1, arrow.ToString());
            newCaret = caret;
            return true;
        }

        newText = text;
        newCaret = caret;
        return false;
    }
}
