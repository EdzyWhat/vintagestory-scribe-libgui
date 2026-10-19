using System;
using Gui.Core.Framework;        // ClipBehavior, RenderObject, RenderBox, RenderProxyBox
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle
using Gui.Widgets.Basic;         // Text, Container
using Gui.Widgets.Basic.Theming; // DefaultTextStyle (Task Text Font ancestor)
using Gui.Widgets.Events;        // KeyboardEvent, PointerEvent
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, BuildContext, Theme, SingleChildWidget
using Gui.Widgets.Input;         // TextField, TextEditingController, TextSelection, FocusNode, GestureDetector
using Gui.Widgets.Layout;        // Row, Column, Expanded, Center, Padding, CrossAxisAlignment
using Gui.Core.Layout;           // MainAxisSize
using Gui.Widgets.Painting;      // BoxStyle, Transform
using OpenTK.Mathematics;        // Vector2, Vector4
using Vintagestory.API.Client;   // GlKeys

namespace Scribe;

/// <summary>
/// Scribe's numeric-entry control: a text field with +/- buttons plus up/down arrow-key stepping while
/// focused. A near-clone of LibGUI's stock <see cref="NumericField"/>, which cannot be extended: it owns its
/// inner <see cref="TextField"/> privately, and <see cref="TextField"/> marks every non-Alt key <c>Handled</c>
/// before it bubbles. The one public seam is <see cref="TextField"/>'s <c>onKeyDown</c> callback (invoked
/// before it swallows the key), which this widget uses to turn Up/Down into a step.
///
/// <para>Like the stock field it is uncontrolled (seeds from <c>initialValue</c> in <c>InitState</c> only;
/// callers remount via a <c>ValueKey</c> when the value changes) and reverts unparseable text.</para>
///
/// <para><b>Clamp on unfocus.</b> Typing edits locally only, so the caller's <c>onChanged</c> →
/// <c>Normalized()</c> → <c>ValueKey</c> remount can't snap the field to a bound mid-edit (which would make
/// select-all-and-retype impossible). On BLUR the field parses its text, applies the optional
/// <see cref="Clamp"/> callback (Core stays the range source of truth), rewrites its text, and fires
/// <c>onChanged</c> once, only if the value differs from the mounted value (see
/// <see cref="OnFocusChanged"/>). The +/- buttons and arrow keys write through live.</para>
/// </summary>
/// <summary>
/// Host-owned focus state for a settings form's numeric fields, so focus survives the form's write-through
/// <c>ForceRebuild</c>. The dialog owns one for the form's lifetime and passes it into
/// <see cref="ScribeSettingsContent"/> on every rebuild; fields get a persistent <see cref="FocusNode"/>
/// keyed by a stable field id. A stepped field calls <see cref="ArmAutoFocus"/>; the next rebuilt field
/// with that id re-requests focus.
/// </summary>
internal sealed class ScribeNumericFocusRegistry
{
    private readonly Dictionary<string, FocusNode> nodes = new();
    private string? armedId;

    /// <summary>The persistent focus node for a field id, created on first request.</summary>
    public FocusNode NodeFor(string id)
    {
        if (!nodes.TryGetValue(id, out var node))
        {
            node = new FocusNode();
            nodes[id] = node;
        }
        return node;
    }

    /// <summary>Marks a field id to focus after the next rebuild.</summary>
    public void ArmAutoFocus(string id) => armedId = id;

    /// <summary>Whether the field id should request focus on mount; the matching read consumes the one-shot
    /// arm. Self-clearing because fields read this lazily during mount, after the host's Build returns.</summary>
    public bool ShouldFocus(string id)
    {
        if (armedId != id) return false;
        armedId = null;
        return true;
    }

    /// <summary>Disposes all owned nodes.</summary>
    public void Dispose()
    {
        foreach (var node in nodes.Values) node.Dispose();
        nodes.Clear();
    }
}

public sealed class ScribeNumericField : StatefulWidget
{
    public ScribeNumericField(
        float initialValue = 0,
        float step = 1,
        Action<float>? onChanged = null,
        BoxStyle? style = null,
        FocusNode? focusNode = null,
        bool autoFocus = false,
        Action? onStepped = null,
        Func<float, float>? clamp = null,
        TextStyle? textStyle = null,
        Vector4? focusBorderColor = null)
    {
        Value = initialValue;
        Step = step;
        OnChanged = onChanged;
        Style = style ?? new BoxStyle { Height = 40, Width = 100 };
        FocusNode = focusNode;
        AutoFocus = autoFocus;
        OnStepped = onStepped;
        Clamp = clamp;
        TextStyle = textStyle;
        FocusBorderColor = focusBorderColor;
    }

    public float Value { get; }
    public float Step { get; }
    public Action<float>? OnChanged { get; }
    public BoxStyle Style { get; }

    /// <summary>Optional clamp applied to the typed value on BLUR (a Core <c>Clamp*</c> static); null commits
    /// as typed. Core owns the range, the field owns only the timing.</summary>
    public Func<float, float>? Clamp { get; }

    /// <summary>Optional host-owned focus node that survives the host's write-through <c>ForceRebuild</c>;
    /// paired with <see cref="AutoFocus"/> the field re-grabs focus after the rebuild. Null uses an internal
    /// node whose focus does not survive a rebuild.</summary>
    public FocusNode? FocusNode { get; }

    /// <summary>Request focus on mount (set for the field the player just stepped).</summary>
    public bool AutoFocus { get; }

    /// <summary>Fired on a step, BEFORE the value write, so the host can arm auto-focus ahead of the rebuild.</summary>
    public Action? OnStepped { get; }

    /// <summary>Optional text style for the number input, overriding the theme default.</summary>
    public TextStyle? TextStyle { get; }

    /// <summary>Focused-border color for the typing box (<see cref="ScribeRowStyle.InputFocusBorderColor"/>).
    /// Null → theme Primary.</summary>
    public Vector4? FocusBorderColor { get; }

    public override State CreateState() => new ScribeNumericFieldState();
}

internal sealed class ScribeNumericFieldState : State<ScribeNumericField>
{
    private TextEditingController _controller = null!;
    private FocusNode _focusNode = null!;
    private FocusNode? _internalFocusNode;
    private float _currentValue;
    private bool _hadFocus;

    public override void InitState()
    {
        base.InitState();
        _currentValue = Widget.Value;
        _controller = new TextEditingController(_currentValue.ToString());
        _controller.AddListener(OnTextChanged);

        // RequestFocus resolves its manager via Owner?.Owner?.FocusManager, so the node needs an Owner or focus never takes.
        _focusNode = Widget.FocusNode ?? (_internalFocusNode = new FocusNode());
        _focusNode.Owner = Element;

        // TextField exposes no onBlur, so blur is detected via the focus node (removed in Dispose; the node may be host-owned).
        _hadFocus = _focusNode.HasFocus;
        _focusNode.AddListener(OnFocusChanged);

        if (Widget.AutoFocus) _focusNode.RequestFocus();
    }

    /// <summary>Resyncs the displayed value when an in-place reconcile hands this state a new widget whose
    /// <see cref="ScribeNumericField.Value"/> changed. The field is otherwise seeded in <see cref="InitState"/>
    /// only; the editor reconcile is keyed by <c>TaskId</c>, so a Craft parent's step reuses every ingredient
    /// row's field and it would paint a stale count.
    ///
    /// <para>The <c>!HasFocus</c> gate is load-bearing: it never stomps the field being edited (whose
    /// <see cref="Adjust"/> already took focus and whose <c>_currentValue</c> already reflects the step), so
    /// the other rows live-update while the caret stays put.</para></summary>
    public override void UpdateWidget(ScribeNumericField oldWidget)
    {
        base.UpdateWidget(oldWidget);
        if (Math.Abs(Widget.Value - oldWidget.Value) > 0.0001f && !_focusNode.HasFocus)
        {
            _currentValue = Widget.Value;
            string text = _currentValue.ToString();
            if (_controller.Text != text)
            {
                _controller.Value = new TextEditingValue(text, TextSelection.Collapsed(text.Length));
            }
        }
    }

    /// <summary>Typing edits only the local text; the commit is deferred to blur
    /// (<see cref="OnFocusChanged"/>) because a per-keystroke write would remount and re-seed the field
    /// mid-edit. Unparseable junk (not a partial-number prefix) snaps back to the last good value.</summary>
    private void OnTextChanged()
    {
        if (float.TryParse(_controller.Text, out var newValue))
        {
            _currentValue = newValue;
        }
        else if (_controller.Text is not ("" or "-" or "." or "," or " "))
        {
            _controller.Value = new TextEditingValue(
                _currentValue.ToString(),
                TextSelection.Collapsed(_currentValue.ToString().Length));
        }
    }

    /// <summary>On focus LOST, commits the typed value once: parse, apply <see cref="ScribeNumericField.Clamp"/>,
    /// rewrite the text, and fire <c>onChanged</c>. Only the focus edge is acted on.
    ///
    /// <para><b>Step-button unfocus guard.</b> A step button is a bare <see cref="GestureDetector"/>, not
    /// focusable, so pressing it blurs this field on pointer-DOWN (LibGUI's <c>DispatchPointerDown</c> calls
    /// <c>RequestFocus(null)</c>). If that blur fired <c>onChanged</c>, the host's synchronous
    /// <c>ForceRebuild</c> would unmount the button mid-press, so its pointer-UP (<see cref="Adjust"/>) never
    /// runs. Hence <c>onChanged</c> is skipped when the committed value equals the mounted value.</para></summary>
    private void OnFocusChanged()
    {
        bool has = _focusNode.HasFocus;
        if (has == _hadFocus) return;
        _hadFocus = has;

        if (!has)
        {
            if (!float.TryParse(_controller.Text, out var typed)) typed = _currentValue;
            float committed = Widget.Clamp is not null ? Widget.Clamp(typed) : typed;

            _currentValue = committed;
            string text = committed.ToString();
            if (_controller.Text != text)
            {
                _controller.Value = new TextEditingValue(text, TextSelection.Collapsed(text.Length));
            }

            // Write through only on a real change (see the step-button guard above).
            if (Math.Abs(committed - Widget.Value) > 0.0001f)
            {
                Widget.OnChanged?.Invoke(committed);
                return;
            }
        }

        // Repaint so the border tracks focus (skipped above when onChanged already rebuilds the parent).
        SetState(() => { });
    }

    /// <summary>Steps the value by <paramref name="delta"/> (buttons and arrow keys) and writes it through;
    /// clamping is the caller's job.</summary>
    private void Adjust(float delta)
    {
        _focusNode.RequestFocus();
        // Armed before the write because onChanged triggers a rebuild that unmounts us; InitState re-requests focus.
        Widget.OnStepped?.Invoke();
        _currentValue += delta;
        _controller.Text = _currentValue.ToString();
        Widget.OnChanged?.Invoke(_currentValue);
        SetState(() => { });
    }

    /// <summary>Up/Down step the value; Handled stops the field also treating the arrow as caret movement.
    /// Other keys fall through.</summary>
    private void OnFieldKeyDown(KeyboardEvent e)
    {
        switch (e.KeyCode)
        {
            case (int)GlKeys.Up:
                Adjust(Widget.Step);
                e.Handled = true;
                break;
            case (int)GlKeys.Down:
                Adjust(-Widget.Step);
                e.Handled = true;
                break;
        }
    }

    public override Widget Build(BuildContext context)
    {
        var theme = Theme.Of(context);
        var colors = theme.ColorScheme;

        float fieldHeight = Widget.Style.Height ?? 40;
        float buttonSize = fieldHeight / 2;

        // Body-sized glyphs overflow a half-field button: Text's layout box includes descent, so the symbol
        // sits low. Shrink to 85% and nudge 4% up to center optically (stock uses SVG buttons instead).
        float glyphSize = buttonSize * 0.85f;
        Widget MakeButton(string label, Action<PointerEvent> onTap) =>
            new GestureDetector(
                onTap: onTap,
                child: new Container(
                    new BoxStyle
                    {
                        Width = buttonSize,
                        Height = buttonSize,
                        Color = colors.Primary,
                        CornerRadius = Vector4.Zero,
                    },
                    new Center(Transform.Translate(
                        new Text(label, new TextStyle
                        {
                            FontSize = glyphSize,
                            Color = colors.OnPrimary,
                        }),
                        new Vector2(0f, -buttonSize * 0.04f)))));

        // SurfaceHigh is the multiline text field's box color, so an unstyled numeric field matches a text input on every theme.
        var fillColor = Widget.Style.Color.W > 0
            ? Widget.Style.Color
            : colors.SurfaceHigh;
        bool focused = _focusNode.HasFocus;
        var borderColor = focused
            ? (Widget.FocusBorderColor ?? colors.Primary)
            : colors.Border;

        // LibGUI's TextField hardcodes a 10px inset on both sides; shift it inward so the visual pad is 5px,
        // clipping after the chrome paints so overflow does not spill into the +/- column.
        //
        // Chrome MUST be a RenderProxyBox (ScribeTightChrome), not a Container: Container gives children
        // loose constraints, so a TextField with only Height collapses to 0 width. RenderProxyBox forwards
        // the Expanded slot's tight width.
        const float stockTextPadX = 10f;
        const float visualPadX = 5f;
        float shift = stockTextPadX - visualPadX;
        var inherited = DefaultTextStyle.Of(context);
        var typingTextStyle = (Widget.TextStyle ?? new TextStyle { Color = colors.OnSurface })
            .Merge(inherited);

        Widget typingBox = new ScribeTightChrome(
            fillColor,
            borderColor,
            borderThickness: 1f,
            cornerRadii: Vector4.One * 4f,
            child: new ScribeTightClip(
                child: new Padding(
                    EdgeInsets.Only(left: -shift, right: -shift),
                    child: new TextField(
                        _controller,
                        _focusNode,
                        new TextFieldStyle
                        {
                            Height = fieldHeight,
                            FillColor = Vector4.Zero,
                            BorderThickness = 0,
                            TextStyle = typingTextStyle,
                        },
                        onKeyDown: OnFieldKeyDown))));

        return new Container(
            Widget.Style,
            new Row(children: new Widget[]
            {
                new Expanded(typingBox),
                new Column(children: new Widget[]
                {
                    MakeButton("+", _ => Adjust(Widget.Step)),
                    MakeButton("-", _ => Adjust(-Widget.Step)),
                }),
            }));
    }

    public override void Dispose()
    {
        _controller.RemoveListener(OnTextChanged);
        _controller.Dispose();
        // A host-owned node outlives remounts: remove the listener to avoid leaking a dead handler, and dispose only an internal node.
        _focusNode.RemoveListener(OnFocusChanged);
        _internalFocusNode?.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// A single-child box that paints fill/border like a <c>Container</c> but lays out its child with the
/// <b>same</b> constraints it received. <c>Container</c> always passes loose constraints, which collapses a
/// <see cref="TextField"/> (Height set, Width unset) in an <c>Expanded</c> to 0px wide.
/// </summary>
internal sealed class ScribeTightChrome : SingleChildWidget
{
    public ScribeTightChrome(
        Vector4 color,
        Vector4 borderColor,
        float borderThickness,
        Vector4 cornerRadii,
        Widget? child = null)
        : base(child)
    {
        Color = color;
        BorderColor = borderColor;
        BorderThickness = borderThickness;
        CornerRadii = cornerRadii;
    }

    public Vector4 Color { get; }
    public Vector4 BorderColor { get; }
    public float BorderThickness { get; }
    public Vector4 CornerRadii { get; }

    public override RenderObject CreateRenderObject() => new RenderProxyBox();

    public override void UpdateRenderObject(RenderObject renderObject)
    {
        var box = (RenderBox)renderObject;
        box.Color = Color;
        box.BorderColor = BorderColor;
        box.BorderThickness = BorderThickness;
        box.CornerRadii = CornerRadii;
        box.ClipBehavior = ClipBehavior.None;
    }
}

/// <summary>
/// Clips a child without painting fill or border. Nested inside <see cref="ScribeTightChrome"/> so the 1px
/// chrome stroke is not shaved by the anti-aliased clip.
/// </summary>
internal sealed class ScribeTightClip : SingleChildWidget
{
    public ScribeTightClip(Widget? child = null) : base(child) { }

    public override RenderObject CreateRenderObject() => new RenderProxyBox();

    public override void UpdateRenderObject(RenderObject renderObject)
        => ((RenderBox)renderObject).ClipBehavior = ClipBehavior.AntiAlias;
}
