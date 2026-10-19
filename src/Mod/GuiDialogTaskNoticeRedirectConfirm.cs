using Gui;                       // GuiBase, WindowConfig
using Gui.Rendering;              // EdgeInsets
using Gui.Rendering.Text;         // TextStyle
using Gui.Widgets.Basic;          // WindowFrame, Container, Text, Button, ButtonVariant
using Gui.Widgets.Framework;      // Widget, ThemeData, ColorScheme
using Gui.Widgets.Layout;         // Column, Row, CrossAxisAlignment, MainAxisAlignment
using Gui.Widgets.Painting;       // BoxStyle
using Gui.Core.Layout;            // MainAxisSize
using OpenTK.Mathematics;         // Vector2
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;    // Lang

namespace Scribe;

/// <summary>
/// The "this notice isn't addressed to you" popup, shown when a non-recipient taps Accept on a sealed Task Notice.
/// Layered over <see cref="GuiDialogTaskNotice"/> in the same <see cref="DrawOrder"/> band; a one-shot dialog
/// constructed on demand and disposed on either button.
/// </summary>
public sealed class GuiDialogTaskNoticeRedirectConfirm : GuiBase
{
    private readonly string recipientName;
    private readonly Action onConfirm;

    public GuiDialogTaskNoticeRedirectConfirm(ICoreClientAPI capi, string recipientName, Action onConfirm) : base(capi)
    {
        this.recipientName = recipientName;
        this.onConfirm = onConfirm;
    }

    public override string DialogCode => "scribetasknoticeredirectconfirm";

    /// <summary>Matches <see cref="GuiDialogTaskNotice"/>'s band; opens layered over it.</summary>
    public override double DrawOrder => 0.2;

    protected override WindowConfig CreateWindowConfig() => new()
    {
        Size = new Vector2(360, 190),
        Draggable = true,
        Resizable = false,
    };

    protected override Widget Build()
    {
        var colors = ThemeData.Default.ColorScheme;

        Widget confirmButton = new Button(
            child: new Text(Lang.Get("scribe:scribe-tasknotice-redirect-confirm-confirm-button"),
                new TextStyle { FontSize = 13, Color = colors.OnPrimary, FontFamily = ScribeTaskFont.ButtonFamily }),
            variant: ButtonVariant.Primary,
            onTap: _ =>
            {
                onConfirm();
                TryClose();
            });

        Widget cancelButton = new Button(
            child: new Text(Lang.Get("scribe:scribe-tasknotice-redirect-confirm-cancel-button"),
                new TextStyle { FontSize = 13, Color = colors.OnSurface, FontFamily = ScribeTaskFont.ButtonFamily }),
            variant: ButtonVariant.Secondary,
            onTap: _ => TryClose());

        return ScribeTextDefaults.WrapChrome(new WindowFrame(
            title: Lang.Get("scribe:scribe-tasknotice-redirect-confirm-title"),
            onClose: () => TryClose(),
            child: new Container(
                style: new BoxStyle { Color = colors.Surface },
                child: new Padding(
                    EdgeInsets.All(14),
                    child: new Column(
                        crossAxisAlignment: CrossAxisAlignment.Stretch,
                        mainAxisSize: MainAxisSize.Min,
                        spacing: 12,
                        children: new Widget[]
                        {
                            new Text(
                                Lang.Get("scribe:scribe-tasknotice-redirect-confirm-body", recipientName),
                                new TextStyle { FontSize = 14, Color = colors.OnSurface, SoftWrap = true }),
                            new Row(
                                spacing: 8f,
                                mainAxisAlignment: MainAxisAlignment.End,
                                mainAxisSize: MainAxisSize.Max,
                                children: new Widget[] { cancelButton, confirmButton }),
                        })))));
    }
}
