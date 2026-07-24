// ============================================================================
// REFERENCE-ONLY (adopt-libgui-foundation task 2.3). Retained transiently as the proven
// reference implementation for the editor-view port (the next change: "editor view on LibGUI").
// It is NOT wired into any production path — the read-view dialog this change ships is read-only
// and does not use it. DELETE this file once the editor-view change lands and supersedes it.
//
// Originally the spike's throwaway MULTI-LINE editable text widget built on LibGUI's PUBLIC API,
// to answer the final
// go/no-go gate: can we build the wrapping, auto-growing, focus-holding editable row that Scribe's
// lectern needs — the one thing LibGUI's stock (single-line, `internal`) `TextField` does not give?
//
// It mirrors LibGUI's own TextField architecture (read reference/vslibgui/.../Widgets/Input/
// TextField.cs + Core/Input/RenderTextField.cs as templates — those types are `internal`, so this
// REIMPLEMENTS on the public bases rather than subclassing):
//   • ScribeMultilineFieldRender : RenderBox — wraps text to width (greedy word-wrap via the public
//     TextLayoutHelper.MeasureText, since BreakIntoLines is internal), sizes height to line count
//     (the auto-grow), and paints lines + caret with PaintingContext.DrawText / DrawBox.
//   • ScribeMultilineFieldRenderWidget : RenderObjectWidget — the create/update bridge.
//   • ScribeMultilineField : StatefulWidget, IFocusable + its State : IKeyCharHandler,
//     IKeyDownHandler — owns a FocusNode, focuses on click via a GestureDetector, and edits a
//     plain (string text, int caret) model (simpler than TextEditingController for a probe).
//
// PROTOTYPE SUBSET (see plan): insert, backspace, Left/Right + Home/End caret, Enter=newline (this
// probe treats Enter as a hard break so growth is easy to see; the REAL row uses Enter=commit /
// Shift+Enter=break per ScribeRowTextInput). DEFERRED to real adoption: selection, clipboard,
// word-skip, Mac Cmd/Alt caret translation, Enter=commit/Tab=move-focus — all already solved in
// src/Mod/ScribeRowTextInput.cs and portable when/if we adopt.
// ============================================================================

using System;
using System.Collections.Generic;
using Gui.Core.Framework;         // RenderObject, LayoutConstraints
using Gui.Rendering;              // PaintingContext
using Gui.Rendering.Text;         // TextLayoutHelper, FontWeight
using Gui.Widgets.Events;         // KeyboardEvent, IKeyDownHandler, IKeyCharHandler, PointerEvent
using Gui.Widgets.Framework;      // Widget, StatefulWidget, State, RenderObjectWidget, Theme, IFocusable
using Gui.Widgets.Input;          // FocusNode, GestureDetector
using OpenTK.Mathematics;         // Vector2, Vector4
using Vintagestory.API.Client;    // GlKeys

namespace Scribe;

/// <summary>SPIKE. The render object: wraps + paints multi-line text and a caret, auto-sizing height.</summary>
internal sealed class ScribeMultilineFieldRender : Gui.Core.Framework.RenderBox
{
    private string text = "";
    private int caret;
    private bool hasFocus;
    private float fontSize = 15f;
    private Vector4 textColor = Vector4.One;
    private Vector4 caretColor = Vector4.One;
    private const float PadX = 8f;
    private const float PadY = 6f;

    // Cached wrap result from the last PerformLayout, reused by PaintInternal.
    private readonly List<string> visualLines = new();
    private float lineHeight = 18f;

    public string Text { get => text; set => SetProperty(ref text, value ?? "", relayout: true); }
    public int Caret { get => caret; set => SetProperty(ref caret, value, repaint: true); }
    public bool FieldHasFocus { get => hasFocus; set => SetProperty(ref hasFocus, value, repaint: true); }
    public float FontSize { get => fontSize; set => SetProperty(ref fontSize, value, relayout: true); }
    public Vector4 TextColor { get => textColor; set => SetProperty(ref textColor, value, repaint: true); }
    public Vector4 CaretColor { get => caretColor; set => SetProperty(ref caretColor, value, repaint: true); }

    protected override void PerformLayout()
    {
        float availWidth = float.IsPositiveInfinity(Constraints.MaxWidth) ? 300f : Constraints.MaxWidth;
        float textWidth = Math.Max(1f, availWidth - PadX * 2);

        WrapInto(visualLines, text, textWidth, fontSize);
        lineHeight = MeasureLineHeight(fontSize);

        float height = visualLines.Count * lineHeight + PadY * 2;
        // Fill the available width (so it looks like a field), height follows content = auto-grow.
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

        // Text baseline: first line sits PadY down; DrawText's Y is the baseline, so add ascent.
        float ascent = lineHeight * 0.8f;
        for (int i = 0; i < visualLines.Count; i++)
        {
            float y = PadY + i * lineHeight + ascent;
            context.DrawText(visualLines[i], new Vector2(PadX, y), fontSize, textColor, "", FontWeight.Normal);
        }

        // Caret: map the flat caret index onto (line, column) of the wrapped text, then draw a bar.
        if (hasFocus)
        {
            (int line, int col) = CaretToLineCol();
            string upto = line < visualLines.Count ? visualLines[line].Substring(0, Math.Min(col, visualLines[line].Length)) : "";
            float caretX = PadX + TextLayoutHelper.MeasureText(upto, "", fontSize, FontWeight.Normal).X;
            float caretY = PadY + line * lineHeight;
            context.DrawBox(new Vector2(caretX, caretY), new Vector2(2f, lineHeight), caretColor, Vector4.Zero, 0f, Vector4.Zero);
        }
    }

    // Greedy word-wrap to a pixel width, honoring explicit '\n'. Public API only (MeasureText).
    private static void WrapInto(List<string> outLines, string s, float maxWidth, float fontSize)
    {
        outLines.Clear();
        foreach (var paragraph in s.Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                outLines.Add("");
                continue;
            }

            string current = "";
            foreach (var word in paragraph.Split(' '))
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                float w = TextLayoutHelper.MeasureText(candidate, "", fontSize, FontWeight.Normal).X;
                if (w <= maxWidth || current.Length == 0)
                {
                    current = candidate;
                }
                else
                {
                    outLines.Add(current);
                    current = word;
                }
            }
            outLines.Add(current);
        }

        if (outLines.Count == 0)
        {
            outLines.Add("");
        }
    }

    private static float MeasureLineHeight(float fontSize)
    {
        // MeasureText returns (width, height) for a sample; height ~ one line. Fall back to 1.2em.
        float h = TextLayoutHelper.MeasureText("Ag", "", fontSize, FontWeight.Normal).Y;
        return h > 0 ? h : fontSize * 1.2f;
    }

    // Map the flat caret offset (over the ORIGINAL text incl. its '\n') to a (visualLine, column).
    // For the prototype we approximate by walking the wrapped lines and consuming caret characters;
    // this is exact for hard newlines and close enough for soft-wrapped lines to demo caret drawing.
    private (int, int) CaretToLineCol()
    {
        int remaining = Math.Clamp(caret, 0, text.Length);
        for (int i = 0; i < visualLines.Count; i++)
        {
            int lineLen = visualLines[i].Length;
            if (remaining <= lineLen)
            {
                return (i, remaining);
            }
            // +1 accounts for the space/newline consumed between wrapped lines.
            remaining -= lineLen + 1;
            if (remaining < 0)
            {
                return (i, lineLen);
            }
        }
        int last = Math.Max(0, visualLines.Count - 1);
        return (last, visualLines.Count > 0 ? visualLines[last].Length : 0);
    }
}

/// <summary>SPIKE. RenderObjectWidget bridge: pushes text/caret/focus/colors into the render object.</summary>
internal sealed class ScribeMultilineFieldRenderWidget : RenderObjectWidget
{
    public ScribeMultilineFieldRenderWidget(string text, int caret, bool hasFocus, float fontSize,
        Vector4 textColor, Vector4 caretColor, Vector4 boxColor, Vector4 borderColor, float borderThickness,
        Vector4 cornerRadii)
    {
        Text = text;
        Caret = caret;
        HasFocus = hasFocus;
        FontSize = fontSize;
        TextColor = textColor;
        CaretColor = caretColor;
        BoxColor = boxColor;
        BorderColor = borderColor;
        BorderThickness = borderThickness;
        CornerRadii = cornerRadii;
    }

    public string Text { get; }
    public int Caret { get; }
    public bool HasFocus { get; }
    public float FontSize { get; }
    public Vector4 TextColor { get; }
    public Vector4 CaretColor { get; }
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
        ro.FieldHasFocus = HasFocus;
        ro.FontSize = FontSize;
        ro.TextColor = TextColor;
        ro.CaretColor = CaretColor;
        ro.Color = BoxColor;
        ro.BorderColor = BorderColor;
        ro.BorderThickness = BorderThickness;
        ro.CornerRadii = CornerRadii;
    }
}

/// <summary>SPIKE. The public widget: a focusable multi-line editable field with a (text, caret) model.</summary>
public sealed class ScribeMultilineField : StatefulWidget, IFocusable
{
    public ScribeMultilineField(string initialText = "", Action<string>? onChanged = null,
        FocusNode? focusNode = null)
    {
        InitialText = initialText;
        OnChanged = onChanged;
        FocusNode = focusNode;
    }

    public string InitialText { get; }
    public Action<string>? OnChanged { get; }
    public FocusNode? FocusNode { get; }

    public override State CreateState() => new ScribeMultilineFieldState();
}

internal sealed class ScribeMultilineFieldState : State<ScribeMultilineField>, IKeyCharHandler, IKeyDownHandler
{
    private string text = "";
    private int caret;
    private FocusNode focusNode = null!;
    private FocusNode? internalFocusNode;

    public override void InitState()
    {
        base.InitState();
        text = Widget.InitialText;
        caret = text.Length;
        focusNode = Widget.FocusNode ?? (internalFocusNode = new FocusNode());
        // REQUIRED: FocusNode.RequestFocus() resolves its FocusManager via Owner?.Owner?.FocusManager
        // (see Focus.cs). Without setting Owner, RequestFocus can't reach the manager, focus never
        // takes, and the HasFocus-gated key handlers never fire — the exact "non-interactable" bug.
        focusNode.Owner = Element;
        focusNode.AddListener(OnFocusChanged);
    }

    private void OnFocusChanged() => MarkNeedsBuild(); // repaint caret + focus border on focus change

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

        switch (e.KeyCode)
        {
            case (int)GlKeys.BackSpace:
                if (caret > 0)
                {
                    text = text.Remove(caret - 1, 1);
                    caret--;
                    Commit();
                }
                e.Handled = true;
                break;

            case (int)GlKeys.Enter:
            case (int)GlKeys.KeypadEnter:
                // PROTOTYPE: Enter inserts a hard newline so the auto-grow is easy to see. The REAL
                // row uses Enter=commit / Shift+Enter=break (see ScribeRowTextInput) — deferred.
                Insert("\n");
                e.Handled = true;
                break;

            case (int)GlKeys.Left:
                caret = Math.Max(0, caret - 1);
                MarkNeedsBuild();
                e.Handled = true;
                break;

            case (int)GlKeys.Right:
                caret = Math.Min(text.Length, caret + 1);
                MarkNeedsBuild();
                e.Handled = true;
                break;

            case (int)GlKeys.Home:
                caret = 0;
                MarkNeedsBuild();
                e.Handled = true;
                break;

            case (int)GlKeys.End:
                caret = text.Length;
                MarkNeedsBuild();
                e.Handled = true;
                break;

            // Esc intentionally NOT handled — bubbles up to close the dialog (matches Scribe).
        }
    }

    private void Insert(string s)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        text = text.Insert(caret, s);
        caret += s.Length;
        Commit();
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
                hasFocus: focusNode.HasFocus,
                fontSize: 15f,
                textColor: colors.OnSurface,
                caretColor: colors.Primary,
                boxColor: colors.SurfaceHigh,
                borderColor: focusNode.HasFocus ? colors.Primary : colors.Border,
                borderThickness: 1f,
                cornerRadii: Vector4.One * 4f));
    }
}
