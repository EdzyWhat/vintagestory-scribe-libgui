// Live cuneiform single-line title input (add-tablet-cuneiform-chrome). The tablet's title bar reads and
// edits in cuneiform like its rows, but the base dialog's title uses a stock LibGUI TextField driven by a
// shared TextEditingController + FocusNode — NOT the ScribeMultilineField State. So this is a purpose-built
// single-line input that is a DROP-IN for that TextField: same controller, same focus node, same OnKeyDown
// hook. The base's title-commit machinery (_isTitleEditing, CommitTitleIfEditing, the blur listener, the
// deferred _pendingTitleEditRebuild/_pendingTitleFocus) all key off that controller/focus node, so they keep
// working unchanged — only the rendered widget differs.
//
// It renders through ScribeCuneiformFieldRender in single-line mode (no wrap; a long title is hard-clipped by
// an enclosing Clip at the band width, since cuneiform has no '…' glyph). Keyboard handling mirrors LibGUI's
// TextField (typing, Backspace/Delete, Left/Right/Home/End, clipboard) minus selection — caret-only for v1,
// matching the row field's accepted scope-cut (selection over cuneiform is the deferred explore stub).

using System;
using Gui.Core.Framework;         // RenderObject
using Gui.Widgets.Events;         // KeyboardEvent, PointerEvent, IKeyDownHandler, IKeyCharHandler
using Gui.Widgets.Framework;      // Widget, StatefulWidget, State, Theme, IFocusable
using Gui.Widgets.Input;          // FocusNode, GestureDetector, TextEditingController, TextSelection, TextEditingValue
using OpenTK.Mathematics;         // Vector2, Vector4
using Scribe.Core.Cuneiform;      // GlyphBundle
using Vintagestory.API.Client;    // GlKeys

namespace Scribe;

/// <summary>
/// A single-line cuneiform text input bound to an external <see cref="TextEditingController"/> and
/// <see cref="FocusNode"/> — a drop-in for the base dialog's stock <see cref="Gui.Widgets.Input.TextField"/>
/// title editor, so <see cref="ScribeDialogBase"/>'s title commit/blur/deferred-rebuild machinery (all keyed
/// off the shared controller and focus node) is untouched. Renders via <see cref="ScribeCuneiformFieldRender"/>
/// in single-line mode with a synthetic caret. Caret-only (no selection highlight) for v1.
/// </summary>
public sealed class ScribeCuneiformTitleField : StatefulWidget, IFocusable
{
    public ScribeCuneiformTitleField(
        TextEditingController controller,
        FocusNode focusNode,
        float fontSizeEm,
        GlyphBundle? bundle,
        Action<KeyboardEvent>? onKeyDown = null,
        Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        Controller = controller;
        FocusNode = focusNode;
        FontSizeEm = fontSizeEm;
        Bundle = bundle;
        OnKeyDown = onKeyDown;
    }

    public TextEditingController Controller { get; }
    public FocusNode FocusNode { get; }
    public float FontSizeEm { get; }
    public GlyphBundle? Bundle { get; }
    /// <summary>Parent key hook, invoked BEFORE this field acts on a key (mirrors LibGUI's TextField). The
    /// tablet points this at the base's shared title key handler for the maxlength gate + Enter/Escape
    /// commit, so a cuneiform title honors the identical limit and commit rules as the normal title field.</summary>
    public Action<KeyboardEvent>? OnKeyDown { get; }

    public override State CreateState() => new ScribeCuneiformTitleFieldState();
}

internal sealed class ScribeCuneiformTitleFieldState : State<ScribeCuneiformTitleField>, IKeyDownHandler, IKeyCharHandler
{
    private TextEditingController Controller => Widget.Controller;
    private FocusNode FocusNode => Widget.FocusNode;

    public override void InitState()
    {
        base.InitState();
        // The controller and focus node are owned by the base dialog (shared with the resting display and the
        // commit machinery); we only listen so a caret/text change from elsewhere repaints this field.
        FocusNode.Owner = Element;
        Controller.AddListener(OnControllerChanged);
        FocusNode.AddListener(OnFocusChanged);
    }

    public override void Dispose()
    {
        Controller.RemoveListener(OnControllerChanged);
        FocusNode.RemoveListener(OnFocusChanged);
        base.Dispose();
    }

    private void OnControllerChanged() => SetState(() => { });
    private void OnFocusChanged() => SetState(() => { });

    public void OnKeyChar(KeyboardEvent e)
    {
        if (!FocusNode.HasFocus || e.KeyChar == '\0' || char.IsControl(e.KeyChar))
        {
            return;
        }
        e.Handled = true;
        InsertAtCaret(e.KeyChar.ToString());
    }

    public void OnKeyDown(KeyboardEvent e)
    {
        if (!FocusNode.HasFocus)
        {
            return;
        }

        // Let the parent (the base title handler) gate the key first — maxlength + Enter/Escape commit. If it
        // marks the event handled, stop before mutating (a blocked over-limit keystroke, or a commit that has
        // already rebuilt the title back to display mode).
        Widget.OnKeyDown?.Invoke(e);
        if (e.Handled)
        {
            return;
        }

        // Swallow other keys so they don't leak to game movement (Alt excepted, per LibGUI's TextField).
        if (!e.Alt)
        {
            e.Handled = true;
        }

        string text = Controller.Text;
        int len = text.Length;
        int caret = Math.Clamp(Controller.Selection.ExtentOffset, 0, len);

        switch (e.KeyCode)
        {
            case (int)GlKeys.BackSpace:
                if (caret > 0)
                {
                    Controller.Text = text.Remove(caret - 1, 1);
                    Controller.Selection = TextSelection.Collapsed(caret - 1);
                }
                break;

            case (int)GlKeys.Delete:
                if (caret < len)
                {
                    Controller.Text = text.Remove(caret, 1);
                    Controller.Selection = TextSelection.Collapsed(caret);
                }
                break;

            case (int)GlKeys.Left:
                Controller.Selection = TextSelection.Collapsed(Math.Max(0, caret - 1));
                break;

            case (int)GlKeys.Right:
                Controller.Selection = TextSelection.Collapsed(Math.Min(len, caret + 1));
                break;

            case (int)GlKeys.Home:
                Controller.Selection = TextSelection.Collapsed(0);
                break;

            case (int)GlKeys.End:
                Controller.Selection = TextSelection.Collapsed(len);
                break;

            case (int)GlKeys.V when e.Ctrl:
                string? clip = Element?.Owner?.GetClipboard()?.GetText();
                if (!string.IsNullOrEmpty(clip))
                {
                    InsertAtCaret(clip);
                }
                break;
        }
    }

    private void InsertAtCaret(string s)
    {
        string text = Controller.Text;
        int caret = Math.Clamp(Controller.Selection.ExtentOffset, 0, text.Length);
        Controller.Text = text.Insert(caret, s);
        Controller.Selection = TextSelection.Collapsed(caret + s.Length);
    }

    /// <summary>Place the caret from a click. Cuneiform hit-testing lives in the render object
    /// (<see cref="ScribeCuneiformFieldRender.OffsetAtPosition"/>); the GestureDetector proxy wraps it as its
    /// single child, so a proxy-local point maps straight through.</summary>
    private void OnPress(PointerEvent e)
    {
        FocusNode.RequestFocus();
        e.Handled = true;

        if (Element?.RenderObject is not { } proxy) return;
        var render = proxy.Children.Count > 0 ? proxy.Children[0] as IScribeEditableTextRender : null;
        if (render is null) return;

        Vector2 local = proxy.GlobalToLocal(new Vector2(e.X, e.Y));
        int offset = Math.Clamp(render.OffsetAtPosition(local), 0, Controller.Text.Length);
        Controller.Selection = TextSelection.Collapsed(offset);
    }

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        int caret = Math.Clamp(Controller.Selection.ExtentOffset, 0, Controller.Text.Length);

        return new GestureDetector(
            onPress: OnPress,
            child: new ScribeCuneiformFieldRenderWidget(
                text: Controller.Text,
                caret: caret,
                hasFocus: FocusNode.HasFocus,
                fontSizeEm: Widget.FontSizeEm,
                inkColor: colors.OnSurface,
                caretColor: colors.Primary,
                bundle: Widget.Bundle,
                padX: 0f,   // the title band supplies its own inset; keep the glyphs flush like the RichText did
                padY: 0f,
                boxColor: Vector4.Zero,
                borderColor: Vector4.Zero,
                borderThickness: 0f,
                cornerRadii: Vector4.Zero,
                singleLine: true));
    }
}
