using System;
using Cairo;
using Vintagestory.API.Client;

namespace Scribe;

/// <summary>
/// A multi-line editor row input, subclassing <see cref="GuiElementTextArea"/> so a long row
/// WRAPS (and grows in height) the same way its static <see cref="ScribeRowElement"/> label does,
/// instead of the single-line horizontal scroll a <c>GuiElementTextInput</c> gave (which collapsed
/// a wrapped row to one line on focus). <c>GuiElementTextArea</c> sets <c>multilineMode = true</c>
/// and ships an <c>Autoheight</c> mechanism (<c>TextChanged</c> re-measures <c>Bounds.fixedHeight</c>
/// via <c>GetMultilineTextHeight</c> on every keystroke); the dialog wires that height change back
/// into the row list so the focused row grows and the rows below it shift (see
/// <c>GuiDialogScribeLectern.OnEditInputTextChanged</c>).
///
/// Keyboard model (Enter/Shift+Enter/Tab), all handled BEFORE delegating to the base:
/// <list type="bullet">
///   <item><b>Plain Enter / keypad Enter</b> -> commit + advance to the next row. ALWAYS consumed,
///     never delegated -- in <c>multilineMode</c> the base's <c>OnKeyEnter()</c> would otherwise
///     insert a newline. Enter is a commit gesture here, never a text key.</item>
///   <item><b>Shift+Enter</b> -> deliberately NOT consumed: it falls through to the base so
///     <c>OnKeyEnter()</c> inserts a hard line break, and the auto-height wiring grows the row.</item>
///   <item><b>Shift+Tab</b> -> commit + retreat to the previous row (always consumed).</item>
///   <item><b>Plain Tab</b> -> consumed as a no-op (a tab glyph inside a task line is unwanted, and
///     multiline mode would otherwise insert one).</item>
///   <item><b>Esc</b> -> deliberately NOT intercepted: the base leaves KeyCode 50 unhandled, so it
///     bubbles up and closes the dialog (the wanted panic-close, decided 2026-07-21). Blur on close
///     still commits the pending edit, so nothing typed is lost.</item>
/// </list>
///
/// The spike (VSAPI-NOTES "Text-input caret / selection conventions") found the shared base
/// <c>GuiElementEditableTextBase</c> already implements selection, word-skip, clipboard, and
/// line-jump -- but its caret nav is gated on <c>CtrlPressed</c> only (never <c>CommandPressed</c>)
/// and its <c>OnKeyDownInternal</c> hard-returns on any <c>AltPressed</c>. So on a Mac, Cmd+Arrow
/// (line ends) and Alt/Option+Arrow (word-skip) do nothing. Rather than reimplement caret logic,
/// this override <b>translates the Mac modifier combos into the Windows-keyed combos the base
/// already handles</b> (see <see cref="TranslateMacCaretModifiers"/>), then delegates.
///
/// This subclass also <b>drops the base's embossed border + dark fill box</b> (see
/// <see cref="ComposeTextElements"/>): the row is drawn by <see cref="ScribeRowElement"/>'s
/// lined-paper look, and a boxed input border over one row read as the text "jumping" on focus
/// (playtest 2026-07-21). Only a subtle focused-highlight background remains -- built here as this
/// element's OWN texture because <c>GuiElementTextArea</c>'s highlight members are private to it.
/// </summary>
public sealed class ScribeRowTextInput : GuiElementTextArea
{
    // GlKeys codes (confirmed via decompile of GlKeys): Up=45, Down=46, Left=47, Right=48,
    // Enter=49, Escape=50, Tab=52, Home=58, End=59, KeypadEnter=82.
    private const int KeyLeft = 47;
    private const int KeyRight = 48;
    private const int KeyEnter = 49;
    // Escape (50) is intentionally not referenced: we let it fall through to the base so it
    // bubbles up and closes the dialog (panic-close, decided 2026-07-21).
    private const int KeyTab = 52;
    private const int KeyHome = 58;
    private const int KeyEnd = 59;
    private const int KeyKeypadEnter = 82;

    /// <summary>Plain Enter (or keypad Enter) pressed while this row is focused: commit + advance to
    /// the next row.</summary>
    private readonly Func<bool> onCommitAndAdvance;

    /// <summary>Shift+Tab pressed: commit + retreat to the previous row.</summary>
    private readonly Func<bool> onCommitAndRetreat;

    /// <summary>Focus lost without an Enter/Shift+Tab (e.g. the player clicked away): commit
    /// the row's edit. May be null when the dialog does not need a blur hook.</summary>
    private readonly Action? onBlur;

    public ScribeRowTextInput(
        ICoreClientAPI capi,
        ElementBounds bounds,
        Action<string> onTextChanged,
        CairoFont font,
        Func<bool> onCommitAndAdvance,
        Func<bool> onCommitAndRetreat,
        Action? onBlur = null)
        : base(capi, bounds, onTextChanged, font)
    {
        this.onCommitAndAdvance = onCommitAndAdvance;
        this.onCommitAndRetreat = onCommitAndRetreat;
        this.onBlur = onBlur;

        // Disable GuiElementTextArea's self-sizing. The row's height is owned by the dialog: each
        // compose sizes this input's bounds to the shared ScribeRowElement.RowHeightFixed, and any
        // height change (wrap or newline) is driven by OnEditInputTextChanged -> recompose. Left ON,
        // Autoheight re-measures the element in the base's internal TextChanged() (fired on every
        // SetValue) using the raw GetMultilineTextHeight -- which does NOT count a trailing newline --
        // so a Shift+Enter'd row shrank the INPUT back to one line even though the row slot grew,
        // leaving the caret rendering below the box (playtest 2026-07-22T00-02-12, task 4.6). With it
        // off, the input keeps the height compose gave it and the two measures can't diverge.
        Autoheight = false;
    }

    /// <summary>
    /// Drops <see cref="GuiElementTextArea"/>'s boxed border (its <c>EmbossRoundRectangleElement</c>
    /// + dark <c>0.2</c> fill) while keeping everything else working. The trick (decompile-confirmed):
    /// the base's <c>ComposeTextElements</c> draws the emboss + fill onto the PASSED <c>ctx</c>, but
    /// its <c>GenerateHighlight()</c> and <c>RecomposeText()</c> IGNORE that ctx -- they build their
    /// own textures (the focus highlight, the glyph texture) plus set the private
    /// <c>highlightBounds</c>, all off <c>Bounds</c>. So we call <c>base.ComposeTextElements</c> with a
    /// THROWAWAY surface/context: the border draws onto scratch we immediately discard, while the
    /// highlight + text + <c>highlightBounds</c> initialize correctly on the element.
    ///
    /// This is required (not just cosmetic): the base's <c>RenderInteractiveElements</c>
    /// unconditionally renders <c>highlightBounds</c> when focused, so if we skipped
    /// <c>GenerateHighlight</c> entirely (as the old <c>GuiElementTextInput</c>-based override did),
    /// <c>highlightBounds</c> would be null and focusing a row would crash with a
    /// <c>NullReferenceException</c> (VintagestoryData crash 2026-07-21T23-25). We can't reproduce
    /// <c>GenerateHighlight</c>/<c>RecomposeText</c> ourselves -- they're private to the base and the
    /// glyph <c>textTexture</c> is <c>internal</c> -- so routing through the base with a discarded ctx
    /// is the way to get the borderless look without reimplementing inaccessible engine internals.
    /// </summary>
    public override void ComposeTextElements(Context ctx, ImageSurface surface)
    {
        int w = System.Math.Max(1, (int)Bounds.OuterWidth);
        int h = System.Math.Max(1, (int)Bounds.OuterHeight);
        var throwaway = new ImageSurface(Format.Argb32, w, h);
        Context throwawayCtx = genContext(throwaway);

        // Emboss + dark fill land on the throwaway (discarded); GenerateHighlight + RecomposeText
        // build the real highlight/text textures + set highlightBounds off Bounds regardless.
        base.ComposeTextElements(throwawayCtx, throwaway);

        throwawayCtx.Dispose();
        throwaway.Dispose();
    }

    /// <summary>
    /// Renders as the base does, but skips drawing entirely when this row is scrolled fully outside
    /// the enclosing <c>BeginClip</c> window.
    ///
    /// <para>Unlike <c>GuiElementTextInput</c> (whose <c>GlScissorFlag(false)</c> globally disabled
    /// the scissor and caused a "new task drawn below the box" bleed, playtest 2026-07-21T20-58-36),
    /// <c>GuiElementTextArea</c> does NOT touch the scissor -- so the ambient dialog clip already
    /// clips this input's text, and the old <c>PushScissor</c>/<c>PopScissor</c> re-assert is no longer
    /// needed. The off-screen skip is therefore defensive rather than load-bearing, but it's cheap and
    /// focus-safe (the input stays focusable/typable and reappears when its row scrolls back in). Uses
    /// <c>renderY</c>, which tracks the parent's scroll <c>fixedY</c> shift; vertical overlap only (the
    /// list scrolls only vertically).</para>
    /// </summary>
    public override void RenderInteractiveElements(float deltaTime)
    {
        if (InsideClipBounds != null)
        {
            double top = Bounds.renderY;
            double bottom = top + Bounds.InnerHeight;
            double clipTop = InsideClipBounds.renderY;
            double clipBottom = clipTop + InsideClipBounds.InnerHeight;
            // Fully above or fully below the visible window -> don't draw.
            if (bottom <= clipTop || top >= clipBottom) return;
        }

        base.RenderInteractiveElements(deltaTime);
    }

    public override void OnFocusLost()
    {
        base.OnFocusLost();
        // Blur commits the pending edit (task 4.3). The dialog guards against a recompose-driven
        // blur double-committing; a real click-away commit is idempotent anyway (FlushIfDirty is a
        // no-op when nothing changed).
        onBlur?.Invoke();
    }

    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
    {
        if (!HasFocus) return;

        // ---- Cross-row navigation / newline / Tab: handle before the base sees the key ----

        // Shift+Tab retreats. Always consume it: in multiline mode the base treats Tab (52) as an
        // insertable character, so falling through would type a tab glyph.
        if (args.KeyCode == KeyTab && args.ShiftPressed)
        {
            onCommitAndRetreat();
            args.Handled = true;
            return;
        }

        // Plain Tab: consume as a no-op (don't insert a tab glyph; we don't use Tab for traversal).
        if (args.KeyCode == KeyTab)
        {
            args.Handled = true;
            return;
        }

        // Plain Enter (main or keypad) commits and advances. ALWAYS consume -- if we let it fall
        // through, the base's multiline OnKeyEnter() would insert a newline. Enter is never a text
        // key here.
        if ((args.KeyCode == KeyEnter || args.KeyCode == KeyKeypadEnter) && !args.ShiftPressed)
        {
            onCommitAndAdvance();
            args.Handled = true;
            return;
        }

        // Shift+Enter: deliberately NOT intercepted -- it falls through to the base so OnKeyEnter()
        // inserts a hard line break. The dialog's auto-height wiring then grows the row.

        // Escape is deliberately NOT handled here either: the base leaves KeyCode 50 unhandled, so
        // it bubbles up and closes the dialog (panic-close). Blur on close still commits the edit.

        // ---- Mac caret-convention translation, then delegate to the base ----
        base.OnKeyDown(api, TranslateMacCaretModifiers(args));
    }

    /// <summary>
    /// Rewrites a key event so the base's Windows-keyed caret navigation responds to the Mac
    /// modifier idioms. Only touches Left/Right arrow events carrying Cmd or Alt; every other event
    /// (including a plain Shift+Enter) is returned unchanged. Shift is always preserved so
    /// selection-extend still works.
    /// </summary>
    private static KeyEvent TranslateMacCaretModifiers(KeyEvent args)
    {
        bool isArrow = args.KeyCode == KeyLeft || args.KeyCode == KeyRight;
        if (!isArrow) return args;

        // Cmd+Arrow -> jump to line start/end. The base moves to line ends on Home/End, so rewrite
        // the key code and drop the Cmd flag (leave Ctrl off so it's the current-line Home/End, not
        // the Ctrl+Home/End whole-document jump).
        if (args.CommandPressed)
        {
            return new KeyEvent
            {
                KeyCode = args.KeyCode == KeyLeft ? KeyHome : KeyEnd,
                KeyChar = args.KeyChar,
                ShiftPressed = args.ShiftPressed,
                CtrlPressed = false,
                CommandPressed = false,
                AltPressed = false,
            };
        }

        // Alt/Option+Arrow -> word-skip. The base runs MoveCursor(dir, wholeWord: args.CtrlPressed)
        // for a plain arrow, but ONLY after an `if (args.AltPressed) return;` early-out. So clear
        // Alt and set Ctrl: the early-out is skipped and the arrow does a whole-word move.
        if (args.AltPressed)
        {
            return new KeyEvent
            {
                KeyCode = args.KeyCode,
                KeyChar = args.KeyChar,
                ShiftPressed = args.ShiftPressed,
                CtrlPressed = true,
                CommandPressed = false,
                AltPressed = false,
            };
        }

        return args;
    }
}
