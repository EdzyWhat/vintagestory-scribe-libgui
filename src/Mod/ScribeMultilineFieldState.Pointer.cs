using Gui.Core.Framework;
using Gui.Widgets.Events;
using OpenTK.Mathematics;

namespace Scribe;

internal sealed partial class ScribeMultilineFieldState
{
    /// <summary>Maps a pointer event to a source caret offset, or null if render geometry isn't reachable. The
    /// GestureDetector proxy lays the text render out at (0,0) with the same size, so the proxy-local point is
    /// usable directly by <see cref="ScribeMultilineFieldRender.OffsetAtPosition"/>.</summary>
    private int? OffsetAt(PointerEvent e)
    {
        if (Element?.RenderObject is not { } proxy) return null;
        var textRender = ResolveTextRender(proxy);
        if (textRender is null) return null;

        Vector2 local = proxy.GlobalToLocal(new Vector2(e.X, e.Y));
        return Math.Clamp(textRender.OffsetAtPosition(local), 0, text.Length);
    }

    /// <summary>Resolves the editable text render (normal or cuneiform) through the GestureDetector proxy via the
    /// shared <see cref="IScribeEditableTextRender"/> contract.</summary>
    private static IScribeEditableTextRender? ResolveTextRender(RenderObject proxy)
    {
        if (proxy is IScribeEditableTextRender direct) return direct;
        return proxy.Children.Count > 0 ? proxy.Children[0] as IScribeEditableTextRender : null;
    }

    /// <summary>Moves the caret one visual line up (-1) or down (+1); null if render geometry isn't reachable yet.</summary>
    private int? CaretVertical(int direction)
    {
        if (Element?.RenderObject is not { } proxy) return null;
        var textRender = ResolveTextRender(proxy);
        if (textRender is null) return null;

        return Math.Clamp(textRender.CaretOffsetVertical(caret, direction), 0, text.Length);
    }

    /// <summary>Press: focuses the field and moves the caret to the click point, starting a drag selection. A
    /// double click at the same spot selects the word, a triple click the logical line.</summary>
    private void OnFieldPress(PointerEvent e)
    {
        focusNode.RequestFocus();

        if (OffsetAt(e) is not { } offset) return;

        var now = DateTime.Now;
        bool sameSpotInTime = lastClickOffset == offset && (now - lastClickTime) <= MultiClickWindow;
        clickCount = sameSpotInTime ? Math.Min(clickCount + 1, 3) : 1;
        lastClickTime = now;
        lastClickOffset = offset;

        switch (clickCount)
        {
            case 2: // word select
                (anchor, caret) = WordBoundaryAt(offset);
                isSelecting = false; // a word is selected outright; don't also start a drag
                break;
            case 3: // line select
                anchor = LineStart(offset);
                caret = LineEnd(offset);
                isSelecting = false;
                break;
            default: // single click: caret + begin drag-select
                caret = offset;
                anchor = offset; // collapsed; onMove extends from here
                isSelecting = true;
                break;
        }

        RestartCaretBlink();
        MarkNeedsBuild();
        // The caret is already visible where clicked, so the editor must not auto-scroll (programmatic focus goes through FocusEditorRow).
        Widget.OnPointerFocus?.Invoke();
    }

    /// <summary>Word boundaries (start, end) around <paramref name="pos"/>, matching LibGUI's
    /// <c>TextField.FindWordBoundary</c>: a word char expands to its run; a non-word char selects just itself.</summary>
    private (int start, int end) WordBoundaryAt(int pos)
    {
        if (text.Length == 0) return (0, 0);
        pos = Math.Clamp(pos, 0, text.Length - 1);

        int start = pos, end = pos;
        if (IsWordChar(text[pos]))
        {
            while (start > 0 && IsWordChar(text[start - 1])) start--;
            while (end < text.Length - 1 && IsWordChar(text[end + 1])) end++;
            end++; // end is exclusive
        }
        else
        {
            end = pos + 1; // select the single non-word character
        }
        return (start, end);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>Drag while selecting: moves the caret to the cursor, anchor stays at the press point.</summary>
    private void OnFieldMove(PointerEvent e)
    {
        if (!isSelecting) return;
        if (OffsetAt(e) is not { } offset) return;
        if (offset == caret) return; // no caret movement -> no rebuild
        caret = offset;              // anchor unchanged: [min,max] is the live selection
        MarkNeedsBuild();
    }

    /// <summary>Release: ends the drag; caret/anchor hold the final selection.</summary>
    private void OnFieldRelease(PointerEvent e) => isSelecting = false;
}
