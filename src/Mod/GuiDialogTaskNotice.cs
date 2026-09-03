using System;
using System.Collections.Generic;
using System.Linq;
using Gui;                       // GuiBase, WindowConfig
using Gui.Rendering;              // EdgeInsets
using Gui.Rendering.Text;         // TextStyle
using Gui.Widgets.Basic;          // Text, WindowFrame, Container, Button, ButtonVariant, Divider
using Gui.Widgets.Framework;      // Widget, ThemeData, ColorScheme
using Gui.Widgets.Gestures;       // ScrollController
using Gui.Widgets.Input;          // Dropdown, DropdownItem
using Gui.Widgets.Layout;         // Column, Row, Padding, Expanded, CrossAxisAlignment, MainAxisAlignment
using Gui.Widgets.Overlay;        // Tooltip
using Gui.Core.Layout;            // MainAxisSize
using OpenTK.Mathematics;         // Vector2, Vector4
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;    // Lang

namespace Scribe;

/// <summary>
/// The Task Notice's held-item review dialog (`task-notice-item` capability, tasks.md 3.4). A minimal
/// standalone window — NOT a <see cref="ScribeDialogBase"/> subclass, since that base class's
/// <see cref="IScribeDocumentHost"/> contract (editor write-through, lock, guestbook) is built for a real
/// editable document surface and doesn't fit a locked, one-shot Accept/Decline letter (see the
/// add-assignment-physical-delivery-mode design notes). Reuses <see cref="ScribeReadContent"/> for the
/// body (read-only, completion/pin inert) exactly as every other Scribe surface renders its Task/Tracker/
/// Link/Craft rows, and mirrors <see cref="ScribeInboxRow"/>'s Accept/Decline button shapes for the footer.
///
/// <para>The notice's document is a frozen snapshot read once at construction: it never changes while
/// this dialog is open (Accept/Decline both consume the item and close the dialog), so there is no
/// server-resync path to react to, unlike every editable Scribe surface.</para>
/// </summary>
public sealed class GuiDialogTaskNotice : GuiBase
{
    private readonly ItemSlot slot;
    private readonly ScribeModSystem modSystem;
    private readonly ScribeDocument document;
    private readonly ScrollController scrollController = new();
    private readonly ScribeAnimationRegistry collapseRegistry = new();

    private List<ScribeAcceptCandidate> acceptCandidates = new();
    private int selectedCandidateIndex;
    private bool showAcceptPicker;

    public GuiDialogTaskNotice(ItemSlot slot, ICoreClientAPI capi, ScribeModSystem modSystem) : base(capi)
    {
        this.slot = slot;
        this.modSystem = modSystem;
        ScribeDocumentAttributes.TryReadFrom(slot.Itemstack!, out var doc);
        document = doc ?? new ScribeDocument();
        RefreshAcceptCandidates();
    }

    public override string DialogCode => "scribetasknotice";

    /// <summary>Matches the settings window's band (§ScribeSettingsDialog) — a held-item review dialog
    /// can open over a Lectern/Notebook/Tablet just as the settings gear can.</summary>
    public override double DrawOrder => 0.2;

    protected override WindowConfig CreateWindowConfig() => new()
    {
        Size = new Vector2(460, 560),
        Draggable = true,
        Resizable = false,
    };

    private void RefreshAcceptCandidates()
    {
        acceptCandidates = ScribeAcceptCandidates.Compute(capi, null);
        selectedCandidateIndex = 0;
        showAcceptPicker = false;
    }

    protected override Widget Build()
    {
        var colors = ThemeData.Default.ColorScheme;
        var style = ScribeRowStyle.FromSettings(modSystem.MySettings);

        var first = document.Blocks.FirstOrDefault();
        string fromLine = first?.Assignment is { } assignment
            ? Lang.Get("scribe:scribe-tasknotice-from", ResolvePlayerName(assignment.AssignerUid), assignment.AssignedDate)
            : "";

        var rows = document.Blocks.Select((b, i) =>
        {
            bool isItemKind = b.Kind is ScribeBlockKind.Tracker or ScribeBlockKind.Link or ScribeBlockKind.Craft;
            var (displayStack, displayName) = isItemKind
                ? ScribeItemRef.ResolveDisplay(capi.World, b.TargetItemCode ?? b.LinkTarget, b.LinkLabel)
                : (null, null);
            return new ScribeReadRowData(
                Index: i, Kind: b.Kind, Done: false, Pinned: false, TaskId: b.TaskId, Text: b.Text,
                DisplayStack: displayStack, DisplayName: displayName,
                TargetQuantity: b.TargetQuantity, CurrentQuantity: 0, LinkTarget: b.LinkTarget, Depth: b.Depth,
                IsAcceptedAssignment: false);
        }).ToList();

        Widget body = new ScribeReadContent(
            blocks: rows,
            onToggleTask: _ => { },
            onTogglePinned: _ => { },
            onOpenLink: _ => { },
            onSwitchToEditor: () => { },
            footerButtonPadding: EdgeInsets.Zero,
            style: style,
            scrollController: scrollController,
            collapseRegistry: collapseRegistry,
            onDepartureSettled: () => { },
            currentShade: default,
            readOnly: true,
            completionAndPinLive: false);

        return new WindowFrame(
            title: Lang.Get("scribe:item-tasknotice"),
            onClose: () => TryClose(),
            fillHeight: true,
            child: new Container(
                style: new Gui.Widgets.Painting.BoxStyle { Color = colors.Surface },
                child: new Column(
                    crossAxisAlignment: CrossAxisAlignment.Stretch,
                    mainAxisSize: MainAxisSize.Max,
                    children: new Widget[]
                    {
                        new Padding(EdgeInsets.All(10), child: new Text(fromLine,
                            new TextStyle { FontSize = 13, Color = colors.OnSurfaceVariant, SoftWrap = true })),
                        new Divider(),
                        new Expanded(child: body),
                        new Divider(),
                        new Padding(EdgeInsets.All(10), child: BuildActionRow(colors)),
                    })));
    }

    private string ResolvePlayerName(string uid) => capi.World.PlayerByUid(uid)?.PlayerName ?? uid;

    private Widget BuildActionRow(ColorScheme colors)
    {
        Widget decline = ActionButton("scribe:scribe-assignment-action-decline", ButtonVariant.Danger, () =>
        {
            modSystem.SendTaskNoticeAction(slot, ScribeAssignmentAction.Decline, null);
            TryClose();
        }, colors);

        return new Row(
            spacing: 8f,
            mainAxisAlignment: MainAxisAlignment.End,
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[] { decline, BuildAcceptControl(colors) });
    }

    /// <summary>Mirrors <see cref="ScribeInboxRow.BuildAcceptControl"/>'s three shapes (none/one/many
    /// eligible carried items), adapted to this dialog's plain-field + <see cref="ForceRebuild"/> state
    /// (no <c>StatefulWidget</c> here — see class remarks) instead of <c>SetState</c>.</summary>
    private Widget BuildAcceptControl(ColorScheme colors)
    {
        var candidates = acceptCandidates;
        if (candidates.Count == 0)
        {
            return new Tooltip(
                child: new Button(
                    child: new Text(Lang.Get("scribe:scribe-assignment-action-accept"),
                        new TextStyle { FontSize = 13, Color = colors.OnSurfaceVariant, FontFamily = ScribeTaskFont.ButtonFamily }),
                    variant: ButtonVariant.Secondary,
                    enabled: false),
                content: new Padding(EdgeInsets.All(6), child: new Text(
                    Lang.Get("scribe:scribe-assignment-no-eligible-target"),
                    new TextStyle { FontSize = 12, Color = colors.OnSurface, SoftWrap = true })),
                useGlobalOverlay: true);
        }

        if (candidates.Count == 1)
            return ActionButton("scribe:scribe-assignment-action-accept", ButtonVariant.Primary,
                () => AcceptOnto(candidates[0]), colors);

        if (!showAcceptPicker)
            return ActionButton("scribe:scribe-assignment-action-accept", ButtonVariant.Primary,
                () => { showAcceptPicker = true; ForceRebuild(); }, colors);

        int idx = Math.Clamp(selectedCandidateIndex, 0, candidates.Count - 1);
        Widget picker = new Dropdown<int>(
            value: idx,
            items: candidates.Select((c, i) => new DropdownItem<int> { Value = i, Label = c.Label }).ToList(),
            onChanged: v => { selectedCandidateIndex = v; ForceRebuild(); },
            style: ThemeData.Default.DropdownStyle);
        Widget confirmButton = ActionButton("scribe:scribe-assignment-action-accept", ButtonVariant.Primary,
            () => AcceptOnto(candidates[idx]), colors);
        return new Column(
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            spacing: 4f,
            children: new Widget[] { picker, confirmButton });
    }

    private void AcceptOnto(ScribeAcceptCandidate candidate)
    {
        modSystem.SendTaskNoticeAction(slot, ScribeAssignmentAction.Accept, candidate);
        TryClose();
    }

    private static Widget ActionButton(string labelKey, ButtonVariant variant, Action onTap, ColorScheme colors)
    {
        Vector4 fg = variant == ButtonVariant.Danger ? colors.OnError : colors.OnPrimary;
        return new Button(
            child: new Text(Lang.Get(labelKey), new TextStyle { FontSize = 13, Color = fg, FontFamily = ScribeTaskFont.ButtonFamily }),
            variant: variant,
            onTap: _ => onTap());
    }

    public override void Dispose()
    {
        scrollController.Dispose();
        collapseRegistry.Dispose();
        base.Dispose();
    }
}
