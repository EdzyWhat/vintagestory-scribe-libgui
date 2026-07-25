using System;
using System.Collections.Generic;
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle, FontWeight
using Gui.Widgets.Basic;         // Text
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

    public ScribeSettingsContent(
        ScribePlayerSettings settings,
        Action<Action<ScribePlayerSettings>> onMutate,
        ScrollController scrollController)
    {
        this.settings = settings;
        this.onMutate = onMutate;
        this.scrollController = scrollController;
    }

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;

        // The form's own text/checkboxes scale with the WINDOW font scale (add-settings-tab round 1), so
        // the whole form re-renders at the new size on the write-through rebuild UpdateMySettings fires.
        float scale = ScribePlayerSettings.ClampFontScale(settings.WindowFontScale);

        var body = new Column(
            spacing: 14 * scale,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                SectionTitle(Lang.Get("scribe:settings-section-behavior"), colors, scale),
                BuildBehaviorSection(colors, scale),
                SectionTitle(Lang.Get("scribe:settings-section-appearance"), colors, scale),
                BuildAppearanceSection(colors, scale),
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

    private Widget BuildBehaviorSection(ColorScheme colors, float scale)
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
                        items: new List<DropdownItem<ScribeCompletionPolicy>>
                        {
                            new() { Value = ScribeCompletionPolicy.Sink,   Label = Lang.Get("scribe:scribe-completion-sink") },
                            new() { Value = ScribeCompletionPolicy.Keep,   Label = Lang.Get("scribe:scribe-completion-keep") },
                            new() { Value = ScribeCompletionPolicy.Unpin,  Label = Lang.Get("scribe:scribe-completion-unpin") },
                            new() { Value = ScribeCompletionPolicy.Delete, Label = Lang.Get("scribe:scribe-completion-delete") },
                        },
                        onChanged: v => onMutate(s => s.CompletionPolicy = v))),

                LabeledControl(
                    "settings-hudcollapsed", colors, scale,
                    new Checkbox(
                        value: settings.HudCollapsed,
                        onChanged: v => onMutate(s => s.HudCollapsed = v),
                        size: ScribeRowConstants.BaseSettingsCheckboxSize * scale)),
            });
    }

    private Widget BuildAppearanceSection(ColorScheme colors, float scale)
    {
        return new Column(
            spacing: 12 * scale,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                LabeledControl(
                    "settings-hudanchor", colors, scale,
                    new Dropdown<ScribeHudAnchor>(
                        value: settings.HudAnchor,
                        items: AnchorItems(),
                        onChanged: v => onMutate(s => s.HudAnchor = v))),

                // Numeric fields (not sliders — sliders hijack scroll, design D8), each clamped in
                // onChanged (Core Normalized() is the single clamp source) and KEYED by its current value
                // so a clamp that changed the value remounts the (uncontrolled) field showing the clamped
                // result on the next write-through rebuild.
                LabeledControl(
                    "settings-hudmaxrows", colors, scale,
                    IntField(settings.HudMaxRows, step: 1,
                        onChanged: v => onMutate(s => s.HudMaxRows = v))),

                LabeledControl(
                    "settings-hudrowwidth", colors, scale,
                    IntField(settings.HudRowWidth, step: 5,
                        onChanged: v => onMutate(s => s.HudRowWidth = v))),

                // HUD X/Y offsets on ONE row (design D5); each is a ±300 pixel nudge relative to the
                // anchor's pre-baked offset (design D8), stepping by 5.
                LabeledControl(
                    "settings-hudoffset", colors, scale,
                    new Row(
                        spacing: 12 * scale,
                        mainAxisSize: MainAxisSize.Max,
                        children: new Widget[]
                        {
                            new Expanded(child: OffsetField(
                                Lang.Get("scribe:settings-hudoffsetx"), settings.HudOffsetX,
                                v => onMutate(s => s.HudOffsetX = v), colors, scale)),
                            new Expanded(child: OffsetField(
                                Lang.Get("scribe:settings-hudoffsety"), settings.HudOffsetY,
                                v => onMutate(s => s.HudOffsetY = v), colors, scale)),
                        })),

                LabeledControl(
                    "settings-hudfontscale", colors, scale,
                    FontScaleField(settings.HudFontScale, v => onMutate(s => s.HudFontScale = v))),

                LabeledControl(
                    "settings-windowfontscale", colors, scale,
                    FontScaleField(settings.WindowFontScale, v => onMutate(s => s.WindowFontScale = v))),
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

    /// <summary>An integer <see cref="NumericField"/> stepping by <paramref name="step"/> that writes its
    /// rounded value through on change. Wrapped in a <see cref="ValueKey"/>-keyed <see cref="SizedBox"/>
    /// (the field itself takes no key): <see cref="NumericField"/> is uncontrolled (it seeds from
    /// <c>initialValue</c> only in <c>InitState</c>), so changing the wrapper's key when the persisted
    /// (clamped) value differs remounts the field, settling it onto the clamped result after a write.
    /// (Typing a value whose running prefix is below the min briefly clamps mid-type; the +/- buttons are
    /// the primary path.)</summary>
    private static Widget IntField(int value, float step, Action<int> onChanged) =>
        new SizedBox(
            height: 34,
            key: new ValueKey<int>(value),
            child: new NumericField(
                initialValue: value,
                step: step,
                onChanged: v => onChanged((int)MathF.Round(v)),
                style: new BoxStyle { Height = 34, Width = 120 }));

    /// <summary>A labeled ±300px offset field: a small caption over an <see cref="IntField"/> (step 5).</summary>
    private static Widget OffsetField(string caption, int value, Action<int> onChanged, ColorScheme colors, float scale)
    {
        return new Column(
            spacing: 4 * scale,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                new Text(caption, new TextStyle { FontSize = 13 * scale, Color = colors.OnSurfaceVariant }),
                IntField(value, step: 5, onChanged: onChanged),
            });
    }

    /// <summary>A font-scale field entered as a PERCENT (add-settings-tab D8): the stored multiplier
    /// (0.80–1.20) is shown as 80–120 and stepped by 5. Not a slider (sliders hijack scroll). Clamps +
    /// snaps happen in Core's <c>Normalized()</c>; keyed by the current percent so the field settles onto
    /// the snapped result on the write-through rebuild.</summary>
    private static Widget FontScaleField(float scaleValue, Action<float> onChanged)
    {
        int pct = (int)MathF.Round(scaleValue * 100f);
        return new SizedBox(
            height: 34,
            key: new ValueKey<int>(pct),
            child: new NumericField(
                initialValue: pct,
                step: 5,
                onChanged: v => onChanged(MathF.Round(v) / 100f),
                style: new BoxStyle { Height = 34, Width = 120 }));
    }

    // ---------------- Layout helpers ----------------

    private static Widget SectionTitle(string text, ColorScheme colors, float scale) =>
        new Text(text, new TextStyle { FontSize = ScribeRowConstants.BaseSettingsFontSize * scale + 2f, Weight = FontWeight.Bold, Color = colors.OnSurface });

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
