using System;
using Gui.Rendering.Text;        // TextStyle
using Gui.Widgets.Basic;         // Text, Container
using Gui.Widgets.Events;        // KeyboardEvent, PointerEvent
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, BuildContext, Theme
using Gui.Widgets.Input;         // TextField, TextEditingController, TextSelection, FocusNode, GestureDetector
using Gui.Widgets.Layout;        // Row, Column, Expanded, Center
using Gui.Widgets.Painting;      // BoxStyle
using OpenTK.Mathematics;        // Vector4
using Vintagestory.API.Client;   // GlKeys

namespace Scribe;

/// <summary>
/// Scribe's numeric-entry control: a text field with +/- buttons, plus <b>up/down arrow-key stepping</b>
/// while the field is focused (scribe-settings-followups 3.4). It is a near-clone of LibGUI's stock
/// <see cref="NumericField"/>, which we could not extend for this: the stock field owns its inner
/// <see cref="TextField"/> privately and passes no key hook, and <see cref="TextField"/> marks every
/// non-Alt key <c>Handled</c> before it bubbles to any ancestor — so there is no way to add arrow
/// stepping from the outside. The one public seam is <see cref="TextField"/>'s own <c>onKeyDown</c>
/// callback (invoked BEFORE it swallows the key, per its source comment "allow parent to intercept keys
/// before TextField handles them"), which the stock field simply doesn't use. This widget composes that
/// seam: same field + buttons + parse/step behavior as the stock one, with an <c>onKeyDown</c> that
/// turns Up/Down into a step.
///
/// <para>Behavior parity with the stock field is deliberate: uncontrolled (seeds from
/// <paramref name="initialValue"/> in <c>InitState</c> only — the caller remounts it via a
/// <c>ValueKey</c> when the persisted/clamped value changes), fires <c>onChanged</c> on every parseable
/// edit, and reverts unparseable text. Clamping still happens in the caller's <c>onChanged</c> (Core's
/// <c>Normalized()</c> is the single clamp source), so stepping past a bound settles back on the next
/// write-through rebuild exactly like the +/- buttons already do.</para>
/// </summary>
/// <summary>
/// Host-owned focus state for a settings form's numeric fields, so focus survives the form's write-through
/// <c>ForceRebuild</c> (scribe-settings-followups focus fix). The dialog owns one of these for the lifetime
/// of the form and passes it into <see cref="ScribeSettingsContent"/> on every rebuild; the content asks it
/// for a per-field persistent <see cref="FocusNode"/> (created once, keyed by a stable field id) and whether
/// each field should auto-focus on mount. When a field is stepped it calls <see cref="ArmAutoFocus"/> with
/// its id; the next rebuilt field with that id re-requests focus. One-shot: reading <see cref="ShouldFocus"/>
/// during a build does not clear it (several fields build per pass), so the host clears it after the build.
/// </summary>
internal sealed class ScribeNumericFocusRegistry
{
    private readonly Dictionary<string, FocusNode> nodes = new();
    private string? armedId;

    /// <summary>The persistent focus node for a field id, created on first request. Reused across rebuilds
    /// so the field can re-grab focus after being unmounted + remounted.</summary>
    public FocusNode NodeFor(string id)
    {
        if (!nodes.TryGetValue(id, out var node))
        {
            node = new FocusNode();
            nodes[id] = node;
        }
        return node;
    }

    /// <summary>Mark a field id as the one to focus after the next rebuild (called from the field's step).</summary>
    public void ArmAutoFocus(string id) => armedId = id;

    /// <summary>Whether the given field id should request focus on mount, consuming the one-shot arm on the
    /// matching read. Each id builds exactly one field per pass, so the first true read is the right one and
    /// self-clears — no separate host "consume" step, which would otherwise have to run AFTER the child
    /// fields build (they read this lazily during mount, not when the host's Build returns).</summary>
    public bool ShouldFocus(string id)
    {
        if (armedId != id) return false;
        armedId = null;
        return true;
    }

    /// <summary>Dispose all owned nodes (host calls this in its own Dispose).</summary>
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
        Action? onStepped = null)
    {
        Value = initialValue;
        Step = step;
        OnChanged = onChanged;
        Style = style ?? new BoxStyle { Height = 40, Width = 100 };
        FocusNode = focusNode;
        AutoFocus = autoFocus;
        OnStepped = onStepped;
    }

    public float Value { get; }
    public float Step { get; }
    public Action<float>? OnChanged { get; }
    public BoxStyle Style { get; }

    /// <summary>Optional host-owned focus node. When supplied it survives the host's write-through
    /// <c>ForceRebuild</c> (which unmounts this widget), so paired with <see cref="AutoFocus"/> the field
    /// can re-grab focus after the rebuild — the same persistent-node pattern the lectern editor uses to
    /// keep the caret across rebuilds (scribe-settings-followups focus fix). Null = internal node (focus
    /// won't survive a rebuild, fine for a field the host doesn't refocus).</summary>
    public FocusNode? FocusNode { get; }

    /// <summary>Request focus on mount. The host sets this for the one field the player just stepped, so
    /// focus returns to it after the write-through rebuild.</summary>
    public bool AutoFocus { get; }

    /// <summary>Fired when the value is stepped via +/- button or arrow key, BEFORE the value write, so the
    /// host can arm auto-focus for this field ahead of the rebuild the write triggers.</summary>
    public Action? OnStepped { get; }

    public override State CreateState() => new ScribeNumericFieldState();
}

internal sealed class ScribeNumericFieldState : State<ScribeNumericField>
{
    private TextEditingController _controller = null!;
    private FocusNode _focusNode = null!;
    private FocusNode? _internalFocusNode;
    private float _currentValue;

    public override void InitState()
    {
        base.InitState();
        _currentValue = Widget.Value;
        _controller = new TextEditingController(_currentValue.ToString());
        _controller.AddListener(OnTextChanged);

        // Prefer a host-owned focus node so focus survives the host's write-through ForceRebuild; fall back
        // to an internal one otherwise. RequestFocus resolves its manager via Owner?.Owner?.FocusManager,
        // so the node needs an Owner or focus never takes (banked lesson from ScribeMultilineField).
        _focusNode = Widget.FocusNode ?? (_internalFocusNode = new FocusNode());
        _focusNode.Owner = Element;

        // Re-grab focus after a rebuild for the field the player just stepped (scribe-settings-followups
        // focus fix), so repeated arrow/button presses keep working without re-clicking the field.
        if (Widget.AutoFocus) _focusNode.RequestFocus();
    }

    /// <summary>Mirror of the stock field's parse-or-revert: a parseable value fires onChanged; junk text
    /// (that isn't a partial-number prefix like "-" or ".") snaps back to the last good value.</summary>
    private void OnTextChanged()
    {
        if (float.TryParse(_controller.Text, out var newValue))
        {
            if (Math.Abs(_currentValue - newValue) > 0.0001f)
            {
                _currentValue = newValue;
                Widget.OnChanged?.Invoke(_currentValue);
            }
        }
        else if (_controller.Text is not ("" or "-" or "." or "," or " "))
        {
            _controller.Value = new TextEditingValue(
                _currentValue.ToString(),
                TextSelection.Collapsed(_currentValue.ToString().Length));
        }
    }

    /// <summary>Step the value by <paramref name="delta"/> (shared by the +/- buttons and arrow keys) and
    /// write it through. Clamping is the caller's job (in onChanged), same as the stock field.</summary>
    private void Adjust(float delta)
    {
        _focusNode.RequestFocus();
        // Tell the host to arm auto-focus for THIS field before the value write, since onChanged triggers a
        // rebuild that unmounts us; on remount InitState re-requests focus (scribe-settings-followups).
        Widget.OnStepped?.Invoke();
        _currentValue += delta;
        _controller.Text = _currentValue.ToString();
        Widget.OnChanged?.Invoke(_currentValue);
        SetState(() => { });
    }

    /// <summary>The <see cref="TextField"/> key-intercept seam: Up/Down step the value by the field's
    /// increment (scribe-settings-followups 3.4). Marking the event Handled stops the field from also
    /// treating the arrow as caret movement. All other keys fall through to the field unchanged.</summary>
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
                    new Center(new Text(label, new TextStyle
                    {
                        FontSize = theme.TextTheme.Body.FontSize,
                        Color = colors.OnPrimary,
                    }))));

        return new Container(
            Widget.Style,
            new Row(children: new Widget[]
            {
                new Expanded(new TextField(
                    _controller,
                    _focusNode,
                    new BoxStyle
                    {
                        Height = fieldHeight,
                        Color = new Vector4(colors.Background.X, colors.Background.Y, colors.Background.Z, 0.9f),
                        BorderThickness = 1,
                        BorderColor = colors.Border,
                    },
                    onKeyDown: OnFieldKeyDown)),
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
        // Only dispose an internal node; a host-owned node outlives this widget's remounts.
        _internalFocusNode?.Dispose();
        base.Dispose();
    }
}
