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
/// DEV-ONLY live-tuning window for the 5 Scribe writing-station box targets (add-scribe-block-box-tuning).
/// A structural clone of <see cref="ScribeGearTuningDialog"/>: 5 groups (Inbox, Inbox-Wall, Scriptorium,
/// Assignment Desk, Chalkboard) of 6 labeled <see cref="ScribeNumericField"/>s each, writing straight through
/// <see cref="ScribeModSystem.UpdateBoxTuning"/> (which persists + raises <c>BoxTuningChanged</c>) — the
/// world hitbox is computed live off the persisted value on every collision/selection call, so a nudge
/// here changes an already-placed block's hitbox with no rebuild/relaunch. Opened by the <c>.boxtune</c>
/// client command.
///
/// <para>Throwaway aid, not shipped UI: when a target's box is finalized, fold the chosen numbers back
/// into its blocktype JSON and delete this file, <see cref="ScribeBoxTuning"/>, and the command.
/// Deliberately NOT localized (raw English labels) for the same reason.</para>
/// </summary>
public sealed class ScribeBoxTuningDialog : GuiBase
{
    private readonly ScribeModSystem modSystem;
    private readonly ScrollController scrollController = new();
    private readonly ScribeNumericFocusRegistry numericFocus = new();

    public ScribeBoxTuningDialog(ICoreClientAPI capi, ScribeModSystem modSystem) : base(capi)
    {
        this.modSystem = modSystem;
        modSystem.BoxTuningChanged += OnTuningChanged;
    }

    public override string DialogCode => "scribeboxtune";

    /// <summary>Match <see cref="ScribeGearTuningDialog.DrawOrder"/>'s 0.2 band so this stays on top of the
    /// block it tunes live.</summary>
    public override double DrawOrder => 0.2;

    protected override WindowConfig CreateWindowConfig() => new()
    {
        Size = new Vector2(360, 560),
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
        var t = modSystem.BoxTuning;
        var colors = ThemeData.Default.ColorScheme;

        var body = new Column(
            spacing: 14,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                new Text("Block box tuning (dev)",
                    new TextStyle { FontSize = 15, Weight = FontWeight.Bold, Color = colors.OnSurface }),
                new Text("Live collision/selection box, world units. Writes to scribe-box-tuning.json.",
                    new TextStyle { FontSize = 12, Color = colors.OnSurfaceVariant, SoftWrap = true }),
                new Divider(),

                new Text("Inbox (ground)", new TextStyle { FontSize = 13, Weight = FontWeight.Bold, Color = colors.OnSurface }),
                Field("x1", colors, t.InboxX1, "inboxx1", v => modSystem.UpdateBoxTuning(b => b.InboxX1 = v)),
                Field("y1", colors, t.InboxY1, "inboxy1", v => modSystem.UpdateBoxTuning(b => b.InboxY1 = v)),
                Field("z1", colors, t.InboxZ1, "inboxz1", v => modSystem.UpdateBoxTuning(b => b.InboxZ1 = v)),
                Field("x2", colors, t.InboxX2, "inboxx2", v => modSystem.UpdateBoxTuning(b => b.InboxX2 = v)),
                Field("y2", colors, t.InboxY2, "inboxy2", v => modSystem.UpdateBoxTuning(b => b.InboxY2 = v)),
                Field("z2", colors, t.InboxZ2, "inboxz2", v => modSystem.UpdateBoxTuning(b => b.InboxZ2 = v)),

                new Divider(),

                new Text("Inbox (wall-mounted)", new TextStyle { FontSize = 13, Weight = FontWeight.Bold, Color = colors.OnSurface }),
                Field("x1", colors, t.InboxWallX1, "inboxwallx1", v => modSystem.UpdateBoxTuning(b => b.InboxWallX1 = v)),
                Field("y1", colors, t.InboxWallY1, "inboxwally1", v => modSystem.UpdateBoxTuning(b => b.InboxWallY1 = v)),
                Field("z1", colors, t.InboxWallZ1, "inboxwallz1", v => modSystem.UpdateBoxTuning(b => b.InboxWallZ1 = v)),
                Field("x2", colors, t.InboxWallX2, "inboxwallx2", v => modSystem.UpdateBoxTuning(b => b.InboxWallX2 = v)),
                Field("y2", colors, t.InboxWallY2, "inboxwally2", v => modSystem.UpdateBoxTuning(b => b.InboxWallY2 = v)),
                Field("z2", colors, t.InboxWallZ2, "inboxwallz2", v => modSystem.UpdateBoxTuning(b => b.InboxWallZ2 = v)),

                new Divider(),

                new Text("Scriptorium", new TextStyle { FontSize = 13, Weight = FontWeight.Bold, Color = colors.OnSurface }),
                Field("x1", colors, t.ScriptoriumX1, "scriptoriumx1", v => modSystem.UpdateBoxTuning(b => b.ScriptoriumX1 = v)),
                Field("y1", colors, t.ScriptoriumY1, "scriptoriumy1", v => modSystem.UpdateBoxTuning(b => b.ScriptoriumY1 = v)),
                Field("z1", colors, t.ScriptoriumZ1, "scriptoriumz1", v => modSystem.UpdateBoxTuning(b => b.ScriptoriumZ1 = v)),
                Field("x2", colors, t.ScriptoriumX2, "scriptoriumx2", v => modSystem.UpdateBoxTuning(b => b.ScriptoriumX2 = v)),
                Field("y2", colors, t.ScriptoriumY2, "scriptoriumy2", v => modSystem.UpdateBoxTuning(b => b.ScriptoriumY2 = v)),
                Field("z2", colors, t.ScriptoriumZ2, "scriptoriumz2", v => modSystem.UpdateBoxTuning(b => b.ScriptoriumZ2 = v)),

                new Divider(),

                new Text("Assignment Desk", new TextStyle { FontSize = 13, Weight = FontWeight.Bold, Color = colors.OnSurface }),
                Field("x1", colors, t.AssignmentDeskX1, "deskx1", v => modSystem.UpdateBoxTuning(b => b.AssignmentDeskX1 = v)),
                Field("y1", colors, t.AssignmentDeskY1, "desky1", v => modSystem.UpdateBoxTuning(b => b.AssignmentDeskY1 = v)),
                Field("z1", colors, t.AssignmentDeskZ1, "deskz1", v => modSystem.UpdateBoxTuning(b => b.AssignmentDeskZ1 = v)),
                Field("x2", colors, t.AssignmentDeskX2, "deskx2", v => modSystem.UpdateBoxTuning(b => b.AssignmentDeskX2 = v)),
                Field("y2", colors, t.AssignmentDeskY2, "desky2", v => modSystem.UpdateBoxTuning(b => b.AssignmentDeskY2 = v)),
                Field("z2", colors, t.AssignmentDeskZ2, "deskz2", v => modSystem.UpdateBoxTuning(b => b.AssignmentDeskZ2 = v)),

                new Divider(),

                new Text("Chalkboard (selection only — no collision)", new TextStyle { FontSize = 13, Weight = FontWeight.Bold, Color = colors.OnSurface }),
                Field("x1", colors, t.ChalkboardX1, "chalkboardx1", v => modSystem.UpdateBoxTuning(b => b.ChalkboardX1 = v)),
                Field("y1", colors, t.ChalkboardY1, "chalkboardy1", v => modSystem.UpdateBoxTuning(b => b.ChalkboardY1 = v)),
                Field("z1", colors, t.ChalkboardZ1, "chalkboardz1", v => modSystem.UpdateBoxTuning(b => b.ChalkboardZ1 = v)),
                Field("x2", colors, t.ChalkboardX2, "chalkboardx2", v => modSystem.UpdateBoxTuning(b => b.ChalkboardX2 = v)),
                Field("y2", colors, t.ChalkboardY2, "chalkboardy2", v => modSystem.UpdateBoxTuning(b => b.ChalkboardY2 = v)),
                Field("z2", colors, t.ChalkboardZ2, "chalkboardz2", v => modSystem.UpdateBoxTuning(b => b.ChalkboardZ2 = v)),
            });

        return new WindowFrame(
            title: "Block box tuning",
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

    /// <summary>A labeled float field: caption over a <see cref="ScribeNumericField"/>, step 0.05, clamped
    /// to <see cref="ScribeBoxTuning.ClampAxis"/> — mirrors <see cref="ScribeGearTuningDialog.Field"/>.</summary>
    private Widget Field(string caption, ColorScheme colors, float value, string id, Action<float> onChanged)
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
                        step: 0.05f,
                        onChanged: onChanged,
                        style: new BoxStyle { Height = 34, Width = 140 },
                        focusNode: numericFocus.NodeFor(id),
                        autoFocus: numericFocus.ShouldFocus(id),
                        onStepped: () => numericFocus.ArmAutoFocus(id),
                        clamp: ScribeBoxTuning.ClampAxis)),
            });
    }

    public override void Dispose()
    {
        modSystem.BoxTuningChanged -= OnTuningChanged;
        scrollController.Dispose();
        numericFocus.Dispose();
        base.Dispose();
    }
}
