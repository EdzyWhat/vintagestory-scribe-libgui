// Production multi-line editable field for the LibGUI lectern editor view. LibGUI's stock TextField is
// single-line and `internal`, so this reimplements a wrapping, auto-growing, focus-holding field on LibGUI's
// PUBLIC API:
//   • ScribeMultilineFieldRender : RenderBox — wraps text, sizes height to line count, paints selection/lines/caret.
//   • ScribeMultilineFieldRenderWidget : RenderObjectWidget — the create/update bridge.
//   • ScribeMultilineField : StatefulWidget + its State — owns the (text, caret, selection-anchor) model.
//
// Keyboard model: Enter = commit-and-insert-task-below (never a newline), Shift+Enter = hard line break,
// Tab / Shift+Tab = commit-and-advance / retreat, Esc bubbles (closes the dialog). Caret nav: Left/Right/
// Home/End, Ctrl/Alt word-skip, Shift extends the selection. Clipboard: Ctrl+A/C/X/V. LibGUI's
// KeyboardEvent has no Command modifier, so macOS Cmd combos are translated to Ctrl combos one layer up
// (the dialog's OnKeyDown override); this widget stays platform-agnostic.

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

/// <summary>Display-only Latin task/note text using the editor's render object, so Read and Edit share wrap,
/// line-box height, optical draw size, and <c>OffsetEm</c> (a paint-only <c>Transform.Scale</c> would scale
/// the line gap too and crush multi-line rows). No caret, no chrome.</summary>
internal static class ScribeTaskTextDisplay
{
    public static Widget Build(string text, ScribeRowStyle style, Vector4 ink) =>
        new ScribeMultilineFieldRenderWidget(
            text: text,
            placeholder: "",
            caret: 0,
            selectionAnchor: 0,
            hasFocus: false,
            fontSize: style.FontSize,
            padX: style.FieldPadX,
            padY: style.FieldPadY,
            textColor: ink,
            placeholderColor: Vector4.Zero,
            caretColor: Vector4.Zero,
            selectionColor: Vector4.Zero,
            boxColor: Vector4.Zero,
            borderColor: Vector4.Zero,
            borderThickness: 1f,
            cornerRadii: Vector4.One * 4f,
            fontFamily: ScribeTaskFont.Resolve(style.TaskFontFamily),
            caretVisible: false);
}

/// <summary>
/// A focusable multi-line editable field with a (text, caret, selection) model. It owns its text while alive
/// (the editor's scratch document mirrors it via <see cref="OnChanged"/>); a dialog rebuild re-seeds from
/// <see cref="InitialText"/>.
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
        Vector4? focusBorderColor = null,
        bool autoFocus = false,
        int? maxLength = null,
        bool useCuneiform = false,
        GlyphBundle? cuneiformBundle = null,
        float cuneiformJitter = 0f,
        int cuneiformJitterSeed = 0,
        float cuneiformRotation = 0f,
        bool cuneiformProgression = false,
        CuneiformGlow cuneiformGlow = default,
        float cuneiformStrokeWeightScale = 1f,
        Action<string>? onChanged = null,
        Action? onCommitAndAdvance = null,
        Action? onCommitAndRetreat = null,
        Action? onInsertTaskBelow = null,
        Action? onBlur = null,
        Action? onMaxLengthReached = null,
        Action? onCaretMoved = null,
        Action? onPointerFocus = null,
        Action? onJumpToFirstRow = null,
        Action? onJumpToLastRow = null,
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
        FocusBorderColor = focusBorderColor;
        AutoFocus = autoFocus;
        MaxLength = maxLength;
        UseCuneiform = useCuneiform;
        CuneiformBundle = cuneiformBundle;
        CuneiformJitter = cuneiformJitter;
        CuneiformJitterSeed = cuneiformJitterSeed;
        CuneiformRotation = cuneiformRotation;
        CuneiformProgression = cuneiformProgression;
        CuneiformGlow = cuneiformGlow;
        CuneiformStrokeWeightScale = cuneiformStrokeWeightScale;
        OnChanged = onChanged;
        OnCommitAndAdvance = onCommitAndAdvance;
        OnCommitAndRetreat = onCommitAndRetreat;
        OnInsertTaskBelow = onInsertTaskBelow;
        OnBlur = onBlur;
        OnMaxLengthReached = onMaxLengthReached;
        OnCaretMoved = onCaretMoved;
        OnPointerFocus = onPointerFocus;
        OnJumpToFirstRow = onJumpToFirstRow;
        OnJumpToLastRow = onJumpToLastRow;
    }

    public string InitialText { get; }
    /// <summary>Dimmed hint while empty; empty string disables it. Not part of the committed text.</summary>
    public string Placeholder { get; }
    public FocusNode? FocusNode { get; }
    public float FontSize { get; }
    /// <summary>Task-text font family; must match the read row's resolved family for identical line metrics.</summary>
    public string FontFamily { get; }
    /// <summary>Horizontal text padding in pixels (<see cref="ScribeRowStyle.FieldPadX"/>, so the read view matches).</summary>
    public float PadX { get; }
    /// <summary>Vertical text padding in pixels (<see cref="ScribeRowStyle.FieldPadY"/>, so row heights match across views).</summary>
    public float PadY { get; }
    /// <summary>Optional FOCUSED border color; null uses the theme's <c>Primary</c>. The Chalkboard overrides it
    /// (via <see cref="ScribeRowStyle.InputFocusBorderColor"/>) to avoid a forest-green outline.</summary>
    public Vector4? FocusBorderColor { get; }
    /// <summary>Request focus on mount. LibGUI has no focus traversal, so the editor coordinates focus among rows manually.</summary>
    public bool AutoFocus { get; }
    /// <summary>Optional max character count; typing and paste are clamped to it. Hitting it fires
    /// <see cref="OnMaxLengthReached"/>. The codec also clips on read as the authoritative backstop.</summary>
    public int? MaxLength { get; }
    /// <summary>Tablet only: paints the buffer as live cuneiform strokes with a synthetic caret via a
    /// cuneiform render object. False keeps the normal renderer (and is the disable-cuneiform fallback).</summary>
    public bool UseCuneiform { get; }
    /// <summary>Cuneiform glyph geometry; null renders no strokes (asset not loaded). Ignored unless <see cref="UseCuneiform"/>.</summary>
    public GlyphBundle? CuneiformBundle { get; }
    /// <summary>Hand-written jitter strength (0..1); 0 = crisp. Ignored unless <see cref="UseCuneiform"/>.</summary>
    public float CuneiformJitter { get; }
    /// <summary>Fixed per-field jitter seed, so typing does not reseed letters already on screen.</summary>
    public int CuneiformJitterSeed { get; }
    /// <summary>Whole-character rotation in degrees, applied after jitter at paint time.</summary>
    public float CuneiformRotation { get; }
    /// <summary>Whether newly-typed cuneiform presses in stroke-by-stroke; off reveals instantly.</summary>
    public bool CuneiformProgression { get; }
    /// <summary>Per-material outer glow behind the strokes; default disabled.</summary>
    public CuneiformGlow CuneiformGlow { get; }
    /// <summary>Per-view cuneiform stroke-weight scale; 1 = base weight.</summary>
    public float CuneiformStrokeWeightScale { get; }
    public Action<string>? OnChanged { get; }
    /// <summary>Tab: the text was committed via <see cref="OnChanged"/>; the parent normalizes and focuses the next row.</summary>
    public Action? OnCommitAndAdvance { get; }
    /// <summary>Shift+Tab: as advance, but to the previous row.</summary>
    public Action? OnCommitAndRetreat { get; }
    /// <summary>Enter: commit this row, insert a NEW task beneath it and focus it.</summary>
    public Action? OnInsertTaskBelow { get; }
    /// <summary>Focus lost without Tab/Enter (e.g. click-away): commit the row's edit.</summary>
    public Action? OnBlur { get; }
    /// <summary>Fired when <see cref="MaxLength"/> clamps an insert (truncated paste, or a keystroke at the cap).</summary>
    public Action? OnMaxLengthReached { get; }
    /// <summary>Fired when keyboard navigation moves the caret without changing text (the one caret change that
    /// skips <see cref="OnChanged"/>), so the editor can scroll it into view. Not fired on a click (see
    /// <see cref="OnPointerFocus"/>).</summary>
    public Action? OnCaretMoved { get; }
    /// <summary>Fired when a pointer press focuses this field, after the caret is placed. The editor uses it to
    /// suppress scroll-into-view on click (the caret is already visible); programmatic focus (Tab/Enter) still scrolls.</summary>
    public Action? OnPointerFocus { get; }
    /// <summary>Jump to the FIRST row of the note, caret at its start. Fired instead of a one-line move for
    /// Ctrl+Up / Ctrl+Home (macOS Cmd+Up is remapped to Ctrl+Up by the dialog). Null on single-field surfaces.</summary>
    public Action? OnJumpToFirstRow { get; }
    /// <summary>Mirror of <see cref="OnJumpToFirstRow"/> for the LAST row, caret at its end (Ctrl+Down / Ctrl+End).</summary>
    public Action? OnJumpToLastRow { get; }

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

    // ---- Caret blink: a ticker toggles caretVisible; reset to solid on any edit/caret move, paused while selecting or unfocused.
    // Lives in the shared State so both render paths blink identically. ----
    /// <summary>Blink half-period in ms, matching LibGUI <c>TextField.CursorBlinkMs</c>.</summary>
    private const double CaretBlinkMs = 500;
    private Ticker? caretTicker;
    private bool caretVisible = true;
    private DateTime caretLastToggle = DateTime.MinValue;

    // ---- Cuneiform stroke-progression reveal: only the newly-typed run presses in; non-append edits snap to
    // fully revealed. Inactive unless UseCuneiform + CuneiformProgression are both on. ----
    /// <summary>The reveal schedule (pure stroke-count model); its speed knob lives in Core so rows and the title band share it.</summary>
    private static readonly Scribe.Core.Cuneiform.RevealSchedule RevealScheduleShared =
        new(Scribe.Core.Cuneiform.CuneiformReveal.PerStrokeMs);
    private AnimationController? revealController;
    /// <summary>Whether a reveal is animating; when false every stroke paints.</summary>
    private bool revealActive;
    /// <summary>Characters already fully pressed for the current reveal run.</summary>
    private int revealBaselineChars;
    /// <summary>Buffer text at the last reveal decision, used to classify an edit as append vs snap.</summary>
    private string revealTrackedText = "";
    /// <summary>True between press and release: a click-drag is selecting. The dispatcher auto-captures the
    /// press, so moves keep arriving outside the field. Cleared for double/triple click.</summary>
    private bool isSelecting;

    // ---- Multi-click tracking ----
    /// <summary>Max gap between clicks of one multi-click, matching LibGUI's <c>TextField</c> (400ms).</summary>
    private static readonly TimeSpan MultiClickWindow = TimeSpan.FromMilliseconds(400);
    private DateTime lastClickTime = DateTime.MinValue;
    private int lastClickOffset = -1;
    /// <summary>1 = single, 2 = double (word), 3 = triple (line); resets to 1 off-spot or out of window, caps at 3.</summary>
    private int clickCount;

    public override void InitState()
    {
        base.InitState();
        text = Widget.InitialText;
        caret = text.Length;
        anchor = caret;
        // Initial text is pre-existing content, so it starts fully revealed.
        revealTrackedText = text;
        focusNode = Widget.FocusNode ?? (internalFocusNode = new FocusNode());
        // RequestFocus resolves its manager via Owner?.Owner?.FocusManager; without an Owner focus never
        // takes and the HasFocus-gated key handlers never fire.
        focusNode.Owner = Element;
        focusNode.AddListener(OnFocusChanged);
        hadFocus = focusNode.HasFocus;

        // Runs only while focused with a collapsed selection (see RestartCaretBlink).
        caretTicker = Element.Owner!.GetTickerProvider().CreateTicker(OnCaretTick);

        // Reveal driver; its Value (0→1) scales to elapsed-ms in the render, and Duration is reset per reveal.
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
        // A genuine focus loss (not a rebuild) commits the pending edit.
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

    /// <summary>Shows the caret solid and restarts the blink. The ticker runs only while focused with a
    /// collapsed selection; otherwise it stops and the caret stays solid.</summary>
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

    /// <summary>Ticker callback: toggles <see cref="caretVisible"/> every <see cref="CaretBlinkMs"/>; stops on focus loss or selection.</summary>
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

    /// <summary>
    /// Decides how the committed <see cref="text"/> reveals when stroke-progression is on. A pure append animates
    /// only the new suffix (prior length becomes the revealed baseline); any other change snaps to fully revealed.
    /// </summary>
    private void UpdateReveal()
    {
        if (revealController is null)
        {
            return;
        }

        // Append iff the new text starts with the old text and is longer.
        bool isAppend = text.Length > revealTrackedText.Length
            && text.StartsWith(revealTrackedText, StringComparison.Ordinal);

        if (isAppend)
        {
            // Preserve accumulated elapsed time so mid-flight letters are not re-hidden (elapsed = Value × Duration).
            double elapsedMs = revealActive
                ? revealController.Value * revealController.Duration.TotalMilliseconds
                : 0.0;

            if (!revealActive)
            {
                revealBaselineChars = revealTrackedText.Length;
            }

            double durationMs = Scribe.Core.Cuneiform.CuneiformReveal.TotalDurationMs(
                revealBaselineChars, CumulativeRevealUnits(text), RevealScheduleShared);
            if (durationMs > 0)
            {
                revealActive = true;
                revealController.Duration = TimeSpan.FromMilliseconds(durationMs);
                // Resume from the same absolute elapsed against the NEW duration so prior strokes stay pressed.
                revealController.Forward(from: Math.Clamp(elapsedMs / durationMs, 0.0, 1.0));
            }
        }
        else
        {
            revealActive = false;
            revealController.Stop();
        }

        revealTrackedText = text;
    }

    /// <summary>Prefix-sum of per-character stroke-units, sizing the reveal duration to the renderer's per-stroke
    /// timing. Falls back to one unit per character when the bundle isn't loaded.</summary>
    private int[] CumulativeRevealUnits(string forText)
    {
        if (Widget.CuneiformBundle is not { } bundle)
        {
            var flat = new int[forText.Length];
            Array.Fill(flat, 1);
            return Scribe.Core.Cuneiform.CuneiformReveal.CumulativeStrokeUnits(flat);
        }

        var layout = new Scribe.Core.Cuneiform.CuneiformLineLayout(bundle);
        return Scribe.Core.Cuneiform.CuneiformReveal.CumulativeStrokeUnits(layout.StrokeUnitsFor(forText));
    }

    private void OnRevealTick(double _) => MarkNeedsBuild();

    private void OnRevealStatus(AnimationStatus status)
    {
        if (status == AnimationStatus.Completed)
        {
            revealActive = false;
            MarkNeedsBuild();
        }
    }

    private void MarkNeedsBuild() => SetState(() => { });

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

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;

        // Gesture/keyboard model is shared; only the child render widget differs (both implement IScribeEditableTextRender).
        Widget child = Widget.UseCuneiform
            ? new ScribeCuneiformFieldRenderWidget(
                text: text,
                caret: caret,
                selectionAnchor: anchor,
                hasFocus: focusNode.HasFocus,
                fontSizeEm: Widget.FontSize,
                inkColor: colors.OnSurface,
                // Caret = text color (OnSurface), consistent with the stock numeric field. Selection stays the accent wash.
                caretColor: colors.OnSurface,
                selectionColor: colors.Primary with { W = 0.35f },
                bundle: Widget.CuneiformBundle,
                padX: Widget.PadX,
                // Cuneiform-only inset reduction to suit the smaller synthetic caret.
                padY: Widget.PadY * CuneiformMetrics.FieldPadYScale,
                // Focus box scoped to the INPUT: transparent at rest, SurfaceHigh + Primary on focus. Thickness and
                // corner stay constant so read/edit row heights keep parity.
                boxColor: focusNode.HasFocus ? colors.SurfaceHigh : Vector4.Zero,
                borderColor: focusNode.HasFocus ? (Widget.FocusBorderColor ?? colors.Primary) : Vector4.Zero,
                borderThickness: 1f,
                cornerRadii: Vector4.One * 4f,
                caretVisible: caretVisible,
                jitterStrength: Widget.CuneiformJitter,
                jitterSeed: Widget.CuneiformJitterSeed,
                rotationDegrees: Widget.CuneiformRotation,
                glow: Widget.CuneiformGlow,
                strokeWeightScale: Widget.CuneiformStrokeWeightScale,
                glyphDrawScale: CuneiformMetrics.GlyphDrawScale,
                caretHeightScale: CuneiformMetrics.CaretHeightScale,
                revealActive: revealActive,
                revealBaselineChars: revealBaselineChars,
                revealElapsedMs: revealController is not null
                    ? revealController.Value * revealController.Duration.TotalMilliseconds
                    : 0.0)
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
                // Caret = text color (OnSurface), as on the cuneiform path.
                caretColor: colors.OnSurface,
                selectionColor: colors.Primary with { W = 0.35f },
                boxColor: colors.SurfaceHigh,
                borderColor: focusNode.HasFocus ? (Widget.FocusBorderColor ?? colors.Primary) : colors.Border,
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
