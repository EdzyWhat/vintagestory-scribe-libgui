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
using Gui.Widgets.Animations;     // Ticker (caret blink)
using Gui.Widgets.Events;         // KeyboardEvent, IKeyDownHandler, IKeyCharHandler
using Gui.Widgets.Framework;      // Widget, StatefulWidget, State, RenderObjectWidget, Theme, IFocusable
using Gui.Widgets.Input;          // FocusNode, GestureDetector
using OpenTK.Mathematics;         // Vector2, Vector4
using Scribe.Core.Cuneiform;      // GlyphBundle (cuneiform render path)
using Vintagestory.API.Client;    // GlKeys

namespace Scribe;

/// <summary>One wrapped display line: its text and the offset of its first character in the source
/// text. <see cref="Start"/> + <see cref="Text"/>.Length gives the offset just past the line's last
/// character; the source separator consumed by a soft wrap (a space) or a hard break (a '\n') sits
/// between one line's end and the next line's <see cref="Start"/>.</summary>
internal readonly record struct ScribeVisualLine(string Text, int Start);

/// <summary>The render object: wraps + paints multi-line text, a selection highlight, and a caret,
/// auto-sizing its height to the wrapped line count. Implements <see cref="IScribeEditableTextRender"/>
/// (already had both members) so the field State can drive it or the cuneiform render object through
/// one contract.</summary>
internal sealed class ScribeMultilineFieldRender : Gui.Core.Framework.RenderBox, IScribeEditableTextRender
{
    /// <summary>Font family used for BOTH measuring and drawing this field's text. It MUST match the
    /// family the read view's <see cref="Gui.Widgets.Basic.Text"/> uses for the same row, or the two views
    /// resolve different typefaces with different line metrics and a single-line row ends up a couple of
    /// pixels taller in one view than the other — which then also makes the pixel-based scroll-offset
    /// restore across a view switch land on a slightly different row. Passing "" here resolves to a
    /// DIFFERENT system typeface than "sans-serif" (the ~2px read/edit mismatch we hit before), so the
    /// setter coalesces null/empty to the "sans-serif" default. Since v1-release-checklist §6 the family is
    /// no longer fixed: the player's task-font choice flows in here AND into the read row's TextStyle via
    /// the same resolver (<c>ScribeTaskFont.Resolve</c>), keeping the two views on one family.</summary>
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

    // Cached wrap result from the last PerformLayout, reused by PaintInternal so layout and paint
    // agree on the exact line breaks (and their source offsets).
    private readonly List<ScribeVisualLine> visualLines = new();
    private float lineHeight = 18f;

    public string Text { get => text; set => SetProperty(ref text, value ?? "", relayout: true); }
    /// <summary>Ghost hint text painted (dimmed) only when the field is empty — a "New task…" affordance
    /// for a freshly-created empty task. It is NOT part of the editable text (never wrapped, measured for
    /// height, or committed); it is drawn in place of the (absent) real text on the first line.</summary>
    public string Placeholder { get => placeholder; set => SetProperty(ref placeholder, value ?? "", repaint: true); }
    public int Caret { get => caret; set => SetProperty(ref caret, value, repaint: true); }
    public int SelectionAnchor { get => selectionAnchor; set => SetProperty(ref selectionAnchor, value, repaint: true); }
    public bool FieldHasFocus { get => hasFocus; set => SetProperty(ref hasFocus, value, repaint: true); }
    /// <summary>Blink gate: false paints no caret (the OFF half of the blink), driven by the shared caret
    /// ticker in <see cref="ScribeMultilineFieldState"/>. Focus still gates whether a caret exists at all;
    /// a live selection keeps the caret solid (the State pins this true), mirroring LibGUI's TextField.</summary>
    public bool CaretVisible { get => caretVisible; set => SetProperty(ref caretVisible, value, repaint: true); }
    public float FontSize { get => fontSize; set => SetProperty(ref fontSize, value, relayout: true); }
    /// <summary>Task-text font family (v1-release-checklist §6). Coalesces null/empty to "sans-serif" so an
    /// unset value keeps the built-in body face; changing it relayouts (family changes line metrics).</summary>
    public string FontFamily { get => fontFamily; set => SetProperty(ref fontFamily, string.IsNullOrEmpty(value) ? "sans-serif" : value, relayout: true); }
    public float PadX { get => padX; set => SetProperty(ref padX, value, relayout: true); }
    public float PadY { get => padY; set => SetProperty(ref padY, value, relayout: true); }
    public Vector4 TextColor { get => textColor; set => SetProperty(ref textColor, value, repaint: true); }
    public Vector4 PlaceholderColor { get => placeholderColor; set => SetProperty(ref placeholderColor, value, repaint: true); }
    public Vector4 CaretColor { get => caretColor; set => SetProperty(ref caretColor, value, repaint: true); }
    public Vector4 SelectionColor { get => selectionColor; set => SetProperty(ref selectionColor, value, repaint: true); }

    protected override void PerformLayout()
    {
        float availWidth = float.IsPositiveInfinity(Constraints.MaxWidth) ? 300f : Constraints.MaxWidth;
        float textWidth = Math.Max(1f, availWidth - PadX * 2);

        WrapInto(visualLines, text, textWidth, fontSize, fontFamily);
        lineHeight = MeasureLineHeight(fontSize, fontFamily);

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

        // Ghost placeholder: when there is no real text, paint the dimmed hint on the first line in place
        // of the (absent) content. Drawn before the real-text loop so a stray empty visual line can't
        // overpaint it, and gated on empty text so it never sits behind typed characters.
        if (text.Length == 0 && placeholder.Length > 0)
        {
            context.DrawText(placeholder, new Vector2(PadX, PadY + ascent), fontSize, placeholderColor, FontFamily, FontWeight.Normal);
        }

        for (int i = 0; i < visualLines.Count; i++)
        {
            float y = PadY + i * lineHeight + ascent;
            context.DrawText(visualLines[i].Text, new Vector2(PadX, y), fontSize, textColor, FontFamily, FontWeight.Normal);
        }

        // Caret: map the flat caret offset onto (line, column) of the wrapped text, then draw a bar.
        if (hasFocus && caretVisible)
        {
            (int line, int col) = CaretToLineCol(caret);
            string upto = line < visualLines.Count ? visualLines[line].Text.Substring(0, Math.Min(col, visualLines[line].Text.Length)) : "";
            float caretX = PadX + MeasureWidth(upto);
            float caretY = PadY + line * lineHeight;
            context.DrawBox(new Vector2(caretX, caretY), new Vector2(2f, lineHeight), caretColor, Vector4.Zero, 0f, Vector4.Zero);
        }
    }

    private float MeasureWidth(string s) =>
        s.Length == 0 ? 0f : TextLayoutHelper.MeasureText(s, FontFamily, fontSize, FontWeight.Normal).X;

    // Greedy word-wrap to a pixel width, honoring explicit '\n', recording each visual line's source
    // offset so the caret/selection can map flat offsets onto (line, column). Public API only
    // (MeasureText); LibGUI's BreakIntoLines is internal.
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

    private static float MeasureLineHeight(float fontSize, string fontFamily)
    {
        float h = TextLayoutHelper.MeasureText("Ag", fontFamily, fontSize, FontWeight.Normal).Y;
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

    /// <summary>Inverse of the caret paint math: map a click point in the render object's LOCAL space
    /// (post-<c>GlobalToLocal</c>) onto the nearest flat caret offset in the source text. Picks the
    /// visual line by Y (clamped to the first/last line), then within that line walks characters and
    /// returns the boundary nearest the click X (so clicking in the left half of a glyph lands the caret
    /// before it, the right half after). Returns the source offset of that (line, column), so the caret
    /// sits exactly where CaretToLineCol/PaintInternal would draw it. Uses the cached <see cref="visualLines"/>
    /// from the last layout, so it must be called after a layout pass (always true for a click on a
    /// mounted field).</summary>
    public int OffsetAtPosition(Vector2 local)
    {
        if (visualLines.Count == 0) return 0;

        // Which wrapped line: the click Y minus the top pad, divided by line height, clamped in range.
        int line = (int)Math.Floor((local.Y - PadY) / lineHeight);
        line = Math.Clamp(line, 0, visualLines.Count - 1);

        string lineText = visualLines[line].Text;
        int lineStart = visualLines[line].Start;
        float targetX = local.X - PadX;

        // Left of the text → line start. Otherwise find the character boundary whose X is closest to the
        // click, measuring the prefix width up to each boundary (same MeasureWidth the paint path uses).
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
                // Widths are monotonically increasing, so once we've passed the click and started
                // getting farther, the nearest boundary is behind us — stop.
                break;
            }
        }

        return lineStart + bestCol;
    }

    /// <summary>Move a caret offset one visual line up (<paramref name="direction"/> = -1) or down (+1),
    /// landing on the column nearest the caret's current X — the vertical analogue of
    /// <see cref="OffsetAtPosition"/>. Derives the caret's live X from the same paint math
    /// (<c>PadX + MeasureWidth(prefix)</c>), then reuses <see cref="OffsetAtPosition"/> at that X on the
    /// target line, so the caret lands exactly where a click there would. Edges follow desktop editors:
    /// Up on the first line goes to the text start (0), Down on the last line to the text end. There is no
    /// preferred-column memory across presses (the target X is read fresh each call) — see design.md.
    /// Valid only after a layout pass (like the click path); a focused, painted field always satisfies this.</summary>
    public int CaretOffsetVertical(int fromCaret, int direction)
    {
        if (visualLines.Count == 0) return 0;

        var (line, col) = CaretToLineCol(fromCaret);
        int targetLine = line + direction;
        if (targetLine < 0) return 0;
        if (targetLine >= visualLines.Count) return text.Length;

        string curText = visualLines[line].Text;
        float caretX = PadX + MeasureWidth(curText.Substring(0, Math.Min(col, curText.Length)));
        // Aim at the vertical middle of the target line so OffsetAtPosition's Y→line pick lands on it.
        float targetY = PadY + (targetLine + 0.5f) * lineHeight;
        return OffsetAtPosition(new Vector2(caretX, targetY));
    }
}

/// <summary>RenderObjectWidget bridge: pushes text/caret/selection/focus/colors into the render object.</summary>
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

/// <summary>
/// A focusable multi-line editable field with a (text, caret, selection) model. Owns its text while
/// alive (the editor's scratch document mirrors it via <see cref="OnChanged"/>); a full dialog
/// rebuild recreates the field, re-seeding from <see cref="InitialText"/>.
/// </summary>
public sealed class ScribeMultilineField : StatefulWidget, IFocusable
{
    public ScribeMultilineField(
        string initialText = "",
        string placeholder = "",
        FocusNode? focusNode = null,
        float fontSize = 15f,
        string fontFamily = "sans-serif",
        float padX = 8f,
        float padY = 6f,
        bool autoFocus = false,
        int? maxLength = null,
        bool useCuneiform = false,
        GlyphBundle? cuneiformBundle = null,
        float cuneiformJitter = 0f,
        int cuneiformJitterSeed = 0,
        bool cuneiformProgression = false,
        Action<string>? onChanged = null,
        Action? onCommitAndAdvance = null,
        Action? onCommitAndRetreat = null,
        Action? onInsertTaskBelow = null,
        Action? onBlur = null,
        Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        InitialText = initialText;
        Placeholder = placeholder;
        FocusNode = focusNode;
        FontSize = fontSize;
        FontFamily = fontFamily;
        PadX = padX;
        PadY = padY;
        AutoFocus = autoFocus;
        MaxLength = maxLength;
        UseCuneiform = useCuneiform;
        CuneiformBundle = cuneiformBundle;
        CuneiformJitter = cuneiformJitter;
        CuneiformJitterSeed = cuneiformJitterSeed;
        CuneiformProgression = cuneiformProgression;
        OnChanged = onChanged;
        OnCommitAndAdvance = onCommitAndAdvance;
        OnCommitAndRetreat = onCommitAndRetreat;
        OnInsertTaskBelow = onInsertTaskBelow;
        OnBlur = onBlur;
    }

    public string InitialText { get; }
    /// <summary>Ghost hint painted (dimmed) while the field is empty (e.g. "New task…"); empty string
    /// disables it. Not part of the editable/committed text — see <see cref="ScribeMultilineFieldRender.Placeholder"/>.</summary>
    public string Placeholder { get; }
    public FocusNode? FocusNode { get; }
    public float FontSize { get; }
    /// <summary>Task-text font family (v1-release-checklist §6); defaults to "sans-serif" (the built-in
    /// body face). Must match the read row's resolved family so the two views keep identical line metrics.</summary>
    public string FontFamily { get; }
    /// <summary>Internal horizontal padding for the field's text box (pixels). Fed from
    /// <see cref="ScribeRowStyle.FieldPadX"/> so the read view can match the same inset.</summary>
    public float PadX { get; }
    /// <summary>Internal vertical padding for the field's text box (pixels). Fed from
    /// <see cref="ScribeRowStyle.FieldPadY"/> so single-line row heights match across views.</summary>
    public float PadY { get; }
    /// <summary>Request focus as soon as this field mounts. LibGUI has no focus-traversal API, so the
    /// editor content coordinates focus among rows manually; a freshly built row that should be
    /// focused (e.g. after Add Task or entering editor mode) sets this to focus itself on mount.</summary>
    public bool AutoFocus { get; }
    /// <summary>Optional maximum character count. When set, typing and paste are clamped so the field's
    /// text never exceeds it (a maxlength affordance) — used for Task rows, held to
    /// <c>ScribeDocumentCodec.MaxTaskTextLength</c>; null (Text sections) leaves the field uncapped and
    /// relies on the codec's larger hard bound. The codec also clips on read as the authoritative
    /// backstop, so this is purely the in-editor UX half.</summary>
    public int? MaxLength { get; }
    /// <summary>When true (tablet only), the field paints its buffer as live cuneiform strokes with a
    /// synthetic caret instead of the normal TTF text — the same State drives a cuneiform render object.
    /// Default false keeps the Lectern/Notebook editors on the normal renderer, and the disable-cuneiform
    /// fallback flows in as false so those surfaces revert to a legible editable field.</summary>
    public bool UseCuneiform { get; }
    /// <summary>Parsed cuneiform glyph geometry for the <see cref="UseCuneiform"/> path; null renders no
    /// strokes (asset not yet loaded). Ignored when <see cref="UseCuneiform"/> is false.</summary>
    public GlyphBundle? CuneiformBundle { get; }
    /// <summary>Hand-written jitter strength (0..1) for the cuneiform path; 0 = crisp. Ignored when
    /// <see cref="UseCuneiform"/> is false.</summary>
    public float CuneiformJitter { get; }
    /// <summary>Fixed per-field base seed for the cuneiform jitter, so this field's letters wobble
    /// consistently and typing does not reseed the letters already on screen. Ignored when
    /// <see cref="UseCuneiform"/> is false.</summary>
    public int CuneiformJitterSeed { get; }
    /// <summary>Whether newly-typed cuneiform text presses in stroke-by-stroke (per-letter progression).
    /// Ignored when <see cref="UseCuneiform"/> is false; off reproduces the instant reveal.</summary>
    public bool CuneiformProgression { get; }
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

    // ---- Caret blink (mirrors LibGUI's own TextField: a ticker toggles caretVisible on a fixed cadence,
    // reset to solid on any edit/caret move, paused while a selection is active or focus is lost). Lives
    // in the shared State so BOTH render paths — the normal field and the cuneiform tablet field — blink
    // at the identical cadence (add-tablet-cuneiform-chrome task 8.1). ----
    /// <summary>Blink half-period in milliseconds, matching LibGUI <c>TextField.CursorBlinkMs</c>.</summary>
    private const double CaretBlinkMs = 500;
    private Ticker? caretTicker;
    private bool caretVisible = true;
    private DateTime caretLastToggle = DateTime.MinValue;

    // ---- Cuneiform stroke-progression reveal (add-cuneiform-handwriting-feel). Only the newly-typed run
    // presses in; already-revealed text never replays, and non-append edits (deletion, mid-line change)
    // snap to fully revealed. Inactive unless UseCuneiform + CuneiformProgression are both on. ----
    /// <summary>Per-stroke / per-letter reveal timing (ms). Tuned in-game; the client-config knob (task 6)
    /// will source these.</summary>
    private const double RevealPerStrokeMs = 28;
    private const double RevealPerLetterMs = 90;
    private AnimationController? revealController;
    /// <summary>Whether a reveal is currently animating. When false the field paints every stroke.</summary>
    private bool revealActive;
    /// <summary>Characters already fully pressed (never re-animated) for the current reveal run.</summary>
    private int revealBaselineChars;
    /// <summary>The buffer text as of the last reveal decision, so a commit can classify the edit
    /// (append → animate the suffix; anything else → snap).</summary>
    private string revealTrackedText = "";
    /// <summary>True between an onPress and its onRelease: a click-drag is selecting text, so onMove
    /// extends the selection to the cursor. The event dispatcher auto-captures the field on press, so
    /// moves keep arriving even when the cursor leaves the field's bounds mid-drag. Cleared for a
    /// double/triple click (which selects a word/line outright and should not then drag).</summary>
    private bool isSelecting;

    // ---- Multi-click tracking (double-click = word select, triple-click = line select) ----
    /// <summary>Max gap between clicks to count as part of the same multi-click, matching LibGUI's own
    /// <c>TextField</c> (400ms).</summary>
    private static readonly TimeSpan MultiClickWindow = TimeSpan.FromMilliseconds(400);
    private DateTime lastClickTime = DateTime.MinValue;
    private int lastClickOffset = -1;
    /// <summary>1 = single, 2 = double (word), 3 = triple (line). Increments while clicks land at the
    /// same offset within <see cref="MultiClickWindow"/>; resets to 1 otherwise, and caps at 3.</summary>
    private int clickCount;

    public override void InitState()
    {
        base.InitState();
        text = Widget.InitialText;
        caret = text.Length;
        anchor = caret;
        // The initial text is pre-existing content, not freshly typed, so it starts fully revealed (no
        // animation on open). Only text added after this baseline presses in.
        revealTrackedText = text;
        focusNode = Widget.FocusNode ?? (internalFocusNode = new FocusNode());
        // FocusNode.RequestFocus() resolves its manager via Owner?.Owner?.FocusManager; without an
        // Owner it can't reach the manager, focus never takes, and the HasFocus-gated key handlers
        // never fire (banked lesson from the change-1 spike). Element is set by the time InitState runs.
        focusNode.Owner = Element;
        focusNode.AddListener(OnFocusChanged);
        hadFocus = focusNode.HasFocus;

        // Caret-blink ticker (same provider LibGUI's TextField uses). Started only while focused with a
        // collapsed selection; OnCaretTick toggles caretVisible every CaretBlinkMs and rebuilds.
        caretTicker = Element.Owner!.GetTickerProvider().CreateTicker(OnCaretTick);

        // Reveal driver (cuneiform stroke-progression). Built only when the feature is on; its Value (0→1)
        // scales to elapsed-ms in the render each tick. Duration is reset per reveal to the run's length.
        if (Widget.UseCuneiform && Widget.CuneiformProgression)
        {
            revealController = new AnimationController(
                TimeSpan.FromMilliseconds(1), Element.Owner!.GetTickerProvider());
            revealController.OnValueChanged += OnRevealTick;
            revealController.OnStatusChanged += OnRevealStatus;
        }

        if (Widget.AutoFocus)
        {
            focusNode.RequestFocus();
        }
        RestartCaretBlink();
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
        RestartCaretBlink(); // gaining focus shows a solid caret then blinks; losing it stops the ticker
        MarkNeedsBuild();    // repaint caret + focus border on focus change
    }

    public override void Dispose()
    {
        focusNode.RemoveListener(OnFocusChanged);
        caretTicker?.Dispose();
        if (revealController is not null)
        {
            revealController.OnValueChanged -= OnRevealTick;
            revealController.OnStatusChanged -= OnRevealStatus;
            revealController.Dispose();
            revealController = null;
        }
        internalFocusNode?.Dispose();
        base.Dispose();
    }

    /// <summary>Show the caret solid and (re)start the blink cadence — called on focus gain and after any
    /// edit or caret move, mirroring LibGUI's TextField (a moving/typing caret stays solid, then resumes
    /// blinking). The ticker only runs while focused with a collapsed selection; otherwise it is stopped
    /// and the caret is pinned solid (a selection shows no separate blinking caret).</summary>
    private void RestartCaretBlink()
    {
        caretVisible = true;
        caretLastToggle = DateTime.Now;
        if (focusNode.HasFocus && !HasSelection)
        {
            if (caretTicker is { IsTicking: false })
            {
                caretTicker.Start();
            }
        }
        else
        {
            caretTicker?.Stop();
        }
    }

    /// <summary>Ticker callback: while focused with a collapsed selection, flip <see cref="caretVisible"/>
    /// every <see cref="CaretBlinkMs"/> and rebuild so the caret bar blinks. Bails (caret solid, ticker
    /// stopped) the moment focus is lost or a selection becomes active.</summary>
    private void OnCaretTick(TimeSpan frameDelta)
    {
        if (!focusNode.HasFocus || HasSelection)
        {
            caretVisible = true;
            caretTicker?.Stop();
            return;
        }

        var now = DateTime.Now;
        if ((now - caretLastToggle).TotalMilliseconds >= CaretBlinkMs)
        {
            caretLastToggle = now;
            caretVisible = !caretVisible;
            MarkNeedsBuild();
        }
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
                MarkNeedsBuild(); // repaint the selection now — unlike MoveCaret/Commit paths, this one
                                  // has no other rebuild trigger, so without it the select-all is
                                  // invisible until the next hover/keystroke forces a rebuild.
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

            case (int)GlKeys.Up:
                if (CaretVertical(-1) is { } up) MoveCaret(up, e.Shift);
                Handled(e);
                break;

            case (int)GlKeys.Down:
                if (CaretVertical(+1) is { } down) MoveCaret(down, e.Shift);
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
        RestartCaretBlink(); // a moving caret shows solid, then resumes blinking (pauses if now selecting)
        MarkNeedsBuild();
    }

    private void Insert(string s)
    {
        if (HasSelection)
        {
            DeleteSelection();
        }
        caret = Math.Clamp(caret, 0, text.Length);
        // Enforce the optional maxlength (Task rows): clamp the inserted run to the room remaining after
        // the current text, so a long paste is truncated rather than blocked wholesale. Typing at the cap
        // becomes a no-op. The codec clips on read regardless, so this is the in-editor UX half only.
        if (Widget.MaxLength is { } max)
        {
            int room = Math.Max(0, max - text.Length);
            if (s.Length > room) s = s.Substring(0, room);
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
        UpdateReveal();
        Widget.OnChanged?.Invoke(text);
        RestartCaretBlink(); // any edit shows the caret solid, then resumes blinking
        MarkNeedsBuild();
    }

    /// <summary>
    /// Decide how the just-committed <see cref="text"/> reveals, when cuneiform stroke-progression is on.
    /// A pure APPEND to the previously-tracked text (typing at/after the end) animates only the newly-added
    /// suffix: the prior length becomes the always-revealed baseline and the controller runs from 0. Any
    /// other change (deletion, mid-line insert, paste that isn't a suffix) snaps to fully revealed with no
    /// reverse animation, so earlier letters never replay. A no-op when the feature is off.
    /// </summary>
    private void UpdateReveal()
    {
        if (revealController is null)
        {
            return;
        }

        // Append iff the new text starts with the old text and is longer (Ordinal — exact code units).
        bool isAppend = text.Length > revealTrackedText.Length
            && text.StartsWith(revealTrackedText, StringComparison.Ordinal);

        if (isAppend)
        {
            // Preserve the elapsed time already accumulated (so letters pressed mid-flight are NOT re-hidden
            // when the run is extended by more typing). elapsed = Value × current Duration.
            double elapsedMs = revealActive
                ? revealController.Value * revealController.Duration.TotalMilliseconds
                : 0.0;

            // Starting a fresh run: everything present before this keystroke is the always-revealed baseline.
            if (!revealActive)
            {
                revealBaselineChars = revealTrackedText.Length;
            }

            double durationMs = Scribe.Core.Cuneiform.CuneiformReveal.TotalDurationMs(
                revealBaselineChars, text.Length, new Scribe.Core.Cuneiform.RevealSchedule(RevealPerStrokeMs, RevealPerLetterMs));
            if (durationMs > 0)
            {
                revealActive = true;
                revealController.Duration = TimeSpan.FromMilliseconds(durationMs);
                // Resume from the same absolute elapsed against the NEW duration, so the frontier keeps
                // moving forward and prior strokes stay pressed (the render gates on elapsed-vs-schedule).
                revealController.Forward(from: Math.Clamp(elapsedMs / durationMs, 0.0, 1.0));
            }
        }
        else
        {
            // Deletion / mid-line edit / non-suffix change: snap to fully revealed, no reverse animation.
            revealActive = false;
            revealController.Stop();
        }

        revealTrackedText = text;
    }

    private void OnRevealTick(double _) => MarkNeedsBuild();

    private void OnRevealStatus(AnimationStatus status)
    {
        // Reveal finished: drop out of reveal mode so the field paints every stroke (fully pressed) again.
        if (status == AnimationStatus.Completed)
        {
            revealActive = false;
            MarkNeedsBuild();
        }
    }

    private void MarkNeedsBuild() => SetState(() => { });

    /// <summary>Maps a pointer event to a flat caret offset in the source text, or null if the render
    /// geometry isn't reachable. <c>Element.RenderObject</c> resolves (through the GestureDetector's
    /// proxy box) to the proxy wrapping our text render object as its single child; the proxy lays the
    /// child out at (0,0) with the same size, so the proxy-local point is directly usable by the child's
    /// <see cref="ScribeMultilineFieldRender.OffsetAtPosition"/> (the inverse of the caret paint math).</summary>
    private int? OffsetAt(PointerEvent e)
    {
        if (Element?.RenderObject is not { } proxy) return null;
        var textRender = ResolveTextRender(proxy);
        if (textRender is null) return null;

        Vector2 local = proxy.GlobalToLocal(new Vector2(e.X, e.Y));
        return Math.Clamp(textRender.OffsetAtPosition(local), 0, text.Length);
    }

    /// <summary>Resolve the field's editable text render object through the GestureDetector proxy. The
    /// proxy wraps our render object as its single child; it may be the normal
    /// <see cref="ScribeMultilineFieldRender"/> or the cuneiform <see cref="ScribeCuneiformFieldRender"/>,
    /// so we resolve it by the shared <see cref="IScribeEditableTextRender"/> contract rather than a
    /// concrete type — letting one State drive either render object (design D2/D-Q4).</summary>
    private static IScribeEditableTextRender? ResolveTextRender(RenderObject proxy)
    {
        if (proxy is IScribeEditableTextRender direct) return direct;
        return proxy.Children.Count > 0 ? proxy.Children[0] as IScribeEditableTextRender : null;
    }

    /// <summary>Move the caret one visual line up (<paramref name="direction"/> = -1) or down (+1),
    /// returning the new source offset, or null if the render geometry isn't reachable yet. Delegates the
    /// column-nearest-X math to <see cref="ScribeMultilineFieldRender.CaretOffsetVertical"/> on the same
    /// render object the click path uses (resolved through the GestureDetector proxy).</summary>
    private int? CaretVertical(int direction)
    {
        if (Element?.RenderObject is not { } proxy) return null;
        var textRender = ResolveTextRender(proxy);
        if (textRender is null) return null;

        return Math.Clamp(textRender.CaretOffsetVertical(caret, direction), 0, text.Length);
    }

    /// <summary>Press: focus the field and act on the click. A single click moves the caret to the click
    /// point (repositioning it in an already-focused field, not just focusing) and begins a click-drag
    /// selection. A double click at the same spot selects the word under it; a triple click selects the
    /// whole logical line. Mirrors LibGUI's own <c>TextField.OnPointerDown</c> (word select on
    /// double-click), extended here with triple-click line select and multi-line word/line boundaries.</summary>
    private void OnFieldPress(PointerEvent e)
    {
        focusNode.RequestFocus();

        if (OffsetAt(e) is not { } offset) return;

        // Multi-click detection: same offset within the window bumps the count (capped at 3), else reset.
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

        RestartCaretBlink(); // a fresh click resets the caret solid (or stops the blink if it selected a range)
        MarkNeedsBuild();
    }

    /// <summary>Word boundaries (start, end) around <paramref name="pos"/>, matching LibGUI's
    /// <c>TextField.FindWordBoundary</c>: a word char (letter/digit/underscore) expands to the full run
    /// of word chars; a non-word char selects just that one character. Returns (pos, pos) for empty text.</summary>
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

    /// <summary>Drag while selecting: extend the selection by moving the caret to the cursor and leaving
    /// the anchor at the press point. The dispatcher's press-capture keeps these firing even if the
    /// cursor leaves the field's bounds.</summary>
    private void OnFieldMove(PointerEvent e)
    {
        if (!isSelecting) return;
        if (OffsetAt(e) is not { } offset) return;
        if (offset == caret) return; // no caret movement -> no rebuild
        caret = offset;              // anchor unchanged: [min,max] is the live selection
        MarkNeedsBuild();
    }

    /// <summary>Release: end the click-drag. The caret/anchor already hold the final selection.</summary>
    private void OnFieldRelease(PointerEvent e) => isSelecting = false;

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;

        // The gesture/keyboard model is identical for both render paths (this State owns it); only the
        // child render widget differs. When UseCuneiform is on, drive the cuneiform render object — it
        // implements the same IScribeEditableTextRender geometry contract the click/vertical-nav helpers
        // resolve through the proxy, so no State code changes between the two paths.
        Widget child = Widget.UseCuneiform
            ? new ScribeCuneiformFieldRenderWidget(
                text: text,
                caret: caret,
                selectionAnchor: anchor,
                hasFocus: focusNode.HasFocus,
                fontSizeEm: Widget.FontSize,
                inkColor: colors.OnSurface,
                caretColor: colors.Primary,
                selectionColor: colors.Primary with { W = 0.35f },
                bundle: Widget.CuneiformBundle,
                padX: Widget.PadX,
                padY: Widget.PadY,
                // No field-level box on the cuneiform (tablet) path: the row is borderless/transparent at
                // rest and gains its border + background from the enclosing ScribeEditRow Container on focus
                // (add-tablet-cuneiform-chrome D3 / task 5.1) — one appearance driver, no doubled chrome.
                boxColor: Vector4.Zero,
                borderColor: Vector4.Zero,
                borderThickness: 0f,
                cornerRadii: Vector4.Zero,
                caretVisible: caretVisible,
                jitterStrength: Widget.CuneiformJitter,
                jitterSeed: Widget.CuneiformJitterSeed,
                revealActive: revealActive,
                revealBaselineChars: revealBaselineChars,
                revealElapsedMs: revealController is not null
                    ? revealController.Value * revealController.Duration.TotalMilliseconds
                    : 0.0,
                revealPerStrokeMs: RevealPerStrokeMs,
                revealPerLetterMs: RevealPerLetterMs)
            : new ScribeMultilineFieldRenderWidget(
                text: text,
                placeholder: Widget.Placeholder,
                caret: caret,
                selectionAnchor: anchor,
                hasFocus: focusNode.HasFocus,
                fontSize: Widget.FontSize,
                padX: Widget.PadX,
                padY: Widget.PadY,
                textColor: colors.OnSurface,
                placeholderColor: colors.OnSurfaceVariant with { W = 0.55f },
                caretColor: colors.Primary,
                selectionColor: colors.Primary with { W = 0.35f },
                boxColor: colors.SurfaceHigh,
                borderColor: focusNode.HasFocus ? colors.Primary : colors.Border,
                borderThickness: 1f,
                cornerRadii: Vector4.One * 4f,
                fontFamily: Widget.FontFamily,
                caretVisible: caretVisible);

        return new GestureDetector(
            onPress: OnFieldPress,
            onMove: OnFieldMove,
            onRelease: OnFieldRelease,
            child: child);
    }
}
