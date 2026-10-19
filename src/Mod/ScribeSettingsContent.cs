using System;
using System.Collections.Generic;
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle, FontWeight
using Gui.Widgets.Basic;         // Text, Divider, Button, ButtonVariant
using Gui.Widgets.Framework;     // Widget, StatelessWidget, BuildContext, Theme, ValueKey
using Gui.Widgets.Gestures;      // ScrollController
using Gui.Widgets.Input;         // Dropdown, DropdownItem, NumericField, Checkbox
using Gui.Widgets.Layout;        // Column, Row, Expanded, Padding, SizedBox, CrossAxisAlignment
using Gui.Widgets.Overlay;       // Tooltip
using Gui.Widgets.Painting;      // BoxStyle
using Gui.Widgets.Scroll;        // SingleChildScrollView, Scrollbar
using Gui.Core.Layout;           // MainAxisSize
using OpenTK.Mathematics;        // Vector4
using Scribe.Core;
using Vintagestory.API.Config;   // Lang

namespace Scribe;

/// <summary>
/// The host-agnostic Scribe settings form: every per-player preference, grouped into Mod Behavior, Window
/// Appearance, and HUD Appearance, written through instantly (no OK/Cancel). It assumes no window size
/// (scroll view + scrollbar), so it is hosted in the Lectern's central region and the standalone HUD-gear dialog.
///
/// <para>Stateless: it reads a <see cref="ScribePlayerSettings"/> snapshot and forwards each edit through
/// <c>onMutate</c> (the host's <c>ScribeModSystem.UpdateMySettings</c>), which normalizes/persists and fires
/// the change event the host rebuilds on, so the form re-renders from the clamped value. The scroll
/// controller is owned by the HOST so a rebuild doesn't reset the scroll position.</para>
/// </summary>
internal sealed class ScribeSettingsContent : StatelessWidget
{
    private readonly ScribePlayerSettings settings;
    private readonly Action<Action<ScribePlayerSettings>> onMutate;
    private readonly ScrollController scrollController;
    private readonly ScribeNumericFocusRegistry focus;
    private readonly bool showQuestSettings;
    private readonly Action onOpenThemePicker;

    public ScribeSettingsContent(
        ScribePlayerSettings settings,
        Action<Action<ScribePlayerSettings>> onMutate,
        ScrollController scrollController,
        ScribeNumericFocusRegistry focus,
        Action onOpenThemePicker,
        bool showQuestSettings = false)
    {
        this.settings = settings;
        this.onMutate = onMutate;
        this.scrollController = scrollController;
        this.focus = focus;
        this.onOpenThemePicker = onOpenThemePicker;
        this.showQuestSettings = showQuestSettings;
    }

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;

        // Settings chrome stays at 100% of BaseSettingsFontSize; Window Text Size does not affect this form.
        const float scale = 1f;

        var body = new Column(
            spacing: 14 * scale,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                SectionTitle(Lang.Get("scribe:settings-section-modbehavior"), colors, scale),
                BuildModBehaviorSection(colors, scale),
                new Divider(),
                SectionTitle(Lang.Get("scribe:settings-section-windowappearance"), colors, scale),
                BuildWindowAppearanceSection(colors, scale),
                new Divider(),
                SectionTitle(Lang.Get("scribe:settings-section-hudappearance"), colors, scale),
                BuildHudAppearanceSection(colors, scale),
            });

        // LibGUI default face at unscaled settings size, not the Task Text Font.
        return ScribeTextDefaults.WrapSettingsChrome(
            new Padding(
                EdgeInsets.All(10),
                child: new Scrollbar(
                    controller: scrollController,
                    child: new SingleChildScrollView(
                        controller: scrollController,
                        child: body))
                { AutoHide = false }));
    }

    // ---------------- Sections ----------------

    /// <summary>Mod Behavior: document/task policy, mute, and Clockmaker timer prefs (always shown —
    /// survival lets anyone take the trait).</summary>
    private Widget BuildModBehaviorSection(ColorScheme colors, float scale)
    {
        var children = new List<Widget>
            {
                LabeledControl(
                    "settings-completionpolicy", colors, scale,
                    new Dropdown<ScribeCompletionPolicy>(
                        value: settings.CompletionPolicy,
                        items: new List<DropdownItem<ScribeCompletionPolicy>>
                        {
                            new() { Value = ScribeCompletionPolicy.Keep,      Label = Lang.Get("scribe:scribe-completion-keep") },
                            new() { Value = ScribeCompletionPolicy.Sink,      Label = Lang.Get("scribe:scribe-completion-sink") },
                            new() { Value = ScribeCompletionPolicy.Unpin,     Label = Lang.Get("scribe:scribe-completion-unpin") },
                            new() { Value = ScribeCompletionPolicy.UnpinSink, Label = Lang.Get("scribe:scribe-completion-unpinsink") },
                            new() { Value = ScribeCompletionPolicy.Delete,    Label = Lang.Get("scribe:scribe-completion-delete") },
                        },
                        onChanged: v => onMutate(s => s.CompletionPolicy = v))),

                PairedControls(colors, scale,
                    LabeledControl(
                        "settings-trackercompletion", colors, scale,
                        new Dropdown<ScribeTrackerCompletion>(
                            value: settings.TrackerCompletion,
                            items: new List<DropdownItem<ScribeTrackerCompletion>>
                            {
                                new() { Value = ScribeTrackerCompletion.Complete, Label = Lang.Get("scribe:scribe-trackercompletion-complete") },
                                new() { Value = ScribeTrackerCompletion.Delete,   Label = Lang.Get("scribe:scribe-trackercompletion-delete") },
                                new() { Value = ScribeTrackerCompletion.Nothing,  Label = Lang.Get("scribe:scribe-trackercompletion-nothing") },
                            },
                            onChanged: v => onMutate(s => s.TrackerCompletion = v))),
                    LabeledControl(
                        "settings-subtaskbehavior", colors, scale,
                        new Dropdown<ScribeSubtaskBehavior>(
                            value: settings.SubtaskBehavior,
                            items: new List<DropdownItem<ScribeSubtaskBehavior>>
                            {
                                new() { Value = ScribeSubtaskBehavior.Bound,           Label = Lang.Get("scribe:scribe-subtaskbehavior-bound") },
                                new() { Value = ScribeSubtaskBehavior.Independent,     Label = Lang.Get("scribe:scribe-subtaskbehavior-independent") },
                                new() { Value = ScribeSubtaskBehavior.DiscardChildren, Label = Lang.Get("scribe:scribe-subtaskbehavior-discard") },
                            },
                            onChanged: v => onMutate(s => s.SubtaskBehavior = v)))),

                PairedControls(colors, scale,
                    LabeledControl(
                        "settings-newtaskinsert", colors, scale,
                        new Dropdown<ScribeNewTaskInsert>(
                            value: settings.NewTaskInsert,
                            items: new List<DropdownItem<ScribeNewTaskInsert>>
                            {
                                new() { Value = ScribeNewTaskInsert.Top,    Label = Lang.Get("scribe:scribe-newtaskinsert-top") },
                                new() { Value = ScribeNewTaskInsert.Bottom, Label = Lang.Get("scribe:scribe-newtaskinsert-bottom") },
                            },
                            onChanged: v => onMutate(s => s.NewTaskInsert = v))),
                    LabeledControl(
                        "settings-pininsert", colors, scale,
                        new Dropdown<ScribePinInsert>(
                            value: settings.PinInsert,
                            items: new List<DropdownItem<ScribePinInsert>>
                            {
                                new() { Value = ScribePinInsert.Top,    Label = Lang.Get("scribe:scribe-pininsert-top") },
                                new() { Value = ScribePinInsert.Bottom, Label = Lang.Get("scribe:scribe-pininsert-bottom") },
                            },
                            onChanged: v => onMutate(s => s.PinInsert = v)))),

                HuggingCheckbox(
                    "settings-muteuisounds", colors, scale,
                    value: settings.MuteUiSounds,
                    onChanged: v => onMutate(s => s.MuteUiSounds = v)),

                PairedControls(colors, scale,
                    HuggingCheckbox(
                        "settings-timerdisappear", colors, scale,
                        value: settings.TimerAutoDisappear,
                        onChanged: v => onMutate(s => s.TimerAutoDisappear = v)),
                    LabeledControl(
                        "settings-timeralarmvolume", colors, scale,
                        IntField("timeralarmvolume", settings.TimerAlarmVolume, step: 5,
                            onChanged: v => onMutate(s => s.TimerAlarmVolume = v),
                            clamp: ScribePlayerSettings.ClampTimerAlarmVolume))),
        };

        // Quest policies are shown only when vsquest is installed (inert otherwise).
        if (showQuestSettings)
        {
            children.Add(PairedControls(colors, scale,
                LabeledControl(
                    "settings-questacceptpolicy", colors, scale,
                    new Dropdown<ScribeQuestAcceptPolicy>(
                        value: settings.QuestAcceptPolicy,
                        items: new List<DropdownItem<ScribeQuestAcceptPolicy>>
                        {
                            new() { Value = ScribeQuestAcceptPolicy.Always,      Label = Lang.Get("scribe:scribe-questpolicy-always") },
                            new() { Value = ScribeQuestAcceptPolicy.Never,       Label = Lang.Get("scribe:scribe-questpolicy-never") },
                            new() { Value = ScribeQuestAcceptPolicy.PromptHud,   Label = Lang.Get("scribe:scribe-questpolicy-prompthud") },
                            new() { Value = ScribeQuestAcceptPolicy.PromptPopup, Label = Lang.Get("scribe:scribe-questpolicy-promptpopup") },
                        },
                        onChanged: v => onMutate(s => s.QuestAcceptPolicy = v))),
                LabeledControl(
                    "settings-questcompletionpolicy", colors, scale,
                    new Dropdown<ScribeQuestCompletionPolicy>(
                        value: settings.QuestCompletionPolicy,
                        items: new List<DropdownItem<ScribeQuestCompletionPolicy>>
                        {
                            new() { Value = ScribeQuestCompletionPolicy.Always, Label = Lang.Get("scribe:scribe-questpolicy-always") },
                            new() { Value = ScribeQuestCompletionPolicy.Never,  Label = Lang.Get("scribe:scribe-questpolicy-never") },
                            new() { Value = ScribeQuestCompletionPolicy.Prompt, Label = Lang.Get("scribe:scribe-questpolicy-prompt") },
                        },
                        onChanged: v => onMutate(s => s.QuestCompletionPolicy = v)))));

            // Whether accepting a quest also pins the resulting linked task.
            children.Add(HuggingCheckbox(
                "settings-autopinonquestaccept", colors, scale,
                value: settings.AutoPinOnQuestAccept,
                onChanged: v => onMutate(s => s.AutoPinOnQuestAccept = v)));
        }

        return new Column(
            spacing: 12 * scale,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: children);
    }

    /// <summary>Window Appearance: Lectern look (pixel-art theme, Pixel Art Size, window/task fonts)
    /// plus tablet cuneiform toggles.</summary>
    private Widget BuildWindowAppearanceSection(ColorScheme colors, float scale)
    {
        return new Column(
            spacing: 12 * scale,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                // Task/note ROW text font only (the empty-string default keeps the built-in body font).
                // Paired with the theme-picker button because a full-width button reads as oversized.
                PairedControls(colors, scale,
                    LabeledControl(
                        "settings-taskfont", colors, scale,
                        new Dropdown<string>(
                            value: ScribePlayerSettings.NormalizeTaskFontFamily(settings.TaskFontFamily),
                            items: TaskFontItems(),
                            onChanged: v => onMutate(s => s.TaskFontFamily = v))),
                    LabeledControl(
                        "settings-themepicker", colors, scale,
                        new Button(
                            child: new Text(
                                Lang.Get("scribe:settings-themepicker-button"),
                                new TextStyle { FontSize = ScribeRowConstants.BaseSettingsFontSize * scale, Color = colors.OnPrimary }),
                            variant: ButtonVariant.Primary,
                            onTap: _ => onOpenThemePicker()))),

                // Pixel Art Size is the Lectern's single driving width (10px grid, snapped on blur); the
                // window text scale is a percent snapping to a 5% notch on blur.
                PairedControls(colors, scale,
                    LabeledControl(
                        "settings-pixelartsize", colors, scale,
                        IntField("pixelartsize", settings.PixelArtSize, step: 10,
                            onChanged: v => onMutate(s => s.PixelArtSize = v),
                            clamp: ScribePlayerSettings.ClampPixelArtSize)),
                    LabeledControl(
                        "settings-windowfontscale", colors, scale,
                        FontScaleField("windowfontscale", settings.WindowFontScale, v => onMutate(s => s.WindowFontScale = v)))),

                // Cuneiform press-in only applies while Cuneiform tablets is on, so it sits alone below.
                PairedControls(colors, scale,
                    HuggingCheckbox(
                        "settings-pixelartdisplay", colors, scale,
                        value: settings.PixelArtDisplay,
                        onChanged: v => onMutate(s => s.PixelArtDisplay = v)),
                    HuggingCheckbox(
                        "settings-cuneiformtablets", colors, scale,
                        value: settings.CuneiformTablets,
                        onChanged: v => onMutate(s => s.CuneiformTablets = v))),

                HuggingCheckbox(
                    "settings-cuneiformprogression", colors, scale,
                    value: settings.CuneiformProgression,
                    onChanged: v => onMutate(s => s.CuneiformProgression = v)),
            });
    }

    /// <summary>Font-selector options: the empty-string "Default" plus each family from
    /// <see cref="ScribePlayerSettings.KnownTaskFonts"/>. Must stay in sync with
    /// <c>ScribeModSystem.RegisterCustomFonts</c>.</summary>
    private static List<DropdownItem<string>> TaskFontItems()
    {
        var items = new List<DropdownItem<string>>
        {
            new() { Value = ScribePlayerSettings.DefaultTaskFontFamily, Label = Lang.Get("scribe:settings-taskfont-default") },
        };
        foreach (var family in ScribePlayerSettings.KnownTaskFonts)
        {
            items.Add(new DropdownItem<string> { Value = family, Label = family });
        }
        return items;
    }

    /// <summary>HUD Appearance: collapse, storm, icons, gear, then layout (anchor, row cap, width,
    /// offsets, text scale).</summary>
    private Widget BuildHudAppearanceSection(ColorScheme colors, float scale)
    {
        return new Column(
            spacing: 12 * scale,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                PairedControls(colors, scale,
                    HuggingCheckbox(
                        "settings-hudcollapsed", colors, scale,
                        value: settings.HudCollapsed,
                        onChanged: v => onMutate(s => s.HudCollapsed = v)),
                    HuggingCheckbox(
                        "settings-stormcorruption", colors, scale,
                        value: settings.StormCorruption,
                        onChanged: v => onMutate(s => s.StormCorruption = v))),

                PairedControls(colors, scale,
                    HuggingCheckbox(
                        "settings-hudshowicons", colors, scale,
                        value: settings.HudShowIcons,
                        onChanged: v => onMutate(s => s.HudShowIcons = v)),
                    HuggingCheckbox(
                        "settings-hudshowsettingsgear", colors, scale,
                        value: settings.HudShowSettingsGear,
                        onChanged: v => onMutate(s => s.HudShowSettingsGear = v))),

                LabeledControl(
                    "settings-hudanchor", colors, scale,
                    new Dropdown<ScribeHudAnchor>(
                        value: settings.HudAnchor,
                        items: AnchorItems(),
                        onChanged: v => onMutate(s => s.HudAnchor = v))),

                // Numeric fields, not sliders (sliders hijack scroll); each clamps ON BLUR via a Core Clamp* static.
                PairedControls(colors, scale,
                    LabeledControl(
                        "settings-hudmaxrows", colors, scale,
                        IntField("hudmaxrows", settings.HudMaxRows, step: 1,
                            onChanged: v => onMutate(s => s.HudMaxRows = v),
                            clamp: ScribePlayerSettings.ClampHudMaxRows)),
                    LabeledControl(
                        "settings-hudrowwidth", colors, scale,
                        IntField("hudrowwidth", settings.HudRowWidth, step: 5,
                            onChanged: v => onMutate(s => s.HudRowWidth = v),
                            clamp: ScribePlayerSettings.ClampHudRowWidth))),

                PairedControls(colors, scale,
                    LabeledControl(
                        "settings-hudoffset", colors, scale,
                        new Row(
                            spacing: 12 * scale,
                            mainAxisSize: MainAxisSize.Max,
                            children: new Widget[]
                            {
                                new Expanded(child: OffsetField(
                                    "hudoffsetx", Lang.Get("scribe:settings-hudoffsetx"), settings.HudOffsetX,
                                    v => onMutate(s => s.HudOffsetX = v), colors, scale)),
                                new Expanded(child: OffsetField(
                                    "hudoffsety", Lang.Get("scribe:settings-hudoffsety"), settings.HudOffsetY,
                                    v => onMutate(s => s.HudOffsetY = v), colors, scale)),
                            })),
                    LabeledControl(
                        "settings-hudfontscale", colors, scale,
                        FontScaleField("hudfontscale", settings.HudFontScale, v => onMutate(s => s.HudFontScale = v)))),
            });
    }

    // ---------------- Control builders ----------------

    private static List<DropdownItem<ScribeHudAnchor>> AnchorItems() => new()
    {
        new() { Value = ScribeHudAnchor.TopLeft,     Label = Lang.Get("scribe:settings-anchor-topleft") },
        new() { Value = ScribeHudAnchor.TopMiddle,   Label = Lang.Get("scribe:settings-anchor-topmiddle") },
        new() { Value = ScribeHudAnchor.TopRight,    Label = Lang.Get("scribe:settings-anchor-topright") },
        new() { Value = ScribeHudAnchor.MiddleLeft,  Label = Lang.Get("scribe:settings-anchor-middleleft") },
        new() { Value = ScribeHudAnchor.MiddleRight, Label = Lang.Get("scribe:settings-anchor-middleright") },
        new() { Value = ScribeHudAnchor.BottomLeft,  Label = Lang.Get("scribe:settings-anchor-bottomleft") },
        new() { Value = ScribeHudAnchor.BottomRight, Label = Lang.Get("scribe:settings-anchor-bottomright") },
    };

    /// <summary>Numeric-field builder shared by all field types. The field is uncontrolled (seeds from
    /// <c>initialValue</c> in <c>InitState</c> only), so the <see cref="ValueKey"/>-keyed wrapper remounts it
    /// onto the persisted/clamped value after a write. Focus survives the remount via the host-owned
    /// <see cref="ScribeNumericFocusRegistry"/> node for <paramref name="id"/>, armed (via <c>onStepped</c>)
    /// just before a step writes through.</summary>
    private Widget NumericField(string id, int keyValue, float initialValue, float step, Action<float> onChanged,
        Func<float, float>? clamp = null) =>
        // Key-only SizedBox (no width/height, so the child sizes itself).
        new SizedBox(
            key: new ValueKey<int>(keyValue),
            child: new ScribeNumericField(
                initialValue: initialValue,
                step: step,
                onChanged: onChanged,
                style: new BoxStyle { Height = 34, Width = 120 },
                focusNode: focus.NodeFor(id),
                autoFocus: focus.ShouldFocus(id),
                onStepped: () => focus.ArmAutoFocus(id),
                clamp: clamp));

    /// <summary>An integer numeric field that writes its rounded value through on change and clamps on blur.</summary>
    private Widget IntField(string id, int value, float step, Action<int> onChanged,
        Func<int, int>? clamp = null) =>
        NumericField(id, keyValue: value, initialValue: value, step: step,
            onChanged: v => onChanged((int)MathF.Round(v)),
            clamp: clamp is null ? null : v => clamp((int)MathF.Round(v)));

    /// <summary>A small caption over an <see cref="IntField"/> (step 5), clamped to the Core offset range.</summary>
    private Widget OffsetField(string id, string caption, int value, Action<int> onChanged, ColorScheme colors, float scale)
    {
        return new Column(
            spacing: 4 * scale,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                new Text(caption, Pegged(13 * scale, colors.OnSurfaceVariant)),
                IntField(id, value, step: 5, onChanged: onChanged,
                    clamp: ScribePlayerSettings.ClampHudOffset),
            });
    }

    /// <summary>A font-scale field entered as a PERCENT: the stored multiplier (0.80–1.20) is shown as 80–120,
    /// stepped by 5, and keyed by the current percent so it settles onto the snapped result.</summary>
    private Widget FontScaleField(string id, float scaleValue, Action<float> onChanged)
    {
        int pct = (int)MathF.Round(scaleValue * 100f);
        return NumericField(id, keyValue: pct, initialValue: pct, step: 5,
            onChanged: v => onChanged(MathF.Round(v) / 100f),
            // Clamp in percent space by round-tripping through the Core multiplier clamp (which also snaps to the 5% notch).
            clamp: v => MathF.Round(ScribePlayerSettings.ClampFontScale(v / 100f) * 100f));
    }

    private static TextStyle Pegged(float nominalSize, Vector4 color, FontWeight weight = FontWeight.Normal, bool softWrap = false)
    {
        var style = new TextStyle
        {
            FontFamily = ScribeTaskFont.DefaultFamily,
            FontSize = nominalSize,
            Color = color,
            Weight = weight,
        };
        if (softWrap) style = style with { SoftWrap = true };
        return style;
    }

    // ---------------- Layout helpers ----------------

    private Widget SectionTitle(string text, ColorScheme colors, float scale) =>
        new Text(text, Pegged(ScribeRowConstants.BaseSettingsFontSize * scale * 1.08f, colors.OnSurface, FontWeight.Bold));

    /// <summary>Two controls side by side as equal-width columns.</summary>
    private static Widget PairedControls(ColorScheme colors, float scale, Widget left, Widget right) =>
        new Row(
            spacing: 12 * scale,
            mainAxisSize: MainAxisSize.Max,
            crossAxisAlignment: CrossAxisAlignment.Start,
            children: new Widget[] { new Expanded(child: left), new Expanded(child: right) });

    /// <summary>A checkbox that hugs its label at the START of the row (unlike <see cref="LabeledControl"/>,
    /// which stretches). The label carries hover helptext.</summary>
    private Widget HuggingCheckbox(string keyBase, ColorScheme colors, float scale, bool value, Action<bool> onChanged)
    {
        Widget label = new Text(
            Lang.Get("scribe:" + keyBase),
            Pegged(ScribeRowConstants.BaseSettingsFontSize * scale, colors.OnSurface));

        Widget labelWithHelp = new Tooltip(
            child: label,
            content: new Padding(
                EdgeInsets.All(6),
                child: new Text(
                    Lang.Get("scribe:" + keyBase + "-help"),
                    Pegged(13 * scale, colors.OnSurface, softWrap: true))),
            useGlobalOverlay: true);

        return new Row(
            spacing: 8 * scale,
            mainAxisSize: MainAxisSize.Min,
            mainAxisAlignment: MainAxisAlignment.Start,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children: new Widget[]
            {
                new Checkbox(
                    value: value,
                    onChanged: onChanged,
                    size: ScribeRowConstants.BaseSettingsCheckboxSize * scale),
                labelWithHelp,
            });
    }

    /// <summary>Wraps a control with its localized <see cref="Tooltip"/>-carrying caption above it.</summary>
    private Widget LabeledControl(string keyBase, ColorScheme colors, float scale, Widget control)
    {
        var label = new Text(
            Lang.Get("scribe:" + keyBase),
            Pegged(ScribeRowConstants.BaseSettingsFontSize * scale, colors.OnSurface));

        // useGlobalOverlay so the tooltip isn't clipped by the scroll viewport.
        Widget labelWithHelp = new Tooltip(
            child: label,
            content: new Padding(
                EdgeInsets.All(6),
                child: new Text(
                    Lang.Get("scribe:" + keyBase + "-help"),
                    Pegged(13 * scale, colors.OnSurface, softWrap: true))),
            useGlobalOverlay: true);

        return new Column(
            spacing: 5 * scale,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[] { labelWithHelp, control });
    }
}
