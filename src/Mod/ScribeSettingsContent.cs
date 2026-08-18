using System;
using System.Collections.Generic;
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle, FontWeight
using Gui.Widgets.Basic;         // Text, Divider
using Gui.Widgets.Framework;     // Widget, StatelessWidget, BuildContext, Theme, ValueKey
using Gui.Widgets.Gestures;      // ScrollController
using Gui.Widgets.Input;         // Dropdown, DropdownItem, NumericField, Checkbox
using Gui.Widgets.Layout;        // Column, Row, Expanded, Padding, SizedBox, CrossAxisAlignment
using Gui.Widgets.Overlay;       // Tooltip
using Gui.Widgets.Painting;      // BoxStyle
using Gui.Widgets.Scroll;        // SingleChildScrollView, Scrollbar
using Gui.Core.Layout;           // MainAxisSize
using Scribe.Core;
using Vintagestory.API.Config;   // Lang

namespace Scribe;

/// <summary>
/// The host-agnostic Scribe settings form (add-settings-tab). One LibGUI widget that renders every
/// per-player preference, grouped into a Behavior and an Appearance section, and writes each change
/// through instantly with no OK/Cancel (design D3). It makes NO assumption about window size — it is
/// wrapped in a <see cref="SingleChildScrollView"/> + <see cref="Scrollbar"/> so it fits a small host
/// (a future Desk) as well as the roomy Lectern — so the same widget is swapped into the Lectern's
/// central region AND hosted by the standalone HUD-gear settings dialog (design D2).
///
/// <para>Stateless: it reads a <see cref="ScribePlayerSettings"/> snapshot and forwards each edit
/// through <see cref="OnMutate"/> (the host's <c>ScribeModSystem.UpdateMySettings</c>), which
/// normalizes/persists and fires the change event the host rebuilds on — so the form re-renders from
/// the clamped value on its next build (live preview + clamp feedback). The scroll controller is owned
/// by the HOST (like the lectern's shared controller) so a live write-through rebuild doesn't reset the
/// scroll position.</para>
/// </summary>
internal sealed class ScribeSettingsContent : StatelessWidget
{
    private readonly ScribePlayerSettings settings;
    private readonly Action<Action<ScribePlayerSettings>> onMutate;
    private readonly ScrollController scrollController;
    private readonly ScribeNumericFocusRegistry focus;

    public ScribeSettingsContent(
        ScribePlayerSettings settings,
        Action<Action<ScribePlayerSettings>> onMutate,
        ScrollController scrollController,
        ScribeNumericFocusRegistry focus)
    {
        this.settings = settings;
        this.onMutate = onMutate;
        this.scrollController = scrollController;
        this.focus = focus;
    }

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;

        // The form's own text/checkboxes scale with the WINDOW font scale (add-settings-tab round 1), so
        // the whole form re-renders at the new size on the write-through rebuild UpdateMySettings fires.
        float scale = ScribePlayerSettings.ClampFontScale(settings.WindowFontScale);

        // Four sections separated by horizontal Dividers: Mod Behavior, Timer, Window Appearance, HUD
        // Appearance. Timer groups the two Clockmaker's Notebook timer preferences.
        var body = new Column(
            spacing: 14 * scale,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                SectionTitle(Lang.Get("scribe:settings-section-modbehavior"), colors, scale),
                BuildModBehaviorSection(colors, scale),
                new Divider(),
                SectionTitle(Lang.Get("scribe:settings-section-timer"), colors, scale),
                BuildTimerSection(colors, scale),
                new Divider(),
                SectionTitle(Lang.Get("scribe:settings-section-windowappearance"), colors, scale),
                BuildWindowAppearanceSection(colors, scale),
                new Divider(),
                SectionTitle(Lang.Get("scribe:settings-section-hudappearance"), colors, scale),
                BuildHudAppearanceSection(colors, scale),
            });

        // Wrapped in a scroll view + bar so the form fits a shorter host without clipping (design D2).
        return new Padding(
            EdgeInsets.All(10),
            child: new Scrollbar(
                controller: scrollController,
                child: new SingleChildScrollView(
                    controller: scrollController,
                    child: body))
            { AutoHide = false });
    }

    // ---------------- Sections ----------------

    /// <summary>Mod Behavior: how the mod behaves regardless of surface — completion policy and the HUD
    /// collapse toggle (a behavior switch, not an appearance one).</summary>
    private Widget BuildModBehaviorSection(ColorScheme colors, float scale)
    {
        return new Column(
            spacing: 12 * scale,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                LabeledControl(
                    "settings-completionpolicy", colors, scale,
                    new Dropdown<ScribeCompletionPolicy>(
                        value: settings.CompletionPolicy,
                        // Explicit display order (v1-playtest-fixes 5.2): 1. Keep (stay in place),
                        // 2. Keep (sink to bottom), 3. Unpin, 4. Delete — NOT the enum/alphabetical order the
                        // dropdown would otherwise show. Keep this order in sync with the Pinned-view picker
                        // in GuiDialogScribeLecternLibGui.
                        items: new List<DropdownItem<ScribeCompletionPolicy>>
                        {
                            new() { Value = ScribeCompletionPolicy.Keep,      Label = Lang.Get("scribe:scribe-completion-keep") },
                            new() { Value = ScribeCompletionPolicy.Sink,      Label = Lang.Get("scribe:scribe-completion-sink") },
                            new() { Value = ScribeCompletionPolicy.Unpin,     Label = Lang.Get("scribe:scribe-completion-unpin") },
                            new() { Value = ScribeCompletionPolicy.UnpinSink, Label = Lang.Get("scribe:scribe-completion-unpinsink") },
                            new() { Value = ScribeCompletionPolicy.Delete,    Label = Lang.Get("scribe:scribe-completion-delete") },
                        },
                        onChanged: v => onMutate(s => s.CompletionPolicy = v))),

                // What happens to a Tracker task the moment its carried-item count first reaches its
                // target (add-tracker-link-tasks 5.4/D6). Distinct axis from the completion policy above:
                // that one governs a PIN when you check it off; this one governs a TRACKER's auto-action.
                // Explicit display order Complete → Delete → Nothing (the useful-to-least-active order),
                // not the enum/alphabetical order the dropdown would otherwise pick.
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

                // "Collapse the HUD" + "Mute Scribe UI sounds" share one row as two columns
                // (scribe-mute-ui-sounds 3.2). Each hugs its own label at the start of its column
                // (scribe-settings-followups 3.3); PairedControls splits the width evenly between them.
                PairedControls(colors, scale,
                    HuggingCheckbox(
                        "settings-hudcollapsed", colors, scale,
                        value: settings.HudCollapsed,
                        onChanged: v => onMutate(s => s.HudCollapsed = v)),
                    HuggingCheckbox(
                        "settings-muteuisounds", colors, scale,
                        value: settings.MuteUiSounds,
                        onChanged: v => onMutate(s => s.MuteUiSounds = v))),

                // Storm text corruption (hud-temporal-storm-corruption): when on, the HUD scrambles its
                // text and swaps its title during a temporal storm / low stability. Behavior switch (not
                // appearance), so it sits here. Timer-related settings (Timer disappears + Alarm Volume)
                // are in the dedicated Timer section below.
                HuggingCheckbox(
                    "settings-stormcorruption", colors, scale,
                    value: settings.StormCorruption,
                    onChanged: v => onMutate(s => s.StormCorruption = v)),

                // Cuneiform toggles share one paired row. "Cuneiform tablets" (add-cuneiform-glyph-font;
                // positive polarity per D8): when on (the default), the tablet tier writes in the
                // carved-wedge cuneiform script; when off, that text renders in the player's selected task
                // font instead. "Cuneiform press-in" (add-cuneiform-handwriting-feel): when on, newly-typed
                // cuneiform presses in stroke-by-stroke; off by default, and only has an effect while
                // Cuneiform tablets is on (it animates the cuneiform glyphs). Both are behavior switches, so
                // they live here; each hugs its own label.
                PairedControls(colors, scale,
                    HuggingCheckbox(
                        "settings-cuneiformtablets", colors, scale,
                        value: settings.CuneiformTablets,
                        onChanged: v => onMutate(s => s.CuneiformTablets = v)),
                    HuggingCheckbox(
                        "settings-cuneiformprogression", colors, scale,
                        value: settings.CuneiformProgression,
                        onChanged: v => onMutate(s => s.CuneiformProgression = v))),
            });
    }

    /// <summary>Timer: Clockmaker's Notebook timer preferences — "Timer disappears" auto-clear toggle
    /// and "Alarm Volume" numeric (0–100). Side by side in one paired row.</summary>
    private Widget BuildTimerSection(ColorScheme colors, float scale)
    {
        return PairedControls(colors, scale,
            HuggingCheckbox(
                "settings-timerdisappear", colors, scale,
                value: settings.TimerAutoDisappear,
                onChanged: v => onMutate(s => s.TimerAutoDisappear = v)),
            LabeledControl(
                "settings-timeralarmvolume", colors, scale,
                IntField("timeralarmvolume", settings.TimerAlarmVolume, step: 5,
                    onChanged: v => onMutate(s => s.TimerAlarmVolume = v),
                    clamp: ScribePlayerSettings.ClampTimerAlarmVolume)));
    }

    /// <summary>Window Appearance: how Scribe's block windows (the Lectern) look — the pixel-art theme
    /// toggle, the notebook's Pixel Art Size, and the window text scale.</summary>
    private Widget BuildWindowAppearanceSection(ColorScheme colors, float scale)
    {
        return new Column(
            spacing: 12 * scale,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                // The pixel-art master toggle (scribe-themed-toggle): flips the Lectern between Scribe's
                // light "pixel-art" look (with notebook art) and the player's own global GUI theme, live.
                // Hugs its label like HudCollapsed.
                HuggingCheckbox(
                    "settings-pixelartdisplay", colors, scale,
                    value: settings.PixelArtDisplay,
                    onChanged: v => onMutate(s => s.PixelArtDisplay = v)),

                // Pixel Art Size + Window text scale share one row as two columns (§9.2). Pixel Art Size (W)
                // is the single driving width of the Lectern's proportional layout (scribe-notebook-frame),
                // stepping by 10 and snapping to the 10px grid on blur; the window text scale is a percent
                // snapping to a 5% notch on blur. Both clamp via their Core statics and re-lay-out the open
                // Lectern live.
                PairedControls(colors, scale,
                    LabeledControl(
                        "settings-pixelartsize", colors, scale,
                        IntField("pixelartsize", settings.PixelArtSize, step: 10,
                            onChanged: v => onMutate(s => s.PixelArtSize = v),
                            clamp: ScribePlayerSettings.ClampPixelArtSize)),
                    LabeledControl(
                        "settings-windowfontscale", colors, scale,
                        FontScaleField("windowfontscale", settings.WindowFontScale, v => onMutate(s => s.WindowFontScale = v)))),

                // Task-text font selector (v1-release-checklist §6): picks the family for task/note ROW
                // text; the empty-string default keeps the built-in body font. Governs task text only —
                // the in-Lectern buttons keep a fixed face. Writes through UpdateMySettings, which
                // NormalizeTaskFontFamily-clamps and repaints the open Lectern live.
                LabeledControl(
                    "settings-taskfont", colors, scale,
                    new Dropdown<string>(
                        value: ScribePlayerSettings.NormalizeTaskFontFamily(settings.TaskFontFamily),
                        items: TaskFontItems(),
                        onChanged: v => onMutate(s => s.TaskFontFamily = v))),
            });
    }

    /// <summary>Font-selector options: the empty-string "Default" (built-in body font) followed by each
    /// bundled/registered family from <see cref="ScribePlayerSettings.KnownTaskFonts"/>, labeled by its own
    /// family name. Kept in sync with the registration in <c>ScribeModSystem.RegisterCustomFonts</c>.</summary>
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

    /// <summary>HUD Appearance: how the pinned-task HUD looks — anchor, row cap, row width, position
    /// offsets, and its text scale.</summary>
    private Widget BuildHudAppearanceSection(ColorScheme colors, float scale)
    {
        return new Column(
            spacing: 12 * scale,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                // Show icons on the HUD (add-tracker-link-tasks 7.11j): when on (the default), Tracker/Link
                // rows render their item icon / guide-page book glyph; off gives a leaner text-only HUD.
                // Hugs its label like PixelArtDisplay in the Window Appearance section.
                HuggingCheckbox(
                    "settings-hudshowicons", colors, scale,
                    value: settings.HudShowIcons,
                    onChanged: v => onMutate(s => s.HudShowIcons = v)),

                LabeledControl(
                    "settings-hudanchor", colors, scale,
                    new Dropdown<ScribeHudAnchor>(
                        value: settings.HudAnchor,
                        items: AnchorItems(),
                        onChanged: v => onMutate(s => s.HudAnchor = v))),

                // Numeric fields (not sliders — sliders hijack scroll, design D8). Each clamps ON BLUR inside
                // the field (refine-settings-and-window-chrome), using the Core Clamp* static; the ValueKey
                // still remounts the field to the committed value after a write.
                //
                // Max HUD rows + HUD row width share one row as two columns (scribe-settings-followups 3.1).
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

                // HUD position (X/Y offsets) + HUD Text Size share one row as two columns
                // (v1-playtest-fixes). The offset sub-row keeps its two inline fields; the font scale sits
                // in the second column.
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

    /// <summary>Core numeric-field builder shared by all three field types. Wraps a
    /// <see cref="ScribeNumericField"/> in a <see cref="ValueKey"/>-keyed <see cref="SizedBox"/> (the field
    /// is uncontrolled — it seeds from <c>initialValue</c> only in <c>InitState</c> — so changing the
    /// wrapper's key when the persisted/clamped value differs remounts it, settling onto the clamped result
    /// after a write). Focus survives that remount because the field uses the host-owned
    /// <see cref="ScribeNumericFocusRegistry"/> node for <paramref name="id"/> and re-requests focus on
    /// mount whenever that id is armed — which the field arms (via <c>onStepped</c>) just before a step
    /// writes through, so repeated +/- or arrow presses keep focus (scribe-settings-followups focus fix).
    /// (Typing a value whose running prefix is below the min briefly clamps mid-type; +/- and arrows are
    /// the primary path.)</summary>
    private Widget NumericField(string id, int keyValue, float initialValue, float step, Action<float> onChanged,
        Func<float, float>? clamp = null) =>
        // A key-only SizedBox (no width/height → null constraints, so the child sizes itself). The ValueKey
        // remounts the uncontrolled field when the persisted/clamped value changes (settling it after a blur
        // commit).
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

    /// <summary>An integer numeric field stepping by <paramref name="step"/> that writes its rounded value
    /// through on change. Clamping happens on blur inside the field (the clamp is the Core <c>Clamp*</c>
    /// static composed to operate on the rounded int).</summary>
    private Widget IntField(string id, int value, float step, Action<int> onChanged,
        Func<int, int>? clamp = null) =>
        NumericField(id, keyValue: value, initialValue: value, step: step,
            onChanged: v => onChanged((int)MathF.Round(v)),
            clamp: clamp is null ? null : v => clamp((int)MathF.Round(v)));

    /// <summary>A labeled ±300px offset field: a small caption over an <see cref="IntField"/> (step 5). Clamps
    /// to the Core offset range on blur.</summary>
    private Widget OffsetField(string id, string caption, int value, Action<int> onChanged, ColorScheme colors, float scale)
    {
        return new Column(
            spacing: 4 * scale,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                new Text(caption, new TextStyle { FontSize = 13 * scale, Color = colors.OnSurfaceVariant }),
                IntField(id, value, step: 5, onChanged: onChanged,
                    clamp: ScribePlayerSettings.ClampHudOffset),
            });
    }

    /// <summary>A font-scale field entered as a PERCENT (add-settings-tab D8): the stored multiplier
    /// (0.80–1.20) is shown as 80–120 and stepped by 5. Clamps + snaps happen in Core's <c>Normalized()</c>;
    /// keyed by the current percent so the field settles onto the snapped result on the write-through
    /// rebuild.</summary>
    private Widget FontScaleField(string id, float scaleValue, Action<float> onChanged)
    {
        int pct = (int)MathF.Round(scaleValue * 100f);
        return NumericField(id, keyValue: pct, initialValue: pct, step: 5,
            onChanged: v => onChanged(MathF.Round(v) / 100f),
            // The field works in PERCENT (80–120); clamp in percent space by round-tripping through the Core
            // multiplier clamp (which also snaps to the 5% notch), so a blurred out-of-range percent settles
            // onto a valid notch percent.
            clamp: v => MathF.Round(ScribePlayerSettings.ClampFontScale(v / 100f) * 100f));
    }

    // ---------------- Layout helpers ----------------

    private static Widget SectionTitle(string text, ColorScheme colors, float scale) =>
        // Only ~8% larger than the window text (§9.1) — the old "+2f" absolute bump read too large.
        new Text(text, new TextStyle { FontSize = ScribeRowConstants.BaseSettingsFontSize * scale * 1.08f, Weight = FontWeight.Bold, Color = colors.OnSurface });

    /// <summary>Lay two labeled controls side by side as equal-width columns in one row
    /// (scribe-settings-followups 3.1/3.2). Each child is <see cref="Expanded"/> so they split the
    /// available width evenly, mirroring the offsets row.</summary>
    private static Widget PairedControls(ColorScheme colors, float scale, Widget left, Widget right) =>
        new Row(
            spacing: 12 * scale,
            mainAxisSize: MainAxisSize.Max,
            crossAxisAlignment: CrossAxisAlignment.Start,
            children: new Widget[] { new Expanded(child: left), new Expanded(child: right) });

    /// <summary>A checkbox that hugs its label at the START of the row instead of stretching across the
    /// full form width (scribe-settings-followups 3.3). The label keeps its hover helptext (design D6) and
    /// scales with the window font size. Unlike <see cref="LabeledControl"/> (label-above-control, stretched
    /// for full-width inputs), a toggle reads best inline with its label.</summary>
    private static Widget HuggingCheckbox(string keyBase, ColorScheme colors, float scale, bool value, Action<bool> onChanged)
    {
        Widget label = new Text(
            Lang.Get("scribe:" + keyBase),
            new TextStyle { FontSize = ScribeRowConstants.BaseSettingsFontSize * scale, Color = colors.OnSurface });

        Widget labelWithHelp = new Tooltip(
            child: label,
            content: new Padding(
                EdgeInsets.All(6),
                child: new Text(
                    Lang.Get("scribe:" + keyBase + "-help"),
                    new TextStyle { FontSize = 13 * scale, Color = colors.OnSurface, SoftWrap = true })),
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

    /// <summary>Wraps a control with its localized label (a <see cref="Tooltip"/>-carrying caption above
    /// the control), so the field is labeled and its helptext is available on hover (design D6). The
    /// label text scales with the window font scale (round 1). The label + control stack keeps a
    /// consistent left-aligned column layout for every field.</summary>
    private static Widget LabeledControl(string keyBase, ColorScheme colors, float scale, Widget control)
    {
        var label = new Text(
            Lang.Get("scribe:" + keyBase),
            new TextStyle { FontSize = ScribeRowConstants.BaseSettingsFontSize * scale, Color = colors.OnSurface });

        // Helptext surfaced on hover over the label (design D6 / spec "Helptext is available per
        // setting"). useGlobalOverlay so the tooltip isn't clipped by a scroll viewport / small host.
        Widget labelWithHelp = new Tooltip(
            child: label,
            content: new Padding(
                EdgeInsets.All(6),
                child: new Text(
                    Lang.Get("scribe:" + keyBase + "-help"),
                    new TextStyle { FontSize = 13 * scale, Color = colors.OnSurface, SoftWrap = true })),
            useGlobalOverlay: true);

        return new Column(
            spacing: 5 * scale,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[] { labelWithHelp, control });
    }
}
