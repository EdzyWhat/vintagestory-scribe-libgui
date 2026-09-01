using System;
using System.Collections.Generic;
using System.Linq;
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle, FontWeight
using Gui.Widgets.Basic;         // Text, Button, ButtonVariant, Container
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, Theme, ColorScheme
using Gui.Widgets.Gestures;      // ScrollController
using Gui.Widgets.Input;         // Checkbox, Dropdown, DropdownItem
using Gui.Widgets.Layout;        // Column, Row, Padding, Expanded, CrossAxisAlignment
using Gui.Widgets.Painting;      // BoxStyle, BoxShadow
using Gui.Core.Layout;           // MainAxisSize
using OpenTK.Mathematics;        // Vector2, Vector4
using Scribe.Core;
using Vintagestory.API.Config;   // Lang

namespace Scribe;

/// <summary>
/// The Assignment Desk's Create Assignments tab (assignment-multi-item-creation): a staging slot for an
/// existing Scribe item, that item's rows rendered Read-view-style with independent Selected checkboxes
/// (<see cref="ScribeAssignmentStageContent"/>), a "Delete from source on send" toggle, and the
/// target-player picker + batch-Send button. Staging-and-select ONLY (refine-assignment-desk-inbox-ux
/// 12.1) — the Sent history this class used to render below a divider moved to its own Sent Assignment
/// History tab (<see cref="ScribeDialogBase.BuildSentAssignmentHistoryContent"/>, 12.2/12.3), so this tab
/// reads as one thing (create) instead of create-plus-a-read-only-history-view bolted underneath it.
///
/// <para>Replaces the freeform task-text field + single-item Send button this class held before
/// (refine-assignment-desk-inbox-ux tasks.md group 7/9.5) — creation now happens by delegating existing,
/// already-authored rows rather than typing a bare checkbox task.</para>
/// </summary>
internal sealed class ScribeAssignmentFormContent : StatefulWidget
{
    public ScribeAssignmentFormContent(
        IReadOnlyList<(string Uid, string Name)> targetPlayers,
        Widget stagingSlot,
        IReadOnlyList<ScribeReadRowData> stagedRows,
        ISet<Guid> selectedTaskIds,
        Action<Guid> onToggleSelected,
        bool deleteFromSource,
        Action<bool> onToggleDeleteFromSource,
        Action<string> onSendBatch,
        bool sending,
        bool canPullFromDesk,
        Action onPullFromDesk,
        ScribeRowStyle style,
        ScrollController scrollController,
        Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        TargetPlayers = targetPlayers;
        StagingSlot = stagingSlot;
        StagedRows = stagedRows;
        SelectedTaskIds = selectedTaskIds;
        OnToggleSelected = onToggleSelected;
        DeleteFromSource = deleteFromSource;
        OnToggleDeleteFromSource = onToggleDeleteFromSource;
        OnSendBatch = onSendBatch;
        Sending = sending;
        CanPullFromDesk = canPullFromDesk;
        OnPullFromDesk = onPullFromDesk;
        Style = style;
        ScrollController = scrollController;
    }

    /// <summary>Every other online player, as (uid, display name) — the target-player picker's options.
    /// An offline player can't be targeted (client has no UID→name directory for them); documented MVP
    /// scope, not a hidden limitation.</summary>
    public IReadOnlyList<(string Uid, string Name)> TargetPlayers { get; }
    /// <summary>The dialog-built staging slot widget (owns the SlotController; kept opaque here so this
    /// class stays free of LibGUI Inventory-widget/SlotController concerns).</summary>
    public Widget StagingSlot { get; }
    /// <summary>The staged item's rows, empty when nothing is staged or the staged item has no readable
    /// document.</summary>
    public IReadOnlyList<ScribeReadRowData> StagedRows { get; }
    public ISet<Guid> SelectedTaskIds { get; }
    public Action<Guid> OnToggleSelected { get; }
    /// <summary>UI-only session state (design.md D13) — the dialog owns it, resetting to false on every
    /// tab (re)open; never a saved preference.</summary>
    public bool DeleteFromSource { get; }
    public Action<bool> OnToggleDeleteFromSource { get; }
    /// <summary>Send every selected staged row as its own independent assignment to the resolved target
    /// player uid.</summary>
    public Action<string> OnSendBatch { get; }
    /// <summary>True while the "Submitted to Player" stamp is playing over the staging slot — disables the
    /// Send button for the animation's duration (refine-assignment-desk-inbox-ux 10.4) so a second tap
    /// can't queue another send before the first one's flourish has finished.</summary>
    public bool Sending { get; }
    /// <summary>Whether the Desk's own document has an eligible task to pull in (add-assignment-desk-own-
    /// tasks design.md D3) — gates the empty-state's "pull from Desk" button.</summary>
    public bool CanPullFromDesk { get; }
    /// <summary>Activates the Desk's own document as this tab's task source (design.md D3).</summary>
    public Action OnPullFromDesk { get; }
    public ScribeRowStyle Style { get; }
    public ScrollController ScrollController { get; }

    public override State CreateState() => new ScribeAssignmentFormContentState();
}

internal sealed class ScribeAssignmentFormContentState : State<ScribeAssignmentFormContent>
{
    private string? selectedTargetUid;

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        var style = Widget.Style;
        var players = Widget.TargetPlayers;

        // Keep the selection valid across a players-list change (someone logged off/on since the last build).
        if (selectedTargetUid is null || !players.Any(p => p.Uid == selectedTargetUid))
            selectedTargetUid = players.Count > 0 ? players[0].Uid : null;

        Widget playerPicker = players.Count == 0
            ? new Text(Lang.Get("scribe:scribe-assignment-no-players"),
                new TextStyle { FontSize = style.FontSize, Color = colors.OnSurfaceVariant })
            : new Dropdown<string>(
                value: selectedTargetUid!,
                items: players.Select(p => new DropdownItem<string> { Value = p.Uid, Label = p.Name }).ToList(),
                onChanged: v => SetState(() => selectedTargetUid = v),
                style: Theme.Of(context).DropdownStyle);

        bool canSend = !Widget.Sending && selectedTargetUid is not null && Widget.SelectedTaskIds.Count > 0;

        Widget sendButton = new Button(
            child: new Text(Lang.Get("scribe:scribe-assignment-send"),
                new TextStyle { FontSize = 14, Color = colors.OnPrimary, FontFamily = ScribeTaskFont.ButtonFamily }),
            variant: ButtonVariant.Primary,
            enabled: canSend,
            onTap: canSend ? _ => Widget.OnSendBatch(selectedTargetUid!) : null);

        // "Send to" row as a LibGUI flex Row (refine-assignment-desk-inbox-ux 7.1 / vslibgui wiki's
        // Layout pattern): a fixed-width label, the player picker taking the flexible remaining space,
        // and the fixed-width Send button.
        Widget sendToRow = new Row(
            crossAxisAlignment: CrossAxisAlignment.Center,
            spacing: 8f,
            children: new Widget[]
            {
                new Text(Lang.Get("scribe:scribe-assignment-target-label"),
                    new TextStyle { FontSize = style.FontSize * 0.85f, Color = colors.OnSurfaceVariant }),
                new Expanded(flex: 1, child: playerPicker),
                sendButton,
            });

        Widget deleteFromSourceRow = new Row(
            spacing: 8f,
            mainAxisSize: MainAxisSize.Min,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children: new Widget[]
            {
                new Checkbox(
                    value: Widget.DeleteFromSource,
                    onChanged: v => Widget.OnToggleDeleteFromSource(v),
                    size: style.CheckboxSize),
                new Text(Lang.Get("scribe:scribe-assignment-delete-from-source"),
                    new TextStyle { FontSize = style.FontSize * 0.85f, Color = colors.OnSurfaceVariant }),
            });

        Widget stagingArea = new Row(
            spacing: 12f,
            crossAxisAlignment: CrossAxisAlignment.Start,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                Widget.StagingSlot,
                new Text(Lang.Get("scribe:scribe-assignment-stage-hint"),
                    new TextStyle { FontSize = style.FontSize * 0.85f, Color = colors.OnSurfaceVariant, SoftWrap = true }),
            });

        // Inscribed in a rounded box with a slight inner glow emulating a shadow (refine-assignment-desk-
        // inbox-ux 12.4) — reads as a recessed "tray" the staged rows sit inside, distinguishing this list
        // from the plain-background rows/controls around it. Corner radius/border echo the staging slot's
        // own rounding; BoxShadow.Inset paints INSIDE the box (see BoxShadow's remarks), which is what
        // reads as a shadow cast INTO the box rather than one the box casts onto the page behind it.
        Widget stageBox = new Container(
            style: new BoxStyle
            {
                Color = colors.SurfaceHigh,
                CornerRadius = new Vector4(8f),
                BorderThickness = 1f,
                BorderColor = colors.Border,
                Padding = EdgeInsets.All(6f),
                BoxShadows = new[]
                {
                    new BoxShadow(Color: colors.OnSurface with { W = 0.35f }, Offset: new Vector2(0f, 2f),
                        BlurRadius: 6f, Inset: true),
                },
            },
            child: new ScribeAssignmentStageContent(
                rows: Widget.StagedRows,
                selectedTaskIds: Widget.SelectedTaskIds,
                onToggleSelected: Widget.OnToggleSelected,
                canPullFromDesk: Widget.CanPullFromDesk,
                onPullFromDesk: Widget.OnPullFromDesk,
                style: style,
                scrollController: Widget.ScrollController));

        var body = new Column(
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Max,
            spacing: 8f,
            children: new Widget[]
            {
                new Text(Lang.Get("scribe:scribe-assignment-form-heading"),
                    new TextStyle { FontSize = style.FontSize * 1.1f, Weight = FontWeight.Bold, Color = colors.OnSurface }),
                stagingArea,
                new Expanded(child: stageBox),
                deleteFromSourceRow,
                sendToRow,
            });

        // Rooted in the same Task Text Font + EdgeInsets.All(10) inset every other tab uses
        // (ScribeReadContent/ScribePinnedContent/the Guestbook/the Timer tab) — this tab used to return its
        // Column bare, so its Divider spanned edge-to-edge instead of sitting inset like theirs (refine-
        // assignment-desk-inbox-ux 11.1).
        return ScribeTextDefaults.Wrap(style.TaskFontFamily, style.FontSize, new Padding(EdgeInsets.All(10), child: body));
    }
}
