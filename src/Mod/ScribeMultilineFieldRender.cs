using System.Text;
using Gui.Core.Framework;
using Gui.Rendering;
using Gui.Rendering.Text;
using Gui.Widgets.Framework;
using OpenTK.Mathematics;

namespace Scribe;

/// <summary>One wrapped display line: its text and the offset of its first character in the source text.
/// The separator consumed by a soft wrap (space) or hard break ('\n') sits between one line's end and the
/// next line's <see cref="Start"/>.</summary>
internal readonly record struct ScribeVisualLine(string Text, int Start);

/// <summary>The render object: wraps + paints multi-line text, a selection highlight, and a caret,
/// auto-sizing its height to the wrapped line count.</summary>
internal sealed class ScribeMultilineFieldRender : Gui.Core.Framework.RenderBox, IScribeEditableTextRender
{
    /// <summary>Font family for BOTH measuring and drawing. It must match the read view's family for the same
    /// row, or the views get different line metrics (row heights and scroll restore drift). "" resolves to a
    /// different typeface than "sans-serif" (~2px mismatch), so the setter coalesces null/empty to it.</summary>
    private string fontFamily = "sans-serif";

    private string text = "";
    private string placeholder = "";
    private int caret;
    private int selectionAnchor;
    private bool hasFocus;
    private bool caretVisible = true;
    private float fontSize = 15f;
    private Vector4 textColor = Vector4.One;
    private Vector4 placeholderColor = new(1f, 1f, 1f, 0.4f);
    private Vector4 caretColor = Vector4.One;
    private Vector4 selectionColor = new(0.4f, 0.55f, 0.9f, 0.4f);
    private float padX = 8f;
    private float padY = 6f;

    // Wrap result from the last PerformLayout, reused by PaintInternal so both agree on line breaks.
    private readonly List<ScribeVisualLine> visualLines = new();
    private float lineHeight = 18f;

    public string Text { get => text; set => SetProperty(ref text, value ?? "", relayout: true); }
    /// <summary>Dimmed hint painted only when the field is empty; not part of the editable text.</summary>
    public string Placeholder { get => placeholder; set => SetProperty(ref placeholder, value ?? "", repaint: true); }
    public int Caret { get => caret; set => SetProperty(ref caret, value, repaint: true); }
    public int SelectionAnchor { get => selectionAnchor; set => SetProperty(ref selectionAnchor, value, repaint: true); }
    public bool FieldHasFocus { get => hasFocus; set => SetProperty(ref hasFocus, value, repaint: true); }
    /// <summary>Blink gate: false paints no caret (the OFF half of the blink). Focus still gates whether a
    /// caret exists; a live selection keeps it solid.</summary>
    public bool CaretVisible { get => caretVisible; set => SetProperty(ref caretVisible, value, repaint: true); }
    public float FontSize { get => fontSize; set => SetProperty(ref fontSize, value, relayout: true); }
    /// <summary>Task-text font family; null/empty coalesces to "sans-serif". Changing it relayouts.</summary>
    public string FontFamily { get => fontFamily; set => SetProperty(ref fontFamily, string.IsNullOrEmpty(value) ? "sans-serif" : value, relayout: true); }
    public float PadX { get => padX; set => SetProperty(ref padX, value, relayout: true); }
    public float PadY { get => padY; set => SetProperty(ref padY, value, relayout: true); }
    public Vector4 TextColor { get => textColor; set => SetProperty(ref textColor, value, repaint: true); }
    public Vector4 PlaceholderColor { get => placeholderColor; set => SetProperty(ref placeholderColor, value, repaint: true); }
    public Vector4 CaretColor { get => caretColor; set => SetProperty(ref caretColor, value, repaint: true); }
    public Vector4 SelectionColor { get => selectionColor; set => SetProperty(ref selectionColor, value, repaint: true); }

    /// <summary>Point size to draw/wrap at (optical size). Layout height uses the nominal size only.</summary>
    private float DrawSize => ScribeTaskFont.EffectiveSize(fontFamily, fontSize);

    /// <summary>Glyph-only vertical nudge; caret and selection stay on the Caudex line-box.</summary>
    private float GlyphOffsetY => ScribeTaskFont.OffsetY(fontFamily, fontSize);

    protected override void PerformLayout()
    {
        float availWidth = float.IsPositiveInfinity(Constraints.MaxWidth) ? 300f : Constraints.MaxWidth;
        float textWidth = Math.Max(1f, availWidth - PadX * 2);

        WrapInto(visualLines, text, textWidth, DrawSize, fontFamily);
        lineHeight = ScribeTaskFont.LineHeight(fontSize);

        float height = visualLines.Count * lineHeight + PadY * 2;
        // Fill the available width; height follows content (auto-grow).
        Size = Constraints.Constrain(new Vector2(availWidth, height));
    }

    protected override void PaintInternal(PaintingContext context)
    {
        base.PaintInternal(context);
        if (context.Canvas == null)
        {
            return;
        }

        int selStart = Math.Min(caret, selectionAnchor);
        int selEnd = Math.Max(caret, selectionAnchor);

        // Selection first, so it sits behind the text.
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

        // DrawText's Y is the baseline, so add ascent.
        float ascent = lineHeight * 0.8f;

        // Drawn before the text loop and only when empty, so it never sits behind typed characters.
        if (text.Length == 0 && placeholder.Length > 0)
        {
            ScribeGlyphFallback.DrawLine(context, placeholder, new Vector2(PadX, PadY + ascent + GlyphOffsetY), DrawSize, placeholderColor, FontFamily, FontWeight.Normal);
        }

        for (int i = 0; i < visualLines.Count; i++)
        {
            float y = PadY + i * lineHeight + ascent + GlyphOffsetY;
            // DrawLine (not context.DrawText) so a stored ←/→ the active font lacks renders in the fallback family.
            ScribeGlyphFallback.DrawLine(context, visualLines[i].Text, new Vector2(PadX, y), DrawSize, textColor, FontFamily, FontWeight.Normal);
        }

        if (hasFocus && caretVisible)
        {
            (int line, int col) = CaretToLineCol(caret);
            string upto = line < visualLines.Count ? visualLines[line].Text.Substring(0, Math.Min(col, visualLines[line].Text.Length)) : "";
            float caretX = PadX + MeasureWidth(upto);
            float caretY = PadY + line * lineHeight;
            context.DrawBox(new Vector2(caretX, caretY), new Vector2(2f, lineHeight), caretColor, Vector4.Zero, 0f, Vector4.Zero);
        }
    }

    // Routes through the same arrow-fallback split DrawLine uses, so caret/selection x doesn't drift by the
    // arrow's advance delta between the active and fallback fonts.
    private float MeasureWidth(string s) =>
        s.Length == 0 ? 0f : ScribeGlyphFallback.MeasureWidth(s, DrawSize, FontFamily, FontWeight.Normal);

    // Greedy word-wrap to a pixel width honoring '\n', recording each line's source offset (LibGUI's
    // BreakIntoLines is internal).
    private static void WrapInto(List<ScribeVisualLine> outLines, string s, float maxWidth, float fontSize, string fontFamily)
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
                float w = TextLayoutHelper.MeasureText(candidate, fontFamily, fontSize, FontWeight.Normal).X;
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

    // Maps a source offset to (visualLine, column); a caret in a consumed separator attaches to the earlier line's end.
    private (int, int) CaretToLineCol(int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        for (int i = 0; i < visualLines.Count; i++)
        {
            int lineStart = visualLines[i].Start;
            int lineEnd = lineStart + visualLines[i].Text.Length;
            if (offset < lineStart)
            {
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

    /// <summary>Caret vertical extent in LOCAL space, matching the painted bar. False before layout.</summary>
    public bool TryGetCaretRect(out float localTop, out float height)
    {
        if (lineHeight <= 0f)
        {
            localTop = 0f;
            height = 0f;
            return false;
        }

        (int line, _) = CaretToLineCol(caret);
        localTop = PadY + line * lineHeight;
        height = lineHeight;
        return true;
    }

    /// <summary>Inverse of the caret paint math: maps a LOCAL-space click point to the nearest source offset
    /// (line by clamped Y, then nearest character boundary by X). Uses the cached <see cref="visualLines"/>,
    /// so call it after a layout pass.</summary>
    public int OffsetAtPosition(Vector2 local)
    {
        if (visualLines.Count == 0) return 0;

        int line = (int)Math.Floor((local.Y - PadY) / lineHeight);
        line = Math.Clamp(line, 0, visualLines.Count - 1);

        string lineText = visualLines[line].Text;
        int lineStart = visualLines[line].Start;
        float targetX = local.X - PadX;

        if (targetX <= 0f) return lineStart;

        int bestCol = 0;
        float bestDist = Math.Abs(targetX); // distance to the boundary before the first character (x=0)
        for (int col = 1; col <= lineText.Length; col++)
        {
            float x = MeasureWidth(lineText.Substring(0, col));
            float dist = Math.Abs(targetX - x);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestCol = col;
            }
            else if (x > targetX)
            {
                // Widths increase monotonically; once past the click and getting farther, stop.
                break;
            }
        }

        return lineStart + bestCol;
    }

    /// <summary>Moves a caret offset one visual line up (-1) or down (+1), landing on the column nearest the
    /// caret's current X. Up on the first line goes to 0, Down on the last to the text end. No
    /// preferred-column memory across presses. Valid only after a layout pass.</summary>
    public int CaretOffsetVertical(int fromCaret, int direction)
    {
        if (visualLines.Count == 0) return 0;

        var (line, col) = CaretToLineCol(fromCaret);
        int targetLine = line + direction;
        if (targetLine < 0) return 0;
        if (targetLine >= visualLines.Count) return text.Length;

        string curText = visualLines[line].Text;
        float caretX = PadX + MeasureWidth(curText.Substring(0, Math.Min(col, curText.Length)));
        // Aim at the target line's vertical middle.
        float targetY = PadY + (targetLine + 0.5f) * lineHeight;
        return OffsetAtPosition(new Vector2(caretX, targetY));
    }
}

/// <summary>RenderObjectWidget bridge into <see cref="ScribeMultilineFieldRender"/>.</summary>
internal sealed class ScribeMultilineFieldRenderWidget : RenderObjectWidget
{
    public ScribeMultilineFieldRenderWidget(string text, string placeholder, int caret, int selectionAnchor, bool hasFocus,
        float fontSize, float padX, float padY, Vector4 textColor, Vector4 placeholderColor, Vector4 caretColor, Vector4 selectionColor,
        Vector4 boxColor, Vector4 borderColor, float borderThickness, Vector4 cornerRadii, string fontFamily, bool caretVisible = true)
    {
        Text = text;
        Placeholder = placeholder;
        Caret = caret;
        SelectionAnchor = selectionAnchor;
        HasFocus = hasFocus;
        CaretVisible = caretVisible;
        FontSize = fontSize;
        FontFamily = fontFamily;
        PadX = padX;
        PadY = padY;
        TextColor = textColor;
        PlaceholderColor = placeholderColor;
        CaretColor = caretColor;
        SelectionColor = selectionColor;
        BoxColor = boxColor;
        BorderColor = borderColor;
        BorderThickness = borderThickness;
        CornerRadii = cornerRadii;
    }

    public string Text { get; }
    public string Placeholder { get; }
    public int Caret { get; }
    public int SelectionAnchor { get; }
    public bool HasFocus { get; }
    public bool CaretVisible { get; }
    public float FontSize { get; }
    public string FontFamily { get; }
    public float PadX { get; }
    public float PadY { get; }
    public Vector4 TextColor { get; }
    public Vector4 PlaceholderColor { get; }
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
        ro.Placeholder = Placeholder;
        ro.Caret = Caret;
        ro.SelectionAnchor = SelectionAnchor;
        ro.FieldHasFocus = HasFocus;
        ro.CaretVisible = CaretVisible;
        ro.FontSize = FontSize;
        ro.FontFamily = FontFamily;
        ro.PadX = PadX;
        ro.PadY = PadY;
        ro.TextColor = TextColor;
        ro.PlaceholderColor = PlaceholderColor;
        ro.CaretColor = CaretColor;
        ro.SelectionColor = SelectionColor;
        ro.Color = BoxColor;
        ro.BorderColor = BorderColor;
        ro.BorderThickness = BorderThickness;
        ro.CornerRadii = CornerRadii;
    }
}
