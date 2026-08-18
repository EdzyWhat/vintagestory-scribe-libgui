using System;
using Gui;                       // GuiBase, WindowConfig
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle, FontWeight
using Gui.Widgets.Basic;         // WindowFrame, Container, Text, Divider
using Gui.Widgets.Framework;     // Widget, ThemeData, ValueKey
using Gui.Widgets.Gestures;      // ScrollController
using Gui.Widgets.Input;         // (ScribeNumericField lives in this assembly)
using Gui.Widgets.Layout;        // Column, CrossAxisAlignment, Padding, SizedBox
using Gui.Widgets.Painting;      // BoxStyle
using Gui.Widgets.Scroll;        // SingleChildScrollView, Scrollbar
using Gui.Core.Layout;           // MainAxisSize
using OpenTK.Mathematics;        // Vector2
using Vintagestory.API.Client;

namespace Scribe;

/// <summary>
/// DEV-ONLY live-tuning window for the Timer-tab gearworks layout (add-timer-gearworks art follow-up).
/// A near-clone of <see cref="ScribeSettingsDialog"/>: a small draggable window hosting four
/// <see cref="ScribeNumericField"/>s that write straight through <see cref="ScribeModSystem.UpdateGearTuning"/>
/// (which persists + raises <c>GearTuningChanged</c>), so nudging a number re-lays-out an open Clockmaker's
/// Notebook Timer tab instantly — no rebuild/relaunch. Opened by the <c>.geartune</c> client command.
///
/// <para>Throwaway aid, not shipped UI: when the layout is finalized, fold the chosen numbers back into the
/// <c>ScribeGearworks.Build</c> constants and delete this file, <see cref="ScribeGearTuning"/>, and the
/// command. Deliberately NOT localized (raw English labels) for the same reason.</para>
/// </summary>
public sealed class ScribeGearTuningDialog : GuiBase
{
    private readonly ScribeModSystem modSystem;
    private readonly ScrollController scrollController = new();
    private readonly ScribeNumericFocusRegistry numericFocus = new();

    public ScribeGearTuningDialog(ICoreClientAPI capi, ScribeModSystem modSystem) : base(capi)
    {
        this.modSystem = modSystem;
        modSystem.GearTuningChanged += OnTuningChanged;
    }

    public override string DialogCode => "scribegeartune";

    protected override WindowConfig CreateWindowConfig() => new()
    {
        Size = new Vector2(360, 400),
        Draggable = true,
        Resizable = false,
    };

    private void OnTuningChanged()
    {
        if (!IsOpened()) return;
        ForceRebuild();   // re-seed the fields onto the clamped/persisted values (live preview)
    }

    protected override Widget Build()
    {
        var t = modSystem.GearTuning;
        var colors = ThemeData.Default.ColorScheme;

        var body = new Column(
            spacing: 14,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                new Text("Gearworks tuning (dev)",
                    new TextStyle { FontSize = 15, Weight = FontWeight.Bold, Color = colors.OnSurface }),
                new Text("Live layout knobs, unscaled px. Writes to scribe-gear-tuning.json.",
                    new TextStyle { FontSize = 12, Color = colors.OnSurfaceVariant, SoftWrap = true }),
                new Divider(),

                Field("Small gears: overlap X (mesh depth)", colors,
                    value: t.SmallGearOverlapX, step: 2f, id: "smallx",
                    onChanged: v => modSystem.UpdateGearTuning(g => g.SmallGearOverlapX = v),
                    clamp: ScribeGearTuning.ClampX),

                Field("Small gears: Y (higher = lower on screen)", colors,
                    value: t.SmallGearY, step: 2f, id: "smally",
                    onChanged: v => modSystem.UpdateGearTuning(g => g.SmallGearY = v),
                    clamp: ScribeGearTuning.ClampY),

                Field("Small gears: resting angle (deg)", colors,
                    value: t.SmallGearAngle, step: 2f, id: "smallangle",
                    onChanged: v => modSystem.UpdateGearTuning(g => g.SmallGearAngle = v),
                    clamp: ScribeGearTuning.ClampAngle),

                new Divider(),

                Field("Large wheel: idle peek", colors,
                    value: t.WheelIdlePeek, step: 2f, id: "idlepeek",
                    onChanged: v => modSystem.UpdateGearTuning(g => g.WheelIdlePeek = v),
                    clamp: ScribeGearTuning.ClampPeek),

                Field("Large wheel: active peek", colors,
                    value: t.WheelActivePeek, step: 2f, id: "activepeek",
                    onChanged: v => modSystem.UpdateGearTuning(g => g.WheelActivePeek = v),
                    clamp: ScribeGearTuning.ClampPeek),

                Field("Large wheel: resting angle (deg)", colors,
                    value: t.WheelAngle, step: 2f, id: "wheelangle",
                    onChanged: v => modSystem.UpdateGearTuning(g => g.WheelAngle = v),
                    clamp: ScribeGearTuning.ClampAngle),

                Field("Large wheel: teeth count", colors,
                    value: t.WheelTeeth, step: 1f, id: "wheelteeth",
                    onChanged: v => modSystem.UpdateGearTuning(g => g.WheelTeeth = v),
                    clamp: ScribeGearTuning.ClampTeeth),

                Field("Large wheel: tooth spacing (as-if teeth)", colors,
                    value: t.WheelToothSpacing, step: 1f, id: "wheelspacing",
                    onChanged: v => modSystem.UpdateGearTuning(g => g.WheelToothSpacing = v),
                    clamp: ScribeGearTuning.ClampTeeth),

                new Divider(),

                Field("Large temporal gear: scale (×)", colors,
                    value: t.LargeGearScale, step: 0.05f, id: "largescale",
                    onChanged: v => modSystem.UpdateGearTuning(g => g.LargeGearScale = v),
                    clamp: ScribeGearTuning.ClampScale),

                Field("Small gear: scale (×)", colors,
                    value: t.SmallGearScale, step: 0.05f, id: "smallscale",
                    onChanged: v => modSystem.UpdateGearTuning(g => g.SmallGearScale = v),
                    clamp: ScribeGearTuning.ClampScale),

                Field("Large temporal gear: Y (higher = lower on screen)", colors,
                    value: t.LargeGearY, step: 2f, id: "largey",
                    onChanged: v => modSystem.UpdateGearTuning(g => g.LargeGearY = v),
                    clamp: ScribeGearTuning.ClampY),

                new Divider(),

                Field("Trim box: width", colors,
                    value: t.TrimBoxWidth, step: 4f, id: "trimw",
                    onChanged: v => modSystem.UpdateGearTuning(g => g.TrimBoxWidth = v),
                    clamp: ScribeGearTuning.ClampDim),

                Field("Trim box: height", colors,
                    value: t.TrimBoxHeight, step: 4f, id: "trimh",
                    onChanged: v => modSystem.UpdateGearTuning(g => g.TrimBoxHeight = v),
                    clamp: ScribeGearTuning.ClampDim),

                Field("Trim box: Y offset (+down / -up, no content reflow)", colors,
                    value: t.TrimBoxY, step: 2f, id: "trimy",
                    onChanged: v => modSystem.UpdateGearTuning(g => g.TrimBoxY = v),
                    clamp: ScribeGearTuning.ClampOffset),

            });

        return new WindowFrame(
            title: "Gearworks tuning",
            onClose: () => TryClose(),
            fillHeight: true,
            child: new Container(
                style: new BoxStyle { Color = colors.Surface },
                child: new Padding(
                    EdgeInsets.All(12),
                    child: new Scrollbar(
                        controller: scrollController,
                        child: new SingleChildScrollView(
                            controller: scrollController,
                            child: body))
                    { AutoHide = false })));
    }

    /// <summary>A labeled float field: caption over a <see cref="ScribeNumericField"/>. Mirrors the settings
    /// form's uncontrolled-field + <see cref="ValueKey"/> remount pattern so a blur-clamp settles the field
    /// onto the persisted value; focus survives the write-through rebuild via the shared focus registry.
    /// Keyed off the value ×100 (rounded) so the remount fires on any committed change, including the
    /// sub-integer 0.05 scale steps (a bare int-round would collide 1.00 and 1.05 onto the same key and the
    /// +/- buttons wouldn't visibly update).</summary>
    private Widget Field(string caption, ColorScheme colors, float value, float step, string id,
        Action<float> onChanged, Func<float, float> clamp)
    {
        return new Column(
            spacing: 5,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                new Text(caption, new TextStyle { FontSize = 13, Color = colors.OnSurface }),
                new SizedBox(
                    key: new ValueKey<int>((int)MathF.Round(value * 100f)),
                    child: new ScribeNumericField(
                        initialValue: value,
                        step: step,
                        onChanged: onChanged,
                        style: new BoxStyle { Height = 34, Width = 140 },
                        focusNode: numericFocus.NodeFor(id),
                        autoFocus: numericFocus.ShouldFocus(id),
                        onStepped: () => numericFocus.ArmAutoFocus(id),
                        clamp: clamp)),
            });
    }

    public override void Dispose()
    {
        modSystem.GearTuningChanged -= OnTuningChanged;
        scrollController.Dispose();
        numericFocus.Dispose();
        base.Dispose();
    }
}
