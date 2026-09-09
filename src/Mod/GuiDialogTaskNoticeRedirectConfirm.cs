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
/// The "this notice isn't addressed to you" warning popup (add-task-notice-redirect-confirm design D4):
/// shown when a player holding a sealed Task Notice taps Accept but isn't its recorded recipient.
/// Layered over the already-open <see cref="GuiDialogTaskNotice"/> (same <see cref="DrawOrder"/> band —
/// same-band dialogs stack fine by open-order, per <see cref="GuiDialogScribeQuestPrompt"/>'s own doc
/// comment). A purpose-built, one-shot dialog — not a reusable "Yes/No modal" primitive (Non-Goal): it
/// exists only for the duration of one Accept click's decision, constructed on demand and disposed on
/// either button, mirroring <see cref="ItemScribeTaskNotice"/>'s own on-demand construction of
/// <see cref="GuiDialogTaskNotice"/> rather than <see cref="GuiDialogScribeQuestPrompt"/>'s self-managing
/// subscribe-and-tick pattern (there is no pending-queue concept here).
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

    /// <summary>Matches <see cref="GuiDialogTaskNotice"/>'s own band — this opens layered over it.</summary>
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

        return new WindowFrame(
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
                        }))));
    }
}
