// Production multi-line editable field for the LibGUI lectern editor view
// (migrate-editor-view-libgui). Promoted from the change-1 reference probe
// (SpikeScribeMultilineField.cs, now deleted): LibGUI's stock TextField is single-line and
// `internal`, so this REIMPLEMENTS a wrapping, auto-growing, focus-holding editable field on
// LibGUI's PUBLIC API, mirroring LibGUI's own TextField/RenderTextField architecture:
//   • ScribeMultilineFieldRender : RenderBox — wraps text to width, sizes height to line count
//     (the auto-grow), and paints selection + lines + caret via PaintingContext.DrawBox/DrawText.
//   • ScribeMultilineFieldRenderWidget : RenderObjectWidget — the create/update bridge.
//   • ScribeMultilineField : StatefulWidget, IFocusable + its State : IKeyCharHandler,
//     IKeyDownHandler — owns the (text, caret, selection-anchor) model, focuses on click via a
//     GestureDetector, and coordinates cross-row commit/navigation through callbacks.
//
// Keyboard model ported from the native ScribeRowTextInput: Enter = commit-and-advance (never a
// newline), Shift+Enter = hard line break (grows the row), Shift+Tab = commit-and-retreat, Esc
// bubbles (not handled → closes the dialog). Caret nav: Left/Right/Home/End, Ctrl/Alt word-skip,
// Shift extends the selection. Clipboard: Ctrl+A/C/X/V. Because LibGUI's KeyboardEvent carries no
// Command modifier (only Shift/Ctrl/Alt), the macOS Cmd combos (Cmd+Arrow line-ends, Cmd+C/V/X/A)
// are translated into these Windows-style combos one layer up, in the dialog's OnKeyDown override
// (see GuiDialogScribeLecternLibGui) — this widget stays platform-agnostic.

using System;
using System.Collections.Generic;
using System.Text;
using Gui.Core.Framework;         // RenderObject, RenderBox
using Gui.Rendering;              // PaintingContext
using Gui.Rendering.Text;         // TextLayoutHelper, FontWeight
using Gui.Widgets.Events;         // KeyboardEvent, IKeyDownHandler, IKeyCharHandler
using Gui.Widgets.Framework;      // Widget, StatefulWidget, State, RenderObjectWidget, Theme, IFocusable
using Gui.Widgets.Input;          // FocusNode, GestureDetector
using OpenTK.Mathematics;         // Vector2, Vector4
using Vintagestory.API.Client;    // GlKeys

namespace Scribe;

/// <summary>One wrapped display line: its text and the offset of its first character in the source
/// text. <see cref="Start"/> + <see cref="Text"/>.Length gives the offset just past the line's last
/// character; the source separator consumed by a soft wrap (a space) or a hard break (a '\n') sits
/// between one line's end and the next line's <see cref="Start"/>.</summary>
internal readonly record struct ScribeVisualLine(string Text, int Start);

/// <summary>The render object: wraps + paints multi-line text, a selection highlight, and a caret,
/// auto-sizing its height to the wrapped line count.</summary>
internal sealed class ScribeMultilineFieldRender : Gui.Core.Framework.RenderBox
{
    private string text = "";
    private int caret;
    private int selectionAnchor;
    private bool hasFocus;
    private float fontSize = 15f;
    private Vector4 textColor = Vector4.One;
    private Vector4 caretColor = Vector4.One;
    private Vector4 selectionColor = new(0.4f, 0.55f, 0.9f, 0.4f);
    private const float PadX = 8f;
    private const float PadY = 6f;

    // Cached wrap result from the last PerformLayout, reused by PaintInternal so layout and paint
    // agree on the exact line breaks (and their source offsets).
    private readonly List<ScribeVisualLine> visualLines = new();
    private float lineHeight = 18f;

    public string Text { get => text; set => SetProperty(ref text, value ?? "", relayout: true); }
    public int Caret { get => caret; set => SetProperty(ref caret, value, repaint: true); }
    public int SelectionAnchor { get => selectionAnchor; set => SetProperty(ref selectionAnchor, value, repaint: true); }
    public bool FieldHasFocus { get => hasFocus; set => SetProperty(ref hasFocus, value, repaint: true); }
    public float FontSize { get => fontSize; set => SetProperty(ref fontSize, value, relayout: true); }
    public Vector4 TextColor { get => textColor; set => SetProperty(ref textColor, value, repaint: true); }
    public Vector4 CaretColor { get => caretColor; set => SetProperty(ref caretColor, value, repaint: true); }
    public Vector4 SelectionColor { get => selectionColor; set => SetProperty(ref selectionColor, value, repaint: true); }

    protected override void PerformLayout()
    {
        float availWidth = float.IsPositiveInfinity(Constraints.MaxWidth) ? 300f : Constraints.MaxWidth;
        float textWidth = Math.Max(1f, availWidth - PadX * 2);

        WrapInto(visualLines, text, textWidth, fontSize);
        lineHeight = MeasureLineHeight(fontSize);

        float height = visualLines.Count * lineHeight + PadY * 2;
        // Fill the available width (so it looks like a field); height follows content = auto-grow.
        Size = Constraints.Constrain(new Vector2(availWidth, height));
    }

    protected override void PaintInternal(PaintingContext context)
    {
        // Draw the box (fill/border/corners) via the base, which reads Color/BorderThickness/etc.
        base.PaintInternal(context);
        if (context.Canvas == null)
        {
            return;
        }

        int selStart = Math.Min(caret, selectionAnchor);
        int selEnd = Math.Max(caret, selectionAnchor);

        // Selection highlight first, so it sits BEHIND the text (mirrors RenderTextField).
        if (hasFocus && selEnd > selStart)
        {
            for (int i = 0; i < visualLines.Count; i++)
            {
                int lineStart = visualLines[i].Start;
                int lineEnd = lineStart + visualLines[i].Text.Length;
                int a = Math.Max(selStart, lineStart);
                int b = Math.Min(selEnd, lineEnd);
                if (b <= a)
                {
                    continue;
                }

                float x1 = PadX + MeasureWidth(visualLines[i].Text.Substring(0, a - lineStart));
                float x2 = PadX + MeasureWidth(visualLines[i].Text.Substring(0, b - lineStart));
                float y = PadY + i * lineHeight;
                context.DrawBox(new Vector2(x1, y), new Vector2(x2 - x1, lineHeight), selectionColor, Vector4.Zero, 0f, Vector4.Zero);
            }
        }

        // Text baseline: first line sits PadY down; DrawText's Y is the baseline, so add ascent.
        float ascent = lineHeight * 0.8f;
        for (int i = 0; i < visualLines.Count; i++)
        {
            float y = PadY + i * lineHeight + ascent;
            context.DrawText(visualLines[i].Text, new Vector2(PadX, y), fontSize, textColor, "", FontWeight.Normal);
        }

        // Caret: map the flat caret offset onto (line, column) of the wrapped text, then draw a bar.
        if (hasFocus)
        {
            (int line, int col) = CaretToLineCol(caret);
            string upto = line < visualLines.Count ? visualLines[line].Text.Substring(0, Math.Min(col, visualLines[line].Text.Length)) : "";
            float caretX = PadX + MeasureWidth(upto);
            float caretY = PadY + line * lineHeight;
            context.DrawBox(new Vector2(caretX, caretY), new Vector2(2f, lineHeight), caretColor, Vector4.Zero, 0f, Vector4.Zero);
        }
    }

    private float MeasureWidth(string s) =>
        s.Length == 0 ? 0f : TextLayoutHelper.MeasureText(s, "", fontSize, FontWeight.Normal).X;

    // Greedy word-wrap to a pixel width, honoring explicit '\n', recording each visual line's source
    // offset so the caret/selection can map flat offsets onto (line, column). Public API only
    // (MeasureText); LibGUI's BreakIntoLines is internal.
    private static void WrapInto(List<ScribeVisualLine> outLines, string s, float maxWidth, float fontSize)
    {
        outLines.Clear();
        int paragraphStart = 0;
        foreach (var paragraph in s.Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                outLines.Add(new ScribeVisualLine("", paragraphStart));
                paragraphStart += 1; // the consumed '\n'
                continue;
            }

            int lineStart = paragraphStart; // source offset of the current wrapped line's first char
            var current = new StringBuilder();
            int wordStart = paragraphStart;
            foreach (var word in paragraph.Split(' '))
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                float w = TextLayoutHelper.MeasureText(candidate, "", fontSize, FontWeight.Normal).X;
                if (w <= maxWidth || current.Length == 0)
                {
                    if (current.Length == 0)
                    {
                        lineStart = wordStart;
                    }
                    current.Clear();
                    current.Append(candidate);
                }
                else
                {
                    outLines.Add(new ScribeVisualLine(current.ToString(), lineStart));
                    current.Clear();
                    current.Append(word);
                    lineStart = wordStart;
                }
                wordStart += word.Length + 1; // + the space separator
            }
            outLines.Add(new ScribeVisualLine(current.ToString(), lineStart));
            paragraphStart += paragraph.Length + 1; // + the consumed '\n'
        }

        if (outLines.Count == 0)
        {
            outLines.Add(new ScribeVisualLine("", 0));
        }
    }

    private static float MeasureLineHeight(float fontSize)
    {
        float h = TextLayoutHelper.MeasureText("Ag", "", fontSize, FontWeight.Normal).Y;
        return h > 0 ? h : fontSize * 1.2f;
    }

    // Map a flat caret offset in the source text onto a (visualLine, column). A caret that lands in a
    // separator consumed between two lines (a soft-wrap space or a hard '\n') is attached to the end
    // of the earlier line.
    private (int, int) CaretToLineCol(int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        for (int i = 0; i < visualLines.Count; i++)
        {
            int lineStart = visualLines[i].Start;
            int lineEnd = lineStart + visualLines[i].Text.Length;
            if (offset < lineStart)
            {
                // In a consumed separator before this line → end of the previous line.
                int prev = Math.Max(0, i - 1);
                return (prev, visualLines[prev].Text.Length);
            }
            if (offset <= lineEnd)
            {
                return (i, offset - lineStart);
            }
        }
        int last = Math.Max(0, visualLines.Count - 1);
        return (last, visualLines.Count > 0 ? visualLines[last].Text.Length : 0);
    }
}

/// <summary>RenderObjectWidget bridge: pushes text/caret/selection/focus/colors into the render object.</summary>
internal sealed class ScribeMultilineFieldRenderWidget : RenderObjectWidget
{
    public ScribeMultilineFieldRenderWidget(string text, int caret, int selectionAnchor, bool hasFocus,
        float fontSize, Vector4 textColor, Vector4 caretColor, Vector4 selectionColor, Vector4 boxColor,
        Vector4 borderColor, float borderThickness, Vector4 cornerRadii)
    {
        Text = text;
        Caret = caret;
        SelectionAnchor = selectionAnchor;
        HasFocus = hasFocus;
        FontSize = fontSize;
        TextColor = textColor;
        CaretColor = caretColor;
        SelectionColor = selectionColor;
        BoxColor = boxColor;
        BorderColor = borderColor;
        BorderThickness = borderThickness;
        CornerRadii = cornerRadii;
    }

    public string Text { get; }
    public int Caret { get; }
    public int SelectionAnchor { get; }
    public bool HasFocus { get; }
    public float FontSize { get; }
    public Vector4 TextColor { get; }
    public Vector4 CaretColor { get; }
    public Vector4 SelectionColor { get; }
    public Vector4 BoxColor { get; }
    public Vector4 BorderColor { get; }
    public float BorderThickness { get; }
    public Vector4 CornerRadii { get; }

    public override RenderObject CreateRenderObject() => new ScribeMultilineFieldRender();

    public override void UpdateRenderObject(RenderObject renderObject)
    {
        var ro = (ScribeMultilineFieldRender)renderObject;
        ro.Text = Text;
        ro.Caret = Caret;
        ro.SelectionAnchor = SelectionAnchor;
        ro.FieldHasFocus = HasFocus;
        ro.FontSize = FontSize;
        ro.TextColor = TextColor;
        ro.CaretColor = CaretColor;
        ro.SelectionColor = SelectionColor;
        ro.Color = BoxColor;
        ro.BorderColor = BorderColor;
        ro.BorderThickness = BorderThickness;
        ro.CornerRadii = CornerRadii;
    }
}

/// <summary>
/// A focusable multi-line editable field with a (text, caret, selection) model. Owns its text while
/// alive (the editor's scratch document mirrors it via <see cref="OnChanged"/>); a full dialog
/// rebuild recreates the field, re-seeding from <see cref="InitialText"/>.
/// </summary>
public sealed class ScribeMultilineField : StatefulWidget, IFocusable
{
    public ScribeMultilineField(
        string initialText = "",
        FocusNode? focusNode = null,
        float fontSize = 15f,
        bool autoFocus = false,
        Action<string>? onChanged = null,
        Action? onCommitAndAdvance = null,
        Action? onCommitAndRetreat = null,
        Action? onInsertTaskBelow = null,
        Action? onBlur = null,
        Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        InitialText = initialText;
        FocusNode = focusNode;
        FontSize = fontSize;
        AutoFocus = autoFocus;
        OnChanged = onChanged;
        OnCommitAndAdvance = onCommitAndAdvance;
        OnCommitAndRetreat = onCommitAndRetreat;
        OnInsertTaskBelow = onInsertTaskBelow;
        OnBlur = onBlur;
    }

    public string InitialText { get; }
    public FocusNode? FocusNode { get; }
    public float FontSize { get; }
    /// <summary>Request focus as soon as this field mounts. LibGUI has no focus-traversal API, so the
    /// editor content coordinates focus among rows manually; a freshly built row that should be
    /// focused (e.g. after Add Task or entering editor mode) sets this to focus itself on mount.</summary>
    public bool AutoFocus { get; }
    public Action<string>? OnChanged { get; }
    /// <summary>Tab (no Shift): the field has committed its text via <see cref="OnChanged"/>; the
    /// parent should normalize + flush the row and move focus to the next row.</summary>
    public Action? OnCommitAndAdvance { get; }
    /// <summary>Shift+Tab: same as advance but focus moves to the previous row.</summary>
    public Action? OnCommitAndRetreat { get; }
    /// <summary>Enter (no Shift): commit this row, then insert a NEW task directly beneath it and focus
    /// it. Shift+Enter still inserts a hard line break within the row.</summary>
    public Action? OnInsertTaskBelow { get; }
    /// <summary>Focus lost without an Enter/Shift+Tab (e.g. click-away): commit the row's edit.</summary>
    public Action? OnBlur { get; }

    public override State CreateState() => new ScribeMultilineFieldState();
}

internal sealed class ScribeMultilineFieldState : State<ScribeMultilineField>, IKeyCharHandler, IKeyDownHandler
{
    private string text = "";
    private int caret;
    private int anchor; // selection is [min(anchor,caret), max(anchor,caret)]; collapsed when equal
    private FocusNode focusNode = null!;
    private FocusNode? internalFocusNode;
    private bool hadFocus;

    public override void InitState()
    {
        base.InitState();
        text = Widget.InitialText;
        caret = text.Length;
        anchor = caret;
        focusNode = Widget.FocusNode ?? (internalFocusNode = new FocusNode());
        // FocusNode.RequestFocus() resolves its manager via Owner?.Owner?.FocusManager; without an
        // Owner it can't reach the manager, focus never takes, and the HasFocus-gated key handlers
        // never fire (banked lesson from the change-1 spike). Element is set by the time InitState runs.
        focusNode.Owner = Element;
        focusNode.AddListener(OnFocusChanged);
        hadFocus = focusNode.HasFocus;

        if (Widget.AutoFocus)
        {
            focusNode.RequestFocus();
        }
    }

    private void OnFocusChanged()
    {
        // A genuine focus loss (not a rebuild) commits the row's pending edit, mirroring the native
        // editor's blur-commit. Guard so we only fire on a true has→hasn't transition.
        bool now = focusNode.HasFocus;
        if (hadFocus && !now)
        {
            Widget.OnBlur?.Invoke();
        }
        hadFocus = now;
        MarkNeedsBuild(); // repaint caret + focus border on focus change
    }

    public override void Dispose()
    {
        focusNode.RemoveListener(OnFocusChanged);
        internalFocusNode?.Dispose();
        base.Dispose();
    }

    public void OnKeyChar(KeyboardEvent e)
    {
        if (!focusNode.HasFocus || e.KeyChar == '\0' || char.IsControl(e.KeyChar))
        {
            return;
        }
        e.Handled = true;
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
                    // Shift+Enter inserts a hard line break (grows the row).
                    Insert("\n");
                }
                else
                {
                    // Enter commits this row and inserts a NEW task directly beneath it (never a
                    // newline within the row). Row navigation is Tab's job now.
                    Widget.OnInsertTaskBelow?.Invoke();
                }
                Handled(e);
                break;

            case (int)GlKeys.Tab when e.Shift:
                // Shift+Tab commits and retreats to the previous row.
                Widget.OnCommitAndRetreat?.Invoke();
                Handled(e);
                break;

            case (int)GlKeys.Tab:
                // Tab commits and advances to the next row (never inserts a tab glyph).
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

            case (int)GlKeys.Home:
                MoveCaret(LineStart(caret), e.Shift);
                Handled(e);
                break;

            case (int)GlKeys.End:
                MoveCaret(LineEnd(caret), e.Shift);
                Handled(e);
                break;

            // Esc intentionally NOT handled — it bubbles up and closes the dialog (panic-close);
            // the blur-commit on close still saves the pending edit.
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
        MarkNeedsBuild();
    }

    private void Insert(string s)
    {
        if (HasSelection)
        {
            DeleteSelection();
        }
        caret = Math.Clamp(caret, 0, text.Length);
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

    // Word-skip: from caret, skip any whitespace, then skip the run of non-whitespace.
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

    // Home/End operate on the logical line (delimited by '\n'), matching desktop line-end idioms.
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
        Widget.OnChanged?.Invoke(text);
        MarkNeedsBuild();
    }

    private void MarkNeedsBuild() => SetState(() => { });

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        return new GestureDetector(
            onPress: _ => focusNode.RequestFocus(),
            child: new ScribeMultilineFieldRenderWidget(
                text: text,
                caret: caret,
                selectionAnchor: anchor,
                hasFocus: focusNode.HasFocus,
                fontSize: Widget.FontSize,
                textColor: colors.OnSurface,
                caretColor: colors.Primary,
                selectionColor: colors.Primary with { W = 0.35f },
                boxColor: colors.SurfaceHigh,
                borderColor: focusNode.HasFocus ? colors.Primary : colors.Border,
                borderThickness: 1f,
                cornerRadii: Vector4.One * 4f));
    }
}
