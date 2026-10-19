using System;
using System.Collections.Generic;
using System.Linq;
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle, FontWeight
using Gui.Widgets.Basic;         // Text, Button, ButtonVariant
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, Theme, ColorScheme, ValueKey, Key
using Gui.Widgets.Input;         // GestureDetector, Dropdown, DropdownItem
using Gui.Widgets.Gestures;      // ScrollController
using Gui.Widgets.Layout;        // Column, Row, Expanded, Padding, SizedBox, Center, CrossAxisAlignment, MainAxisAlignment, Wrap
using Gui.Widgets.Overlay;       // Tooltip
using Gui.Widgets.Painting;      // BoxStyle
using Gui.Widgets.Scroll;        // Scrollbar, SingleChildScrollView
using Gui.Core.Layout;           // MainAxisSize
using OpenTK.Mathematics;        // Vector4
using Scribe.Core;
using Vintagestory.API.Config;   // Lang

namespace Scribe;

/// <summary>One assignment record as rendered by the shared Inbox tab, plus the viewer's role (Assignee for a
/// Received-list row, Assigner for a Sent-list row). A value snapshot, like <see cref="ScribeReadRowData"/>.</summary>
internal readonly record struct ScribeInboxRowData(
    Guid TaskId, string Text, int Depth, ScribeAssignmentState State, string AssignerUid,
    string TargetPlayerUid, string AssignedDate, bool Seen, ScribeAssignmentActor ViewerRole,
    string? DisplayName = null, string? AcceptedDate = null, string? DeclinedDate = null,
    string? CancelledDate = null, string? DiscardedDate = null, string? CompletedDate = null,
    string? AcceptedIntoLabel = null, string? ReceivedDate = null,
    string? RedirectedFromUid = null, string? RedirectedDate = null)
{
    /// <summary>The row's label: <see cref="Text"/> is blank for Tracker/Link/Craft rows (see
    /// <see cref="ScribeAssignmentStore.TryCreate"/>), so <see cref="DisplayName"/> wins when present.</summary>
    public string Label => DisplayName ?? Text;
}

/// <summary>
/// The shared Inbox tab content: an always-visible state filter-chip row above a scrollable list of
/// expand/collapse rows. Used by the standalone Inbox block, the Assignment Desk's Inbox tab, and the
/// nav-opened Inbox view on Lectern/Scriptorium/Chalkboard. Rows have no completion control.
/// </summary>
internal sealed class ScribeInboxContent : StatefulWidget
{
    public ScribeInboxContent(
        IReadOnlyList<ScribeInboxRowData> rows,
        Func<string, string> resolvePlayerName,
        Action<Guid, ScribeAssignmentAction> onAction,
        ScribeRowStyle style,
        ScrollController scrollController,
        string labelLangKey,
        string descriptorLangKey,
        string emptyHintLangKey = "scribe:scribe-gui-inbox-empty",
        Action<Guid, ScribeAcceptCandidate>? onAccept = null,
        IReadOnlyList<ScribeAcceptCandidate>? acceptCandidates = null,
        Action<Guid>? onDelete = null,
        ScribeAssignmentFilterGroup activeFilterGroup = ScribeAssignmentFilterGroup.All,
        Action<ScribeAssignmentFilterGroup>? onFilterGroupChanged = null,
        Func<Guid, bool>? isExpanded = null,
        Action<Guid>? onToggleExpand = null,
        bool showSubtitleRow = true,
        Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        Rows = rows;
        ResolvePlayerName = resolvePlayerName;
        OnAction = onAction;
        Style = style;
        ScrollController = scrollController;
        LabelLangKey = labelLangKey;
        DescriptorLangKey = descriptorLangKey;
        EmptyHintLangKey = emptyHintLangKey;
        OnAccept = onAccept ?? ((_, _) => { });
        AcceptCandidates = acceptCandidates ?? Array.Empty<ScribeAcceptCandidate>();
        OnDelete = onDelete ?? (_ => { });
        ActiveFilterGroup = activeFilterGroup;
        OnFilterGroupChanged = onFilterGroupChanged ?? (_ => { });
        IsExpanded = isExpanded ?? (_ => false);
        OnToggleExpand = onToggleExpand ?? (_ => { });
        ShowSubtitleRow = showSubtitleRow;
    }

    public IReadOnlyList<ScribeInboxRowData> Rows { get; }
    /// <summary>Resolves a player UID to a display name (the dialog passes
    /// <see cref="ScribeModSystem.ResolvePlayerName"/>: live name, then known-players snapshot, then raw UID).</summary>
    public Func<string, string> ResolvePlayerName { get; }
    /// <summary>Requests a Decline/Cancel/Discard transition (Accept uses <see cref="OnAccept"/>, which needs a
    /// placement). The server re-validates via <see cref="Scribe.Core.ScribeAssignmentTransitions"/>; the row
    /// updates on the server's resync.</summary>
    public Action<Guid, ScribeAssignmentAction> OnAction { get; }
    /// <summary>Requests an Accept naming the placement target. Defaults to a no-op; an
    /// <see cref="ScribeAssignmentActor.Assigner"/> view never renders Accept.</summary>
    public Action<Guid, ScribeAcceptCandidate> OnAccept { get; }
    /// <summary>The player's Accept-placement candidates (see <c>ScribeDialogBase.ComputeAcceptCandidates</c>).
    /// Empty disables Accept with a tooltip; more than one shows a picker.</summary>
    public IReadOnlyList<ScribeAcceptCandidate> AcceptCandidates { get; }
    /// <summary>Requests permanent deletion of a terminal-state record. The server re-validates; the row
    /// disappears on its resync. Defaults to a no-op.</summary>
    public Action<Guid> OnDelete { get; }
    /// <summary>The active filter group, dialog-owned (not widget State) so the title-bar expand/collapse-all
    /// toggle computes the same visible rows; taps route through <see cref="OnFilterGroupChanged"/>.</summary>
    public ScribeAssignmentFilterGroup ActiveFilterGroup { get; }
    public Action<ScribeAssignmentFilterGroup> OnFilterGroupChanged { get; }
    /// <summary>Whether the row is expanded; dialog-owned like <see cref="ActiveFilterGroup"/> so the title-bar
    /// toggle can bulk-mutate it.</summary>
    public Func<Guid, bool> IsExpanded { get; }
    public Action<Guid> OnToggleExpand { get; }
    public ScribeRowStyle Style { get; }
    /// <summary>Dialog-owned scroll controller; not disposed here.</summary>
    public ScrollController ScrollController { get; }
    /// <summary>Row 2 subtitle lang keys; supplied by the caller because this widget renders both the Inbox and
    /// Sent History tabs.</summary>
    public string LabelLangKey { get; }
    public string DescriptorLangKey { get; }
    public string EmptyHintLangKey { get; }
    /// <summary>Whether <see cref="ScribeTabHeader.Build"/> renders its Row 2 subtitle line.</summary>
    public bool ShowSubtitleRow { get; }

    public override State CreateState() => new ScribeInboxContentState();
}

internal sealed class ScribeInboxContentState : State<ScribeInboxContent>
{
    private static ScribeAssignmentFilterGroup[] AllGroups() =>
        (ScribeAssignmentFilterGroup[])Enum.GetValues(typeof(ScribeAssignmentFilterGroup));

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        var style = Widget.Style;

        var visibleStates = ScribeAssignmentFilterGroups.StatesFor(Widget.ActiveFilterGroup);
        var visibleRows = Widget.Rows.Where(r => visibleStates.Contains(r.State)).ToList();

        Widget filterRow = new Wrap(
            spacing: 6f,
            runSpacing: 6f,
            children: AllGroups()
                .Select(g => BuildFilterChip(g, colors,
                    Widget.Rows.Count(r => ScribeAssignmentFilterGroups.StatesFor(g).Contains(r.State))))
                .ToList());

        Widget list = visibleRows.Count == 0
            ? new Center(child: new Text(
                Lang.Get(Widget.EmptyHintLangKey),
                new TextStyle { Color = colors.OnSurfaceVariant, SoftWrap = true, Align = TextAlignment.Center }))
            // Top padding is inside the scroll region so the divider sits flush and the gap scrolls away.
            : new Scrollbar(
                controller: Widget.ScrollController,
                child: new SingleChildScrollView(
                    controller: Widget.ScrollController,
                    child: new Padding(EdgeInsets.Only(top: 8f), child: new Column(
                        spacing: 4f,
                        crossAxisAlignment: CrossAxisAlignment.Stretch,
                        mainAxisSize: MainAxisSize.Min,
                        children: visibleRows
                            .Select(r => (Widget)new ScribeInboxRow(
                                r, Widget.ResolvePlayerName, Widget.OnAction, style,
                                onAccept: Widget.OnAccept, acceptCandidates: Widget.AcceptCandidates,
                                onDelete: Widget.OnDelete,
                                expanded: Widget.IsExpanded(r.TaskId),
                                onToggleExpand: () => Widget.OnToggleExpand(r.TaskId),
                                key: new ValueKey<Guid>(r.TaskId)))
                            .ToList()))))
            { AutoHide = false };

        Widget header = ScribeTabHeader.Build(colors, style, Widget.LabelLangKey, Widget.DescriptorLangKey, Widget.ShowSubtitleRow, filterRow);

        // Same Task Text Font and inset as the other tabs; the top inset is tighter (4) under the title bar.
        return ScribeTextDefaults.Wrap(style.TaskFontFamily, style.FontSize, new Padding(
            EdgeInsets.Ltrb(10, 4, 10, 10),
            child: new Column(
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: new Widget[]
                {
                    header,
                    new Expanded(child: list),
                })));
    }

    /// <summary>One radio-style pill per filter group; filled when active. Tapping selects it exclusively.</summary>
    private Widget BuildFilterChip(ScribeAssignmentFilterGroup group, ColorScheme colors, int count)
    {
        bool active = Widget.ActiveFilterGroup == group;
        var (labelKey, chipColor) = ScribeAssignmentFilterGroups.LabelAndColor(group);
        Vector4 bg = active ? chipColor with { W = 1f } : colors.SurfaceHigh with { W = 1f };
        Vector4 fg = active ? ScribeRowConstants.NavActiveGlyph : colors.OnSurfaceVariant;

        string label = group == ScribeAssignmentFilterGroup.All || count == 0
            ? Lang.Get(labelKey)
            : Lang.Get("scribe:scribe-assignment-filter-count", Lang.Get(labelKey), count);

        return new GestureDetector(
            onTap: _ => Widget.OnFilterGroupChanged(group),
            child: new Container(
                style: new BoxStyle
                {
                    Color = bg,
                    CornerRadius = new Vector4(10f),
                    BorderThickness = 1f,
                    BorderColor = colors.Border,
                    Padding = EdgeInsets.Symmetric(horizontal: 10f, vertical: 4f),
                },
                child: new Text(label, new TextStyle { FontSize = 12f, Color = fg, FontFamily = ScribeTaskFont.ButtonFamily })));
    }
}

/// <summary>Lang key + color for an assignment state's per-row chip. The coarser filter pills are in
/// <see cref="ScribeAssignmentFilterGroups"/>.</summary>
internal static class ScribeAssignmentChip
{
    public static (string LangKey, Vector4 Color) For(ScribeAssignmentState state, ColorScheme colors) => state switch
    {
        ScribeAssignmentState.Unaccepted => ("scribe:scribe-assignment-state-unaccepted", ScribeRowConstants.AssignmentChipNew),
        ScribeAssignmentState.Accepted => ("scribe:scribe-assignment-state-accepted", ScribeRowConstants.AssignmentChipAccepted),
        ScribeAssignmentState.Declined => ("scribe:scribe-assignment-state-declined", ScribeRowConstants.AssignmentChipRejected),
        ScribeAssignmentState.Cancelled => ("scribe:scribe-assignment-state-cancelled", ScribeRowConstants.AssignmentChipCancelled),
        ScribeAssignmentState.Discarded => ("scribe:scribe-assignment-state-discarded", ScribeRowConstants.AssignmentChipDiscarded),
        ScribeAssignmentState.Completed => ("scribe:scribe-assignment-state-completed", ScribeRowConstants.AssignmentChipCompleted),
        ScribeAssignmentState.Sent => ("scribe:scribe-assignment-state-sent", ScribeRowConstants.AssignmentChipSent),
        _ => ("scribe:scribe-assignment-state-unaccepted", ScribeRowConstants.AssignmentChipNew),
    };
}

/// <summary>The filter-chip groups, coarser than <see cref="ScribeAssignmentState"/>:
/// <see cref="RejectedGroup"/> combines Declined/Cancelled/Discarded. Exactly one group is active at a time.</summary>
internal enum ScribeAssignmentFilterGroup
{
    All,
    New,
    Accepted,
    RejectedGroup,
    Completed,
}

internal static class ScribeAssignmentFilterGroups
{
    private static readonly ScribeAssignmentState[] RejectedStates =
        { ScribeAssignmentState.Declined, ScribeAssignmentState.Cancelled, ScribeAssignmentState.Discarded };

    /// <summary>The states a row may be in to show while this group is active.</summary>
    public static IReadOnlyCollection<ScribeAssignmentState> StatesFor(ScribeAssignmentFilterGroup group) => group switch
    {
        ScribeAssignmentFilterGroup.All => AllStates,
        ScribeAssignmentFilterGroup.New => new[] { ScribeAssignmentState.Unaccepted },
        ScribeAssignmentFilterGroup.Accepted => new[] { ScribeAssignmentState.Accepted },
        ScribeAssignmentFilterGroup.RejectedGroup => RejectedStates,
        ScribeAssignmentFilterGroup.Completed => new[] { ScribeAssignmentState.Completed },
        _ => AllStates,
    };

    private static readonly ScribeAssignmentState[] AllStates =
        (ScribeAssignmentState[])Enum.GetValues(typeof(ScribeAssignmentState));

    /// <summary>Lang key + swatch color for the pill. <see cref="ScribeAssignmentFilterGroup.RejectedGroup"/> uses
    /// Declined's red rather than Discarded's blended color.</summary>
    public static (string LangKey, Vector4 Color) LabelAndColor(ScribeAssignmentFilterGroup group) => group switch
    {
        ScribeAssignmentFilterGroup.All => ("scribe:scribe-assignment-filter-all", ScribeRowConstants.AssignmentChipAll),
        ScribeAssignmentFilterGroup.New => ("scribe:scribe-assignment-state-unaccepted", ScribeRowConstants.AssignmentChipNew),
        ScribeAssignmentFilterGroup.Accepted => ("scribe:scribe-assignment-state-accepted", ScribeRowConstants.AssignmentChipAccepted),
        ScribeAssignmentFilterGroup.RejectedGroup => ("scribe:scribe-assignment-filter-rejected-group", ScribeRowConstants.AssignmentChipRejected),
        ScribeAssignmentFilterGroup.Completed => ("scribe:scribe-assignment-state-completed", ScribeRowConstants.AssignmentChipCompleted),
        _ => ("scribe:scribe-assignment-filter-all", ScribeRowConstants.AssignmentChipAll),
    };
}

/// <summary>One Inbox row: a leading chevron (the sole expand trigger), task text, depth indent, and state
/// chip; expanded it adds assigner, date, and legal actions. Expansion is dialog-owned
/// (<see cref="Expanded"/>/<see cref="OnToggleExpand"/>); the row is keyed by TaskId so its Accept-picker
/// State survives a data refresh.</summary>
internal sealed class ScribeInboxRow : StatefulWidget
{
    public ScribeInboxRow(ScribeInboxRowData data, Func<string, string> resolvePlayerName,
        Action<Guid, ScribeAssignmentAction> onAction, ScribeRowStyle style,
        Action<Guid, ScribeAcceptCandidate>? onAccept = null,
        IReadOnlyList<ScribeAcceptCandidate>? acceptCandidates = null,
        Action<Guid>? onDelete = null,
        bool expanded = false,
        Action? onToggleExpand = null,
        Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        Data = data;
        ResolvePlayerName = resolvePlayerName;
        OnAction = onAction;
        Style = style;
        OnAccept = onAccept ?? ((_, _) => { });
        AcceptCandidates = acceptCandidates ?? Array.Empty<ScribeAcceptCandidate>();
        OnDelete = onDelete ?? (_ => { });
        Expanded = expanded;
        OnToggleExpand = onToggleExpand ?? (() => { });
    }

    public ScribeInboxRowData Data { get; }
    public Func<string, string> ResolvePlayerName { get; }
    public Action<Guid, ScribeAssignmentAction> OnAction { get; }
    public Action<Guid, ScribeAcceptCandidate> OnAccept { get; }
    public IReadOnlyList<ScribeAcceptCandidate> AcceptCandidates { get; }
    public Action<Guid> OnDelete { get; }
    public bool Expanded { get; }
    public Action OnToggleExpand { get; }
    public ScribeRowStyle Style { get; }

    public override State CreateState() => new ScribeInboxRowState();
}

internal sealed class ScribeInboxRowState : State<ScribeInboxRow>
{
    /// <summary>Selected placement-picker index; on State so it survives a data-only reconcile.</summary>
    private int selectedCandidateIndex;
    /// <summary>Whether the placement picker is revealed (two-step Accept with several candidates).</summary>
    private bool showAcceptPicker;

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        var data = Widget.Data;
        var style = Widget.Style;

        Widget chevron = new ScribeRowButton(
            iconName: Widget.Expanded ? "scribetriangledown" : "scribetriangleright",
            iconColor: colors.OnSurfaceVariant,
            size: style.ControlSize,
            onTap: () => Widget.OnToggleExpand());

        var (chipLangKey, chipColor) = ScribeAssignmentChip.For(data.State, colors);
        Widget chip = new Container(
            style: new BoxStyle
            {
                Color = chipColor with { W = 1f },
                CornerRadius = new Vector4(8f),
                Padding = EdgeInsets.Symmetric(horizontal: 8f, vertical: 2f),
            },
            child: new Text(Lang.Get(chipLangKey),
                new TextStyle { FontSize = style.FontSize * 0.7f, Color = ScribeRowConstants.NavActiveGlyph }));

        Widget textWidget = ScribeTaskFont.OffsetWrap(style.TaskFontFamily, style.FontSize,
            new Text(data.Label, new TextStyle { FontSize = style.FontSize, Color = colors.OnSurface, SoftWrap = true }));

        Widget collapsedRow = new Row(
            crossAxisAlignment: CrossAxisAlignment.Center,
            spacing: style.CheckboxTextGap,
            children: new Widget[]
            {
                new Padding(EdgeInsets.Only(left: data.Depth > 0 ? style.SubtaskIndent : 0f), child: chevron),
                new Expanded(child: textWidget),
                chip,
            });

        var children = new List<Widget> { collapsedRow };
        if (Widget.Expanded) children.Add(BuildExpandedDetail(context, data, colors, style));

        return new Padding(
            EdgeInsets.Symmetric(vertical: style.RowVerticalPadding, horizontal: style.RowHorizontalPadding),
            child: new Column(
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Min,
                spacing: 4f,
                children: children));
    }

    /// <summary>Assigner, dates, and the legal actions for the viewer's role. A terminal state shows metadata
    /// plus only the delete control.</summary>
    private Widget BuildExpandedDetail(BuildContext context, ScribeInboxRowData data, ColorScheme colors, ScribeRowStyle style)
    {
        string assignerName = Widget.ResolvePlayerName(data.AssignerUid);
        var metaStyle = new TextStyle { FontSize = style.FontSize * 0.85f, Color = colors.OnSurfaceVariant };
        var metaLines = new List<Widget>
        {
            new Text(Lang.Get("scribe:scribe-assignment-assigned-by", assignerName, data.AssignedDate), metaStyle),
        };
        // Received: when a Task Notice reached the Assignee's inventory; absent for non-notice assignments.
        if (data.ReceivedDate is { } receivedDate)
            metaLines.Add(new Text(Lang.Get("scribe:scribe-assignment-received-on", receivedDate), metaStyle));
        // AcceptedDate can coexist with a terminal date; the terminal dates are mutually exclusive.
        if (data.AcceptedDate is { } acceptedDate)
            metaLines.Add(new Text(data.AcceptedIntoLabel is { } acceptedIntoLabel
                ? Lang.Get("scribe:scribe-assignment-accepted-into-on", acceptedIntoLabel, acceptedDate)
                : Lang.Get("scribe:scribe-assignment-accepted-on", acceptedDate), metaStyle));
        if (data.DeclinedDate is { } declinedDate)
            metaLines.Add(new Text(Lang.Get("scribe:scribe-assignment-declined-on", declinedDate), metaStyle));
        if (data.CancelledDate is { } cancelledDate)
            metaLines.Add(new Text(Lang.Get("scribe:scribe-assignment-cancelled-on", cancelledDate), metaStyle));
        if (data.DiscardedDate is { } discardedDate)
            metaLines.Add(new Text(Lang.Get("scribe:scribe-assignment-discarded-on", discardedDate), metaStyle));
        if (data.CompletedDate is { } completedDate)
            metaLines.Add(new Text(Lang.Get("scribe:scribe-assignment-completed-on", completedDate), metaStyle));
        // Redirect trace; not gated by ViewerRole, so the redirected-to player sees it too.
        if (data.RedirectedFromUid is { } redirectedFromUid && data.RedirectedDate is { } redirectedDate)
            metaLines.Add(new Text(Lang.Get("scribe:scribe-assignment-redirected-on",
                Widget.ResolvePlayerName(redirectedFromUid), Widget.ResolvePlayerName(data.TargetPlayerUid),
                redirectedDate), metaStyle));

        var actions = new List<Widget>();
        if (data.ViewerRole == ScribeAssignmentActor.Assignee)
        {
            if (data.State == ScribeAssignmentState.Unaccepted)
            {
                actions.Add(BuildAcceptControl(context, data, colors));
                actions.Add(ActionButton("scribe:scribe-assignment-action-decline", ButtonVariant.Danger,
                    () => Widget.OnAction(data.TaskId, ScribeAssignmentAction.Decline), colors));
            }
            else if (data.State == ScribeAssignmentState.Accepted)
            {
                actions.Add(ActionButton("scribe:scribe-assignment-action-discard", ButtonVariant.Danger,
                    () => Widget.OnAction(data.TaskId, ScribeAssignmentAction.Discard), colors));
            }
        }
        else if (data.State == ScribeAssignmentState.Unaccepted) // Assigner: cancel window closes on Accept
        {
            actions.Add(ActionButton("scribe:scribe-assignment-action-cancel", ButtonVariant.Danger,
                () => Widget.OnAction(data.TaskId, ScribeAssignmentAction.Cancel), colors));
        }

        // Terminal-record deletion: the one control a terminal row shows, for either party.
        if (data.State.IsTerminal())
        {
            actions.Add(ActionButton("scribe:scribe-assignment-remove-terminal-record", ButtonVariant.Danger,
                () => Widget.OnDelete(data.TaskId), colors));
        }

        if (actions.Count > 0)
            metaLines.Add(new Row(spacing: 8f, mainAxisSize: MainAxisSize.Min, children: actions));

        return new Column(
            crossAxisAlignment: CrossAxisAlignment.Start,
            mainAxisSize: MainAxisSize.Min,
            spacing: 4f,
            children: metaLines);
    }

    private static Widget ActionButton(string labelKey, ButtonVariant variant, Action onTap, ColorScheme colors)
    {
        Vector4 fg = variant == ButtonVariant.Danger ? colors.OnError : colors.OnPrimary;
        return new Button(
            child: new Text(Lang.Get(labelKey), new TextStyle { FontSize = 13, Color = fg, FontFamily = ScribeTaskFont.ButtonFamily }),
            variant: variant,
            onTap: _ => onTap());
    }

    /// <summary>The Accept control, gated by <see cref="ScribeInboxRow.AcceptCandidates"/>: disabled with a
    /// tooltip when none, a plain button for one, and for several a two-step tap (first reveals the picker
    /// stacked above a confirming Accept; side by side could overflow the narrow content width).</summary>
    private Widget BuildAcceptControl(BuildContext context, ScribeInboxRowData data, ColorScheme colors)
    {
        var candidates = Widget.AcceptCandidates;
        if (candidates.Count == 0)
        {
            return new Tooltip(
                // Secondary, not Primary: LibGUI's Button only dims the label when disabled, so a disabled
                // Primary would still paint its full fill and read as active.
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
                () => Widget.OnAccept(data.TaskId, candidates[0]), colors);

        if (!showAcceptPicker)
            return ActionButton("scribe:scribe-assignment-action-accept", ButtonVariant.Primary,
                () => SetState(() => showAcceptPicker = true), colors);

        int idx = Math.Clamp(selectedCandidateIndex, 0, candidates.Count - 1);
        Widget picker = new Dropdown<int>(
            value: idx,
            items: candidates.Select((c, i) => new DropdownItem<int> { Value = i, Label = c.Label }).ToList(),
            onChanged: v => SetState(() => selectedCandidateIndex = v),
            style: Theme.Of(context).DropdownStyle);
        Widget confirmButton = ActionButton("scribe:scribe-assignment-action-accept", ButtonVariant.Primary,
            () => Widget.OnAccept(data.TaskId, candidates[idx]), colors);
        return new Column(
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            spacing: 4f,
            children: new Widget[] { picker, confirmButton });
    }
}
