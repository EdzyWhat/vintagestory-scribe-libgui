using Gui.Widgets.Events;
using Vintagestory.API.Client;

namespace Scribe;

internal sealed partial class ScribeMultilineFieldState
{
    public void OnKeyChar(KeyboardEvent e)
    {
        if (!focusNode.HasFocus || e.KeyChar == '\0' || char.IsControl(e.KeyChar))
        {
            return;
        }
        e.Handled = true;

        // Typed-arrow substitution: completing `->`/`<-` with the char before the caret replaces the pair with
        // the Unicode arrow. Typing only (never paste), not over a selection; length-neutral so it bypasses the max-length clamp.
        if (!HasSelection
            && Scribe.Core.ScribeArrowDigraph.TryApply(text, caret, e.KeyChar, out string substituted, out int newCaret))
        {
            text = substituted;
            caret = newCaret;
            anchor = caret;
            Commit();
            return;
        }

        Insert(e.KeyChar.ToString());
    }

    public void OnKeyDown(KeyboardEvent e)
    {
        if (!focusNode.HasFocus)
        {
            return;
        }

        bool wholeWord = e.Ctrl || e.Alt; // Ctrl (Windows) or Alt/Option (Mac, delivered as Alt)

        switch (e.KeyCode)
        {
            case (int)GlKeys.A when e.Ctrl:
                anchor = 0;
                caret = text.Length;
                MarkNeedsBuild(); // select-all has no other rebuild trigger
                Handled(e);
                break;

            case (int)GlKeys.C when e.Ctrl:
                CopySelection();
                Handled(e);
                break;

            case (int)GlKeys.X when e.Ctrl:
                if (HasSelection)
                {
                    CopySelection();
                    DeleteSelection();
                    Commit();
                }
                Handled(e);
                break;

            case (int)GlKeys.V when e.Ctrl:
                Paste();
                Handled(e);
                break;

            case (int)GlKeys.BackSpace:
                if (HasSelection)
                {
                    DeleteSelection();
                }
                else if (caret > 0)
                {
                    text = text.Remove(caret - 1, 1);
                    caret--;
                }
                anchor = caret;
                Commit();
                Handled(e);
                break;

            case (int)GlKeys.Delete:
                if (HasSelection)
                {
                    DeleteSelection();
                }
                else if (caret < text.Length)
                {
                    text = text.Remove(caret, 1);
                }
                anchor = caret;
                Commit();
                Handled(e);
                break;

            case (int)GlKeys.Enter:
            case (int)GlKeys.KeypadEnter:
                if (e.Shift)
                {
                    Insert("\n");
                }
                else
                {
                    // Never a newline within the row.
                    Widget.OnInsertTaskBelow?.Invoke();
                }
                Handled(e);
                break;

            case (int)GlKeys.Tab when e.Shift:
                Widget.OnCommitAndRetreat?.Invoke();
                Handled(e);
                break;

            case (int)GlKeys.Tab:
                // Never inserts a tab glyph.
                Widget.OnCommitAndAdvance?.Invoke();
                Handled(e);
                break;

            case (int)GlKeys.Left:
                MoveCaret(wholeWord ? WordLeft(caret) : Math.Max(0, caret - 1), e.Shift);
                Handled(e);
                break;

            case (int)GlKeys.Right:
                MoveCaret(wholeWord ? WordRight(caret) : Math.Min(text.Length, caret + 1), e.Shift);
                Handled(e);
                break;

            case (int)GlKeys.Up:
                // Ctrl+Up = document top (macOS Cmd+Up is remapped to it in ScribeDialogBase.OnKeyDown).
                // Ctrl alone: Alt/Option+Up stays a one-line move.
                if (e.Ctrl)
                {
                    Widget.OnJumpToFirstRow?.Invoke();
                }
                else if (CaretVertical(-1) is { } up)
                {
                    MoveCaret(up, e.Shift);
                }
                Handled(e);
                break;

            case (int)GlKeys.Down:
                // Ctrl+Down = last row (see Up).
                if (e.Ctrl)
                {
                    Widget.OnJumpToLastRow?.Invoke();
                }
                else if (CaretVertical(+1) is { } down)
                {
                    MoveCaret(down, e.Shift);
                }
                Handled(e);
                break;

            case (int)GlKeys.Home:
                // Ctrl+Home = document top; plain Home = line start (Cmd+Left is remapped to plain Home).
                if (e.Ctrl)
                {
                    Widget.OnJumpToFirstRow?.Invoke();
                }
                else
                {
                    MoveCaret(LineStart(caret), e.Shift);
                }
                Handled(e);
                break;

            case (int)GlKeys.End:
                // Ctrl+End = document bottom; plain End = line end.
                if (e.Ctrl)
                {
                    Widget.OnJumpToLastRow?.Invoke();
                }
                else
                {
                    MoveCaret(LineEnd(caret), e.Shift);
                }
                Handled(e);
                break;

            // Esc is intentionally unhandled: it bubbles and closes the dialog; the blur-commit saves the edit.
        }
    }

    private static void Handled(KeyboardEvent e) => e.Handled = true;

    private bool HasSelection => caret != anchor;

    private void MoveCaret(int newCaret, bool extendSelection)
    {
        caret = Math.Clamp(newCaret, 0, text.Length);
        if (!extendSelection)
        {
            anchor = caret;
        }
        RestartCaretBlink();
        MarkNeedsBuild();
        // Keyboard caret moves never fire OnChanged (the editor's scroll-follow trigger), so notify explicitly.
        // Click-to-place goes through OnFieldPress, not here.
        Widget.OnCaretMoved?.Invoke();
    }

    /// <summary>Snaps the caret to the buffer start or end, collapsing any selection. The dialog calls it on the
    /// row focused by a first-/last-row jump; mirrors <see cref="MoveCaret"/>'s blink reset, repaint and scroll-follow notify.</summary>
    public void PlaceCaretAtEdge(bool atStart)
    {
        caret = atStart ? 0 : text.Length;
        anchor = caret;
        RestartCaretBlink();
        MarkNeedsBuild();
        Widget.OnCaretMoved?.Invoke();
    }

    private void Insert(string s)
    {
        if (HasSelection)
        {
            DeleteSelection();
        }
        caret = Math.Clamp(caret, 0, text.Length);
        // Clamp the run to the remaining room, so a long paste truncates rather than being blocked.
        if (Widget.MaxLength is { } max)
        {
            int room = Math.Max(0, max - text.Length);
            // Notify when the cap bites: truncated, or no room at all.
            if (s.Length > room)
            {
                s = s.Substring(0, room);
                Widget.OnMaxLengthReached?.Invoke();
            }
            if (s.Length == 0) return;
        }
        text = text.Insert(caret, s);
        caret += s.Length;
        anchor = caret;
        Commit();
    }

    private void DeleteSelection()
    {
        int start = Math.Min(caret, anchor);
        int end = Math.Max(caret, anchor);
        text = text.Remove(start, end - start);
        caret = start;
        anchor = start;
    }

    private void CopySelection()
    {
        if (!HasSelection)
        {
            return;
        }
        int start = Math.Min(caret, anchor);
        int end = Math.Max(caret, anchor);
        Element?.Owner?.GetClipboard()?.SetText(text.Substring(start, end - start));
    }

    private void Paste()
    {
        string? clip = Element?.Owner?.GetClipboard()?.GetText();
        if (string.IsNullOrEmpty(clip))
        {
            return;
        }
        Insert(clip);
    }

    // Word-skip: skip whitespace, then the run of non-whitespace.
    private int WordLeft(int from)
    {
        int i = from;
        while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--;
        while (i > 0 && !char.IsWhiteSpace(text[i - 1])) i--;
        return i;
    }

    private int WordRight(int from)
    {
        int i = from;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
        return i;
    }

    // Home/End operate on the logical line (delimited by '\n').
    private int LineStart(int from)
    {
        int i = Math.Clamp(from, 0, text.Length);
        int nl = text.LastIndexOf('\n', Math.Max(0, i - 1));
        return nl < 0 ? 0 : nl + 1;
    }

    private int LineEnd(int from)
    {
        int i = Math.Clamp(from, 0, text.Length);
        int nl = text.IndexOf('\n', i);
        return nl < 0 ? text.Length : nl;
    }

    private void Commit()
    {
        UpdateReveal();
        Widget.OnChanged?.Invoke(text);
        RestartCaretBlink();
        MarkNeedsBuild();
    }
}
