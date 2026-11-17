using System;
using Gui.Core.Layout;           // MainAxisSize
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle, TextAlignment
using Gui.Widgets.Basic;         // Container, Button, Text
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, BuildContext, Theme, ColorScheme
using Gui.Widgets.Input;         // GestureDetector
using Gui.Widgets.Layout;        // Column, Row, Expanded, SizedBox, Center, CrossAxisAlignment, MainAxisAlignment
using Gui.Widgets.Painting;      // BoxStyle
using OpenTK.Mathematics;        // Vector4
using Scribe.Core;
using Vintagestory.API.Config;   // Lang

namespace Scribe;

/// <summary>
/// The date picker's panel: a month, a day and a year stepper over a <see cref="ScribeDatePickerModel"/>, and
/// Set and Cancel buttons. The stepping rules live in the model; the panel only draws it and rebuilds after each
/// step. <see cref="ScribeRowMenuHost"/> places it as an overlay beside the row.
/// </summary>
internal sealed class ScribeDatePickerPanel : StatefulWidget
{
    public ScribeDatePickerPanel(
        ScribeDatePickerModel model, ColorScheme colors, ButtonStyle buttonStyle, ScribeDate today,
        Action onSet, Action onCancel, ScribeAmbientLightSampler.Shade shade)
    {
        Model = model;
        Colors = colors;
        ButtonStyle = buttonStyle;
        Today = today;
        OnSet = onSet;
        OnCancel = onCancel;
        Shade = shade;
    }

    public ScribeDatePickerModel Model { get; }
    public ColorScheme Colors { get; }

    /// <summary>The dialog's button look, captured when the picker opened (the overlay is outside the dialog's theme).</summary>
    public ButtonStyle ButtonStyle { get; }

    /// <summary>The date the Today button returns the picker to.</summary>
    public ScribeDate Today { get; }
    public Action OnSet { get; }
    public Action OnCancel { get; }

    /// <summary>The dialog's illumination shade; the overlay sits outside the body's tint, so the panel re-applies it.</summary>
    public ScribeAmbientLightSampler.Shade Shade { get; }

    /// <summary>The panel's fixed width.</summary>
    internal const float Width = 244f;

    /// <summary>The panel's approximate height, used to decide whether it opens above or below its row.</summary>
    internal const float EstimatedHeight = 168f;

    public override State CreateState() => new ScribeDatePickerPanelState();
}

internal sealed class ScribeDatePickerPanelState : State<ScribeDatePickerPanel>
{
    private const float Spacing = 6f;
    private const float ValueWidth = 78f;

    public override Widget Build(BuildContext context)
    {
        var colors = Widget.Colors;
        var model = Widget.Model;
        var labelStyle = new TextStyle { FontSize = 14, Color = colors.OnSurface, FontFamily = ScribeTaskFont.ButtonFamily };
        var valueStyle = labelStyle with { Align = TextAlignment.Center, SoftWrap = false };
        var buttonText = new TextStyle { FontSize = 14, Color = colors.OnPrimary, FontFamily = ScribeTaskFont.ButtonFamily };
        var stepButton = Widget.ButtonStyle with { Padding = EdgeInsets.Symmetric(2f, 9f) };
        var actionButton = Widget.ButtonStyle with { Padding = EdgeInsets.Symmetric(4f, 12f) };

        Widget Stepper(string labelKey, string value, string back, string forward, Func<int, bool> step) => new Row(
            spacing: Spacing,
            crossAxisAlignment: CrossAxisAlignment.Center,
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[]
            {
                new Expanded(child: new Text(Lang.Get(labelKey), labelStyle with { SoftWrap = false })),
                new Button(child: new Text(back, buttonText), style: stepButton, onTap: _ => Step(step, -1)),
                // Align on the text, not a Center: a Center fills the height it is offered and would stretch the row.
                new SizedBox(width: ValueWidth, child: new Text(value, valueStyle)),
                new Button(child: new Text(forward, buttonText), style: stepButton, onTap: _ => Step(step, 1)),
            });

        var content = new Column(
            spacing: Spacing,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                Stepper("scribe:scribe-datepicker-month", ScribeWorldCalendar.MonthName(model.Month), "<", ">", model.StepMonth),
                Stepper("scribe:scribe-datepicker-day", model.Day.ToString(), "-", "+", model.StepDay),
                Stepper("scribe:scribe-datepicker-year", model.Year.ToString(), "-", "+", model.StepYear),
                new Row(
                    spacing: Spacing,
                    mainAxisSize: MainAxisSize.Max,
                    children: new Widget[]
                    {
                        new Expanded(child: new Row(children: new Widget[]
                        {
                            new Button(child: new Text(Lang.Get("scribe:scribe-datepicker-today"), buttonText),
                                style: actionButton, onTap: _ => { Widget.Model.Reset(Widget.Today); SetState(() => { }); }),
                        })),
                        new Button(child: new Text(Lang.Get("scribe:scribe-datepicker-cancel"), buttonText),
                            style: actionButton, onTap: _ => Widget.OnCancel()),
                        new Button(child: new Text(Lang.Get("scribe:scribe-datepicker-set"), buttonText),
                            style: actionButton, onTap: _ => Widget.OnSet()),
                    }),
            });

        var shade = Widget.Shade;
        // The panel takes every press so one on its margin never reaches the barrier behind it.
        return new SizedBox(
            width: ScribeDatePickerPanel.Width,
            child: new ScribeGlobalTint(
                new GestureDetector(
                    onPress: _ => { },
                    child: new Container(
                        style: new BoxStyle
                        {
                            Color = colors.SurfaceHigh with { W = 1f },
                            BorderColor = colors.OnSurface with { W = 0.35f },
                            BorderThickness = 1f,
                            CornerRadius = new Vector4(6f),
                            Padding = EdgeInsets.All(10f),
                        },
                        child: content)),
                brightness: shade.Brightness,
                tintR: shade.TintR,
                tintG: shade.TintG,
                tintB: shade.TintB));
    }

    private void Step(Func<int, bool> step, int delta)
    {
        if (step(delta)) SetState(() => { });
    }
}
