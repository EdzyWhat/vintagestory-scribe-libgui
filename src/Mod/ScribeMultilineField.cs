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

using Gui.Widgets.Animations;     // Ticker (caret blink)
using Gui.Widgets.Events;         // KeyboardEvent, IKeyDownHandler, IKeyCharHandler
using Gui.Widgets.Framework;      // Widget, StatefulWidget, State, RenderObjectWidget, Theme, IFocusable
using Gui.Widgets.Input;          // FocusNode, GestureDetector
using OpenTK.Mathematics;         // Vector2, Vector4
using Scribe.Core.Cuneiform;      // GlyphBundle (cuneiform render path)

namespace Scribe;

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

internal sealed partial class ScribeMultilineFieldState : State<ScribeMultilineField>, IKeyCharHandler, IKeyDownHandler
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

    private void MarkNeedsBuild() => SetState(() => { });

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
