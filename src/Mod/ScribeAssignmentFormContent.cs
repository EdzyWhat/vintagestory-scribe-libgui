using System;
using System.Collections.Generic;
using System.Linq;
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle, FontWeight
using Gui.Widgets.Basic;         // Text, Button, ButtonVariant, Divider
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, Theme, ColorScheme
using Gui.Widgets.Gestures;      // ScrollController
using Gui.Widgets.Input;         // TextField, TextFieldStyle, TextEditingController, TextEditingValue, TextSelection, Dropdown, DropdownItem, FocusNode
using Gui.Widgets.Layout;        // Column, Row, Padding, Expanded, CrossAxisAlignment
using Gui.Core.Layout;           // MainAxisSize
using Scribe.Core;
using Vintagestory.API.Config;   // Lang

namespace Scribe;

/// <summary>
/// The Assignment Desk's Assignment tab (add-assignment-and-quest-support §5.5 /
/// <c>assignment-desk-block</c> spec): the mod's sole create-and-send surface — a target-player picker,
/// task-text field, and Send button — followed by the Assigner's own read-only Sent history (design.md
/// Decision 3), rendered by the SAME <see cref="ScribeInboxContent"/> the Inbox tab uses, viewed as
/// <see cref="ScribeAssignmentActor.Assigner"/>, so the two never diverge into a one-off (Decision 5).
/// </summary>
internal sealed class ScribeAssignmentFormContent : StatefulWidget
{
    public ScribeAssignmentFormContent(
        IReadOnlyList<(string Uid, string Name)> targetPlayers,
        IReadOnlyList<ScribeInboxRowData> sentRows,
        Func<string, string> resolvePlayerName,
        Action<string, string> onSend,
        Action<Guid, ScribeAssignmentAction> onAction,
        ScribeRowStyle style,
        ScrollController scrollController,
        Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        TargetPlayers = targetPlayers;
        SentRows = sentRows;
        ResolvePlayerName = resolvePlayerName;
        OnSend = onSend;
        OnAction = onAction;
        Style = style;
        ScrollController = scrollController;
    }

    /// <summary>Every other online player, as (uid, display name) — the target-player picker's options.
    /// An offline player can't be targeted (client has no UID→name directory for them); documented MVP
    /// scope, not a hidden limitation.</summary>
    public IReadOnlyList<(string Uid, string Name)> TargetPlayers { get; }
    /// <summary>This player's own Sent assignments (design.md Decision 3's read-only history).</summary>
    public IReadOnlyList<ScribeInboxRowData> SentRows { get; }
    public Func<string, string> ResolvePlayerName { get; }
    /// <summary>Create and send a new assignment: (targetPlayerUid, taskText).</summary>
    public Action<string, string> OnSend { get; }
    public Action<Guid, ScribeAssignmentAction> OnAction { get; }
    public ScribeRowStyle Style { get; }
    public ScrollController ScrollController { get; }

    public override State CreateState() => new ScribeAssignmentFormContentState();
}

internal sealed class ScribeAssignmentFormContentState : State<ScribeAssignmentFormContent>
{
    private readonly TextEditingController textController = new("");
    private readonly FocusNode textFocusNode = new();
    private string? selectedTargetUid;

    public override void Dispose()
    {
        textFocusNode.Dispose();
        textController.Dispose();
        base.Dispose();
    }

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

        Widget textField = new TextField(
            textController,
            textFocusNode,
            new TextFieldStyle { TextStyle = new TextStyle { FontSize = style.FontSize, Color = colors.OnSurface } },
            onChanged: _ => SetState(() => { })); // repaint so the Send button's enabled state tracks live typing

        bool canSend = selectedTargetUid is not null && !string.IsNullOrWhiteSpace(textController.Text);

        Widget sendButton = new Button(
            child: new Text(Lang.Get("scribe:scribe-assignment-send"),
                new TextStyle { FontSize = 14, Color = colors.OnPrimary, FontFamily = ScribeTaskFont.ButtonFamily }),
            variant: ButtonVariant.Primary,
            enabled: canSend,
            onTap: canSend
                ? _ =>
                {
                    Widget.OnSend(selectedTargetUid!, textController.Text.Trim());
                    SetState(() => textController.Value = new TextEditingValue(string.Empty, TextSelection.Collapsed(0)));
                }
                : null);

        // "Send to" row as a LibGUI flex Row (refine-assignment-desk-inbox-ux 7.1 / vslibgui wiki's
        // Layout pattern): a fixed-width label, the player picker taking the flexible remaining space,
        // and the fixed-width Send button — replacing three separate full-width stacked rows (label /
        // dropdown / button) with one tighter row.
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

        var form = new Column(
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            spacing: 8f,
            children: new Widget[]
            {
                new Text(Lang.Get("scribe:scribe-assignment-form-heading"),
                    new TextStyle { FontSize = style.FontSize * 1.1f, Weight = FontWeight.Bold, Color = colors.OnSurface }),
                sendToRow,
                new Text(Lang.Get("scribe:scribe-assignment-text-label"),
                    new TextStyle { FontSize = style.FontSize * 0.85f, Color = colors.OnSurfaceVariant }),
                textField,
            });

        Widget sentHistory = new ScribeInboxContent(
            rows: Widget.SentRows,
            resolvePlayerName: Widget.ResolvePlayerName,
            onAction: Widget.OnAction,
            style: style,
            scrollController: Widget.ScrollController,
            emptyHintLangKey: "scribe:scribe-assignment-sent-empty");

        return new Column(
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[]
            {
                new Padding(EdgeInsets.Only(bottom: 8f), child: form),
                new Divider(),
                new Padding(
                    EdgeInsets.Symmetric(vertical: 4f),
                    child: new Text(Lang.Get("scribe:scribe-assignment-sent-heading"),
                        new TextStyle { FontSize = style.FontSize * 0.9f, Weight = FontWeight.Bold, Color = colors.OnSurface })),
                new Expanded(child: sentHistory),
            });
    }
}
