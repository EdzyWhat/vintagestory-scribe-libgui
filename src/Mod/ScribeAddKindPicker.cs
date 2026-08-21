using System;
using System.Collections.Generic;
using Gui.Core.Framework;        // ClipBehavior (segmented group rounded clip)
using Gui.Core.Layout;           // MainAxisSize
using Gui.Core.Painting;         // LayerLink
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle
using Gui.Widgets.Animations;    // AnimationController, Curves (drop-up entry animation)
using Gui.Widgets.Basic;         // Container, Button, Text
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, Theme, ButtonStyle, ButtonVariant, ColorScheme, Key
using Gui.Widgets.Input;         // GestureDetector (barrier)
using Gui.Widgets.Layout;        // Column, Row, Expanded, SizedBox, Positioned, Alignment, CrossAxisAlignment, MainAxisAlignment
using Gui.Widgets.Overlay;       // Overlay, OverlayEntry
using Gui.Widgets.Painting;      // BoxStyle, CompositedTransformTarget, CompositedTransformFollower, HitTestBehavior, Opacity, Transform
using OpenTK.Mathematics;        // Vector2, Vector4
using Vintagestory.API.Config;   // Lang

namespace Scribe;

/// <summary>
/// The editor footer's "add" control (add-note-kind-picker D1): a two-part SEGMENTED button — a primary
/// button that adds the currently-selected kind (defaults to Task, so one click still adds a task) and a
/// caret button that opens a floating drop-up menu of the available kinds (Task / Note, from
/// <see cref="ScribeAddKinds.Live"/>). Picking a kind makes it the primary button's kind AND adds a block
/// of that kind immediately.
///
/// <para>The menu is a FLOATING overlay that grows UPWARD from the button, OVER the scroll body — the
/// scroll area keeps its exact height and nothing reflows (the earlier "inline, footer-growing" idea was
/// rejected: it shrank the scroll body as the list opened). This mirrors LibGUI's own <c>Dropdown</c>
/// exactly: a <see cref="LayerLink"/> ties the on-screen button (a <see cref="CompositedTransformTarget"/>)
/// to a <see cref="CompositedTransformFollower"/> inserted into the <see cref="Overlay"/>, with
/// <c>showAbove: true</c> so the menu's bottom edge pins to the button's top edge. A full-screen barrier
/// entry closes the menu on any outside tap. The floating approach avoids clipping the menu against the
/// dialog's bottom edge (a downward menu would).</para>
///
/// <para>State (selected kind + open/closed) lives on <see cref="ScribeAddKindPickerState"/>, which sits at
/// a stable position in the footer, so it survives the dialog's positional reconcile (RebuildBody) — the
/// remembered kind persists for the life of the open dialog and resets to Task on the next open (a fresh
/// State), per the design's "remember within the open dialog" lean. <see cref="State.Dispose"/> tears down
/// any open overlay entries, exactly as <c>DropdownState</c> does.</para>
/// </summary>
internal sealed class ScribeAddKindPicker : StatefulWidget
{
    public ScribeAddKindPicker(
        Action<ScribeAddKind> onAdd,
        bool addTaskEnabled,
        ScribeRowStyle style,
        ScribeAmbientLightSampler.Shade currentShade,
        Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        OnAdd = onAdd;
        AddTaskEnabled = addTaskEnabled;
        Style = style;
        CurrentShade = currentShade;
    }

    /// <summary>Add a block of the given kind (wired to <see cref="ScribeDialogBase.OnClickAdd"/>).</summary>
    public Action<ScribeAddKind> OnAdd { get; }

    /// <summary>Whether a block of any kind may currently be added. When false (tablet/chalkboard at its
    /// 10-entry cap), the primary button and every drop-up tile DIM but stay clickable so the tap still
    /// reaches <see cref="ScribeDialogBase.OnClickAdd"/> and surfaces <c>NotifyTabletFull</c> (the same
    /// <c>TriggerIngameError</c> path Enter-insert uses). Nulling <c>onTap</c> would swallow the click and
    /// skip the notice (refine-chalkboard §12.9). Uncapped tiers always pass true.</summary>
    public bool AddTaskEnabled { get; }

    /// <summary>Row style, threaded for the cuneiform-aware label rendering (tablet path) and its bundle.</summary>
    public ScribeRowStyle Style { get; }

    /// <summary>The dialog's current illumination shade. The floating drop-up menu paints in the
    /// <see cref="Overlay"/> layer, which sits OUTSIDE the dialog body's <see cref="ScribeGlobalTint"/> wrap,
    /// so without this it would render at full brightness while the rest of the window is shaded. The menu
    /// re-wraps its content in a <see cref="ScribeGlobalTint"/> built from this value so it matches the
    /// window (snapshotted at open time; the picker rebuilds with a fresh shade on every <c>RebuildBody()</c>,
    /// so a newly-opened menu is current).</summary>
    public ScribeAmbientLightSampler.Shade CurrentShade { get; }

    public override State CreateState() => new ScribeAddKindPickerState();
}

internal sealed class ScribeAddKindPickerState : State<ScribeAddKindPicker>
{
    // Links the on-screen segmented group (the CompositedTransformTarget) to the floating menu follower.
    private readonly LayerLink _link = new();
    private OverlayEntry? _menuEntry;
    private OverlayEntry? _barrierEntry;
    private bool _isOpen;

    // The kind the primary button performs and shows a label for. Defaults to Task (Live[0]) so one click
    // still adds a task; remembers the last-picked kind for the life of this State (see the widget doc).
    private ScribeAddKind _selectedKind = ScribeAddKinds.Live[0];

    // A few px of air between the menu's bottom edge and the button's top edge.
    private const float MenuGap = 4f;

    private void Toggle(BuildContext context)
    {
        if (_isOpen) Close();
        else Open(context);
    }

    private void Open(BuildContext context)
    {
        if (_isOpen) return;

        var overlay = Overlay.Of(context);
        if (overlay == null) return;

        // Match the floating menu's width to the group's on-screen width so it reads as an extension of
        // the button (Dropdown reads Element.RenderObject.Size the same way at open time).
        float width = Element.RenderObject?.Size.X ?? 0f;

        _barrierEntry = new OverlayEntry(BuildBarrier());
        _menuEntry = new OverlayEntry(new CompositedTransformFollower(
            _link,
            // showAbove pins the follower's BOTTOM edge to the target's TOP edge; a negative Y offset lifts
            // it a few px further up for a gap (see CompositedTransformFollower's doc example).
            offset: new Vector2(0, -MenuGap),
            child: BuildMenu(context, width),
            showAbove: true));

        // Barrier first so the menu paints on top of it (later entries paint later); the barrier catches
        // any outside tap and closes.
        overlay.Insert(_barrierEntry);
        overlay.Insert(_menuEntry);
        SetState(() => _isOpen = true);
    }

    private void Close()
    {
        if (!_isOpen) return;
        _menuEntry?.Remove();
        _menuEntry = null;
        _barrierEntry?.Remove();
        _barrierEntry = null;
        SetState(() => _isOpen = false);
    }

    private Widget BuildBarrier() => new Positioned(
        0, 0, 0, 0,
        child: new GestureDetector(
            onTap: _ => Close(),
            child: new Container(new BoxStyle { HitTestBehavior = HitTestBehavior.Opaque })));

    /// <summary>Pick a kind from the drop-up: make it the primary button's kind, close the menu, and add a
    /// block of that kind immediately (spec: picking a kind creates a block of that kind).</summary>
    private void PickKind(ScribeAddKind kind)
    {
        SetState(() => _selectedKind = kind);
        Close();
        Widget.OnAdd(kind);
    }

    /// <summary>Primary button: add the currently-selected kind, closing an open menu first.</summary>
    private void AddSelectedKind()
    {
        if (_isOpen) Close();
        Widget.OnAdd(_selectedKind);
    }

    /// <summary>A label for a button/menu tile: cuneiform strokes on the single tablet cuneiform branch
    /// (add-tablet-cuneiform-chrome), else the normal <see cref="Text"/>. Returned DIRECTLY with no
    /// Center/Align wrapper (the balloon-regression rule the footer labels follow); both label widgets
    /// self-size so the button hugs the label at either scale.</summary>
    private Widget BuildLabel(string label, TextStyle labelStyle)
    {
        if (Widget.Style.UseCuneiform && Widget.Style.CuneiformBundle is { } bundle)
        {
            return new CuneiformText(
                text: label,
                fontSizeEm: labelStyle.FontSize,
                inkColor: labelStyle.Color,
                bundle: bundle,
                glow: default);
        }
        return new Text(label, labelStyle);
    }

    private Widget BuildMenu(BuildContext context, float width)
    {
        var theme = Theme.Of(context);
        var colors = theme.ColorScheme;
        TextStyle labelStyle = new() { FontSize = 14, Color = colors.OnPrimary, FontFamily = ScribeTaskFont.ButtonFamily };

        // Each kind is a full (rounded) Primary button — the drop-up reads as a small stack of the same
        // "add" buttons growing up from the segmented control. On the cuneiform path the label runs taller,
        // so trim the vertical padding to match the footer's readable-vs-cuneiform parity (task 8.4).
        const float cuneiformLabelPadY = 6f;
        ButtonStyle tileButtonStyle = Widget.Style.UseCuneiform
            ? theme.ButtonStyle with { Padding = EdgeInsets.Symmetric(cuneiformLabelPadY, 20) }
            : theme.ButtonStyle;

        var tiles = new List<Widget>();
        foreach (var kind in ScribeAddKinds.Live)
        {
            bool dim = !Widget.AddTaskEnabled;
            TextStyle tileStyle = dim ? labelStyle with { Color = colors.OnPrimary with { W = 0.4f } } : labelStyle;
            tiles.Add(new Button(
                child: BuildLabel(Lang.Get(kind.LabelLangKey), tileStyle),
                style: tileButtonStyle,
                onTap: _ => PickKind(kind)));
        }

        // Transparent panel (user preference): no SurfaceHigh fill behind the kind buttons — the floating
        // menu is just the stack of Primary "add" buttons over the scroll content, with a thin border for
        // grouping. The ScribeDropUpMenu wrapper plays the grow-up entry animation. ScribeGlobalTint
        // re-applies the dialog's illumination shade: the menu lives in the Overlay layer, outside the dialog
        // body's own ScribeGlobalTint wrap, so without this the menu would stay full-brightness while the
        // rest of the window is shaded by light/dark exposure (user-reported).
        var shade = Widget.CurrentShade;
        return new SizedBox(
            width: width,
            child: new ScribeGlobalTint(
                new ScribeDropUpMenu(new Container(
                    style: new BoxStyle
                    {
                        // Faint border (25% of the theme Border's opacity): just enough to group the floating
                        // kind buttons over the transparent panel without a hard outline.
                        BorderColor = colors.Border with { W = colors.Border.W * 0.25f },
                        BorderThickness = 1f,
                        CornerRadius = new Vector4(4f),
                        Padding = EdgeInsets.All(4f),
                    },
                    child: new Column(
                        spacing: 4f,
                        crossAxisAlignment: CrossAxisAlignment.Stretch,
                        mainAxisSize: MainAxisSize.Min,
                        children: tiles))),
                brightness: shade.Brightness,
                tintR: shade.TintR,
                tintG: shade.TintG,
                tintB: shade.TintB));
    }

    public override Widget Build(BuildContext context)
    {
        var theme = Theme.Of(context);
        var colors = theme.ColorScheme;
        TextStyle labelStyle = new() { FontSize = 14, Color = colors.OnPrimary, FontFamily = ScribeTaskFont.ButtonFamily };

        // Cuneiform label parity with the footer (task 8.4): the taller cuneiform label gets trimmed
        // vertical padding; the readable path keeps the default theme padding so Lectern/Notebook are
        // byte-identical to their pre-picker single-button form.
        const float cuneiformLabelPadY = 6f;
        EdgeInsets segPadding = Widget.Style.UseCuneiform
            ? EdgeInsets.Symmetric(cuneiformLabelPadY, 20)
            : theme.ButtonStyle.Padding;
        // Single-side vertical padding (Symmetric stores top==bottom, so Vertical is the sum → halve it),
        // reused for the narrower caret button.
        float padV = Widget.Style.UseCuneiform ? cuneiformLabelPadY : theme.ButtonStyle.Padding.Vertical / 2f;

        // Explicit group height (Dropdown-style, load-bearing). A childless divider Container and the
        // Row's Stretch cross-alignment both size to the group's INCOMING height, and the footer hands the
        // group a large loose height — so without an explicit bound the whole group balloons to fill the body
        // (and IntrinsicHeight can't help: Button wraps its label in a Container/RenderBox that reports ZERO
        // intrinsic height, breaking the measurement chain). Setting Height here makes Container wrap the Row
        // in a SizedBox(height), giving the Row — and thus the Stretch'd divider — a bounded height. The value
        // is the label's content height + the button's vertical padding, matching a normal footer button; the
        // primary label is Center'd inside so any slack is symmetric and the text never top-clips. On the
        // cuneiform path the content height is exactly FontSize×LineHeightRatio (as the footer's icon-parity
        // math uses); on the readable path we use a safe line-height estimate.
        float labelContentHeight = Widget.Style.UseCuneiform
            ? labelStyle.FontSize * CuneiformMetrics.LineHeightRatio
            : labelStyle.FontSize * 1.4f;
        float groupHeight = labelContentHeight + segPadding.Vertical;

        // Square-cornered Primary variant: the OUTER Container rounds the group's outer corners and clips,
        // so the seam between the primary and caret buttons is a straight 1px Border divider — the two read
        // as ONE segmented control, not two separate pills.
        ButtonStyle segmentStyle = theme.ButtonStyle with
        {
            Padding = segPadding,
            Primary = theme.ButtonStyle.Primary with { CornerRadius = 0f },
        };
        ButtonStyle caretStyle = segmentStyle with { Padding = EdgeInsets.Symmetric(padV, 10f) };

        bool primaryDim = !Widget.AddTaskEnabled;
        TextStyle primaryLabelStyle = primaryDim
            ? labelStyle with { Color = colors.OnPrimary with { W = 0.4f } }
            : labelStyle;

        // Center the label inside the button so the explicit group height's slack (if any) sits symmetrically
        // above/below the text rather than pinning it to the top (Padding aligns top-left). Both axes are
        // bounded here — width by Expanded's flex share, height by the group's SizedBox — so Center is safe
        // (the balloon rule only bites on an UNBOUNDED axis).
        var primaryButton = new Button(
            child: new Center(child: BuildLabel(Lang.Get(_selectedKind.LabelLangKey), primaryLabelStyle)),
            style: segmentStyle,
            onTap: _ => AddSelectedKind());

        // Caret: ▲ when closed (the menu will expand upward), ▼ when open (tap to collapse). The Row's
        // Stretch (below) stretches this caret button to the group's height, which can exceed the glyph's own
        // size — so the glyph is centered inside a FULLY-BOUNDED SizedBox (both axes tight) + Center, exactly
        // the footer's IconButtonChild pattern. Both axes bounded is the whole trick: the balloon regression
        // only fires when a Center/greedy widget sees an UNBOUNDED axis (the earlier Column(MainAxisSize.Max)
        // did, and inflated the whole group to the footer's full height).
        //
        // Transform.Translate nudges the glyph 5px DOWN within the button; it's paint-only (a translation
        // matrix), so it shifts the drawn caret without changing the button's layout/hit box.
        const float caretGlyphSize = 14f;
        const float caretNudgeDown = 3f;
        var caretButton = new Button(
            child: Transform.Translate(
                new SizedBox(
                    width: caretGlyphSize,
                    height: caretGlyphSize,
                    child: new Center(child: new ScribeVsIconGlyph(
                        _isOpen ? "scribetriangledown" : "scribetriangleup", caretGlyphSize, colors.OnPrimary))),
                new Vector2(0f, caretNudgeDown)),
            style: caretStyle,
            onTap: _ => Toggle(context));

        // Explicit Height (see groupHeight above) is the load-bearing bound: Container applies it by wrapping
        // the Row in a SizedBox(height), so the Row's incoming MaxHeight is groupHeight rather than the
        // footer's large loose height. That makes the Stretch cross-alignment fill the 1px divider and the
        // caret to exactly groupHeight — not the whole body (the balloon) and not zero (the IntrinsicHeight
        // vanish, which failed because a Row whose main child is Expanded reports ZERO intrinsic height).
        var group = new Container(
            style: new BoxStyle
            {
                Height = groupHeight,
                CornerRadius = new Vector4(4f),
                ClipBehavior = ClipBehavior.AntiAlias,
            },
            child: new Row(
                spacing: 0,
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: new Widget[]
                {
                    new Expanded(child: primaryButton),
                    // Thin divider seam; fills the group height via the Row's Stretch cross-alignment.
                    new Container(style: new BoxStyle { Color = colors.Border, Width = 1f }),
                    caretButton,
                }));

        return new CompositedTransformTarget(_link, group);
    }

    public override void Dispose()
    {
        Close();
        base.Dispose();
    }
}

/// <summary>
/// Plays a scale-and-fade entry animation when the add-kind drop-up first mounts, growing UPWARD from the
/// button's top edge. Identical to LibGUI's private <c>DropdownMenu</c> except the scale anchor is
/// <see cref="Alignment.BottomCenter"/> (grow up) instead of TopCenter (grow down).
/// </summary>
internal sealed class ScribeDropUpMenu : StatefulWidget
{
    public ScribeDropUpMenu(Widget child)
    {
        Child = child;
    }

    public Widget Child { get; }

    public override State CreateState() => new ScribeDropUpMenuState();
}

internal sealed class ScribeDropUpMenuState : State<ScribeDropUpMenu>
{
    private AnimationController _anim = null!;

    public override void InitState()
    {
        base.InitState();
        _anim = new AnimationController(
            TimeSpan.FromMilliseconds(140),
            Element.Owner!.GetTickerProvider());
        _anim.OnValueChanged += OnTick;
        _anim.Forward(0.0);
    }

    public override void Dispose()
    {
        _anim.OnValueChanged -= OnTick;
        _anim.Dispose();
        base.Dispose();
    }

    private void OnTick(double value) => SetState(() => { });

    public override Widget Build(BuildContext context)
    {
        var t = (float)Curves.EaseOut.Transform(_anim.Value);
        return new Opacity(
            t,
            Transform.ScaleXy(
                Widget.Child,
                1f,
                0.85f + 0.15f * t,
                Alignment.BottomCenter));
    }
}
