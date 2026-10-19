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
/// The editor footer's "add" control: a segmented button. The primary button adds the currently-selected
/// kind (defaults to Task); the caret button opens a floating drop-up menu of the available kinds
/// (<see cref="ScribeAddKinds.Live"/>). Picking a kind makes it the primary kind and adds a block of it.
///
/// <para>The menu is a floating overlay growing upward over the scroll body, so nothing reflows and it is
/// not clipped by the dialog's bottom edge. Mirrors LibGUI's <c>Dropdown</c>: a <see cref="LayerLink"/> ties
/// the button (<see cref="CompositedTransformTarget"/>) to a follower in the <see cref="Overlay"/> with
/// <c>showAbove: true</c>; a full-screen barrier entry closes the menu on any outside tap.</para>
///
/// <para>State lives on <see cref="ScribeAddKindPickerState"/> at a stable footer position, so it survives
/// RebuildBody; the remembered kind resets to Task on the next dialog open. Dispose tears down any open
/// overlay entries.</para>
/// </summary>
internal sealed class ScribeAddKindPicker : StatefulWidget
{
    public ScribeAddKindPicker(
        Action<ScribeAddKind> onAdd,
        bool addTaskEnabled,
        ScribeRowStyle style,
        ScribeAmbientLightSampler.Shade currentShade,
        IReadOnlyList<ScribeQuestCatalogEntry>? questCatalog = null,
        Action<ScribeQuestCatalogEntry>? onAddQuestLink = null,
        Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        OnAdd = onAdd;
        AddTaskEnabled = addTaskEnabled;
        Style = style;
        CurrentShade = currentShade;
        QuestCatalog = questCatalog ?? Array.Empty<ScribeQuestCatalogEntry>();
        OnAddQuestLink = onAddQuestLink;
    }

    /// <summary>Add a block of the given kind (wired to <see cref="ScribeDialogBase.OnClickAdd"/>).</summary>
    public Action<ScribeAddKind> OnAdd { get; }

    /// <summary>The installed quest mod's catalog. Empty hides the "Quest Link" tile.</summary>
    public IReadOnlyList<ScribeQuestCatalogEntry> QuestCatalog { get; }

    /// <summary>Adds a quest Link for the chosen entry (wired to
    /// <see cref="ScribeDialogBase.OnClickAddQuestLink"/>). Null exactly when <see cref="QuestCatalog"/> is empty.</summary>
    public Action<ScribeQuestCatalogEntry>? OnAddQuestLink { get; }

    /// <summary>Whether a block may currently be added. When false (tablet/chalkboard at its entry cap) the
    /// primary button and tiles DIM but stay clickable, so the tap still reaches
    /// <see cref="ScribeDialogBase.OnClickAdd"/> and surfaces <c>NotifyTabletFull</c>; nulling <c>onTap</c>
    /// would skip the notice.</summary>
    public bool AddTaskEnabled { get; }

    public ScribeRowStyle Style { get; }

    /// <summary>The dialog's illumination shade. The overlay layer sits outside the dialog body's
    /// <see cref="ScribeGlobalTint"/>, so the menu re-wraps its content with this value (snapshotted at open).</summary>
    public ScribeAmbientLightSampler.Shade CurrentShade { get; }

    public override State CreateState() => new ScribeAddKindPickerState();
}

internal sealed class ScribeAddKindPickerState : State<ScribeAddKindPicker>
{
    // Links the segmented group (target) to the floating menu follower.
    private readonly LayerLink _link = new();
    private OverlayEntry? _menuEntry;
    private OverlayEntry? _barrierEntry;
    private bool _isOpen;

    // The kind the primary button adds; defaults to Task (Live[0]) and remembers the last pick.
    private ScribeAddKind _selectedKind = ScribeAddKinds.Live[0];

    // True while the same floating menu shows the quest list instead of the kind tiles; reset on Close().
    private bool _questListOpen;

    private const float MenuGap = 4f;

    // Captured at Open() so RebuildMenuContent can rebuild the same follower.
    private BuildContext? _menuContext;
    private float _menuWidth;

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

        // Match the menu width to the group's on-screen width.
        float width = Element.RenderObject?.Size.X ?? 0f;
        _menuContext = context;
        _menuWidth = width;

        _barrierEntry = new OverlayEntry(BuildBarrier());
        _menuEntry = new OverlayEntry(BuildFollower(context, width));

        // Barrier first so the menu paints on top of it.
        overlay.Insert(_barrierEntry);
        overlay.Insert(_menuEntry);
        SetState(() => _isOpen = true);
    }

    private Widget BuildFollower(BuildContext context, float width) => new CompositedTransformFollower(
        _link,
        // showAbove pins the follower's bottom edge to the target's top edge; the negative Y adds a gap.
        offset: new Vector2(0, -MenuGap),
        child: BuildMenu(context, width),
        showAbove: true);

    /// <summary>Swaps the open menu's content in place (kind tiles and quest list). SetState on this State
    /// does not touch the separately-inserted overlay tree, so the entry is rebuilt directly.</summary>
    private void RebuildMenuContent()
    {
        if (_menuEntry is null || _menuContext is null) return;
        _menuEntry.Widget = BuildFollower(_menuContext.Value, _menuWidth);
        _menuEntry.MarkNeedsBuild();
    }

    private void Close()
    {
        if (!_isOpen) return;
        _menuEntry?.Remove();
        _menuEntry = null;
        _barrierEntry?.Remove();
        _barrierEntry = null;
        _menuContext = null;
        SetState(() => { _isOpen = false; _questListOpen = false; });
    }

    private Widget BuildBarrier() => new Positioned(
        0, 0, 0, 0,
        child: new GestureDetector(
            onTap: _ => Close(),
            child: new Container(new BoxStyle { HitTestBehavior = HitTestBehavior.Opaque })));

    /// <summary>Makes <paramref name="kind"/> the primary kind, closes the menu, and adds a block of it.</summary>
    private void PickKind(ScribeAddKind kind)
    {
        SetState(() => _selectedKind = kind);
        Close();
        Widget.OnAdd(kind);
    }

    /// <summary>Primary button: adds the selected kind, closing an open menu first.</summary>
    private void AddSelectedKind()
    {
        if (_isOpen) Close();
        Widget.OnAdd(_selectedKind);
    }

    /// <summary>The "Quest Link" tile: swaps the open menu to the quest list (picking a quest is a second step).</summary>
    private void OpenQuestList()
    {
        SetState(() => _questListOpen = true);
        RebuildMenuContent();
    }

    /// <summary>Closes the menu and adds a quest Link. Leaves <see cref="_selectedKind"/> unchanged: a quest
    /// Link can't be a repeatable primary-button kind.</summary>
    private void PickQuest(ScribeQuestCatalogEntry entry)
    {
        Close();
        Widget.OnAddQuestLink?.Invoke(entry);
    }

    /// <summary>A label: cuneiform strokes on the tablet cuneiform branch, else <see cref="Text"/>. Returned
    /// without a Center/Align wrapper (it would balloon the button); both self-size.</summary>
    private Widget BuildLabel(string label, TextStyle labelStyle)
    {
        if (Widget.Style.UseCuneiform && Widget.Style.CuneiformBundle is { } bundle)
        {
            return new CuneiformText(
                text: label,
                fontSizeEm: labelStyle.FontSize,
                inkColor: labelStyle.Color,
                bundle: bundle,
                glow: default,
                glyphDrawScale: CuneiformMetrics.GlyphDrawScale);
        }
        return new Text(label, labelStyle);
    }

    /// <summary>One tile per <see cref="ScribeAddKinds.Live"/> kind, plus a "Quest Link" tile only when the
    /// quest catalog is non-empty.</summary>
    private List<Widget> BuildKindTiles(TextStyle labelStyle, ButtonStyle tileButtonStyle, ColorScheme colors)
    {
        bool dim = !Widget.AddTaskEnabled;
        TextStyle tileStyle = dim ? labelStyle with { Color = colors.OnPrimary with { W = 0.4f } } : labelStyle;

        var tiles = new List<Widget>();
        foreach (var kind in ScribeAddKinds.Live)
        {
            tiles.Add(new Button(
                child: BuildLabel(Lang.Get(kind.LabelLangKey), tileStyle),
                style: tileButtonStyle,
                onTap: _ => PickKind(kind)));
        }
        if (Widget.QuestCatalog.Count > 0)
        {
            tiles.Add(new Button(
                child: BuildLabel(Lang.Get("scribe:scribe-gui-addquestlink"), tileStyle),
                style: tileButtonStyle,
                onTap: _ => OpenQuestList()));
        }
        return tiles;
    }

    /// <summary>One tile per catalog entry title. No search/scroll; a very large catalog would overflow.</summary>
    private List<Widget> BuildQuestTiles(TextStyle labelStyle, ButtonStyle tileButtonStyle)
    {
        var tiles = new List<Widget>();
        foreach (var entry in Widget.QuestCatalog)
        {
            tiles.Add(new Button(
                child: BuildLabel(entry.Title, labelStyle),
                style: tileButtonStyle,
                onTap: _ => PickQuest(entry)));
        }
        return tiles;
    }

    private Widget BuildMenu(BuildContext context, float width)
    {
        var theme = Theme.Of(context);
        var colors = theme.ColorScheme;
        TextStyle labelStyle = new() { FontSize = 14, Color = colors.OnPrimary, FontFamily = ScribeTaskFont.ButtonFamily };

        // Each kind is a full Primary button; the cuneiform label runs taller, so its vertical padding is trimmed.
        const float cuneiformLabelPadY = 6f;
        ButtonStyle tileButtonStyle = Widget.Style.UseCuneiform
            ? theme.ButtonStyle with { Padding = EdgeInsets.Symmetric(cuneiformLabelPadY, 20) }
            : theme.ButtonStyle;

        var tiles = _questListOpen
            ? BuildQuestTiles(labelStyle, tileButtonStyle)
            : BuildKindTiles(labelStyle, tileButtonStyle, colors);

        // Opaque SurfaceHigh panel so the stacked buttons read as a surface. ScribeDropUpMenu plays the
        // grow-up animation; ScribeGlobalTint re-applies the shade because the Overlay layer sits outside the
        // dialog body's tint wrap.
        var shade = Widget.CurrentShade;
        return new SizedBox(
            width: width,
            child: new ScribeGlobalTint(
                new ScribeDropUpMenu(new Container(
                    style: new BoxStyle
                    {
                        Color = colors.SurfaceHigh with { W = 1f },
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

        // Cuneiform labels are taller, so they get trimmed vertical padding.
        const float cuneiformLabelPadY = 6f;
        EdgeInsets segPadding = Widget.Style.UseCuneiform
            ? EdgeInsets.Symmetric(cuneiformLabelPadY, 20)
            : theme.ButtonStyle.Padding;
        // Single-side vertical padding (Vertical is the sum), reused for the caret button.
        float padV = Widget.Style.UseCuneiform ? cuneiformLabelPadY : theme.ButtonStyle.Padding.Vertical / 2f;

        // Explicit group height is load-bearing: the divider Container and the Row's Stretch size to the
        // INCOMING height, which the footer makes large, so the group would balloon. IntrinsicHeight can't
        // help (Button's label wrapper reports zero intrinsic height). Value = label content height +
        // vertical padding; the cuneiform content height is FontSize x LineHeightRatio, the readable path
        // uses a safe 1.4 estimate.
        float labelContentHeight = Widget.Style.UseCuneiform
            ? labelStyle.FontSize * CuneiformMetrics.LineHeightRatio
            : labelStyle.FontSize * 1.4f;
        float groupHeight = labelContentHeight + segPadding.Vertical;

        // Square-cornered Primary: the outer Container rounds and clips, so the seam is a straight 1px divider.
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

        // Center the label so slack from the explicit height is symmetric. Both axes are bounded, so Center is safe.
        var primaryButton = new Button(
            child: new Center(child: BuildLabel(Lang.Get(_selectedKind.LabelLangKey), primaryLabelStyle)),
            style: segmentStyle,
            onTap: _ => AddSelectedKind());

        // Caret: up when closed, down when open. The Row's Stretch can make the button taller than the glyph,
        // so the glyph is centered in a fully-bounded SizedBox (an unbounded axis would balloon to the footer's
        // height). Transform.Translate nudges it 3px down, paint-only (no layout/hit-box change).
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

        // The explicit Height makes Container wrap the Row in a SizedBox, so the Row's MaxHeight is
        // groupHeight and Stretch sizes the divider and caret to it (IntrinsicHeight would collapse to zero).
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
                    // Thin divider seam.
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
/// Scale-and-fade entry animation for the drop-up, anchored at <see cref="Alignment.BottomCenter"/> so it
/// grows upward. Otherwise identical to LibGUI's private <c>DropdownMenu</c> (anchored TopCenter).
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
