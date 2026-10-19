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

/// <summary>One assignment record as rendered by the shared Inbox tab, plus the viewing player's role
/// relative to it (Assignee for a Received-list row, Assigner for a Sent-list row; one shared row shape serves both).
/// A value snapshot, mirroring <see cref="ScribeReadRowData"/>.</summary>
internal readonly record struct ScribeInboxRowData(
    Guid TaskId, string Text, int Depth, ScribeAssignmentState State, string AssignerUid,
    string TargetPlayerUid, string AssignedDate, bool Seen, ScribeAssignmentActor ViewerRole,
    string? DisplayName = null, string? AcceptedDate = null, string? DeclinedDate = null,
    string? CancelledDate = null, string? DiscardedDate = null, string? CompletedDate = null,
    string? AcceptedIntoLabel = null, string? ReceivedDate = null,
    string? RedirectedFromUid = null, string? RedirectedDate = null)
{
    /// <summary>What this row shows: <see cref="Text"/> is blank by convention for a
    /// Tracker/Link/Craft row (its label lives on
    /// <see cref="ScribeBlock.TargetItemCode"/>/<see cref="ScribeBlock.LinkTarget"/> instead — see
    /// <see cref="ScribeAssignmentStore.TryCreate"/>'s remarks), so the row shows <see cref="DisplayName"/>
    /// when present. The caller resolves
    /// <see cref="DisplayName"/> the same way the read view does (<c>ScribeDialogBase.ResolveRowItem</c>)
    /// and this falls back to <see cref="Text"/> when it's null (every Task/Text row).</summary>
    public string Label => DisplayName ?? Text;
}

/// <summary>
/// The shared Inbox tab content: a state
/// filter-chip row, always visible, above a scrollable list of expand/collapse rows. One implementation,
/// shared by the standalone Inbox block, the Assignment Desk's Inbox tab, and the nav-button-opened Inbox
/// view on Lectern/Scriptorium/Chalkboard — none of them fork this widget.
///
/// <para>A row has no completable checkbox/tracker control: it shows only its text, depth indent, and
/// state chip.</para>
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
    /// <summary>Resolves an assigner/assignee UID to a display name for the expanded row's "Assigned by"
    /// line. The dialog passes <see cref="ScribeModSystem.ResolvePlayerName"/>, which prefers the live
    /// online name and falls back to the synced known-players snapshot for an offline-but-previously-known
    /// player, only degrading to the raw UID for a target this client has truly never seen.</summary>
    public Func<string, string> ResolvePlayerName { get; }
    /// <summary>Requests a Decline/Cancel/Discard transition for the given assignment (Accept goes through
    /// <see cref="OnAccept"/> instead — it needs a placement target). The server re-validates the
    /// transition (<see cref="Scribe.Core.ScribeAssignmentTransitions"/>) — this is a request, not a
    /// locally-applied change; the row updates once the server's resync arrives.</summary>
    public Action<Guid, ScribeAssignmentAction> OnAction { get; }
    /// <summary>Requests an Accept transition naming the resolved placement target
    /// (the Accept transition requires a placement). Defaults to a no-op — irrelevant for a
    /// Sent-history view (<see cref="ScribeAssignmentActor.Assigner"/> never renders an Accept control).</summary>
    public Action<Guid, ScribeAcceptCandidate> OnAccept { get; }
    /// <summary>This player's current Accept-placement candidates (held item alone, or every eligible
    /// inventory item when nothing is held) — see <c>ScribeDialogBase.ComputeAcceptCandidates</c>. Empty
    /// disables the Accept control with an explanatory tooltip; more than one shows a picker. Defaults to
    /// empty — irrelevant for a Sent-history view.</summary>
    public IReadOnlyList<ScribeAcceptCandidate> AcceptCandidates { get; }
    /// <summary>Requests permanent deletion of a terminal-state assignment record
    /// The server re-validates the terminal-state and
    /// Assigner-or-Assignee restrictions — this is a request, not a locally-applied change; the row
    /// disappears once the server's resync arrives. Defaults to a no-op.</summary>
    public Action<Guid> OnDelete { get; }
    /// <summary>Which filter-chip group is active — lifted out of this widget's own State so the dialog's title-bar expand/collapse-all toggle can compute "currently
    /// visible rows" the same way this content does. Owned and persisted by the dialog; a filter tap routes
    /// through <see cref="OnFilterGroupChanged"/> rather than a local <c>SetState</c>.</summary>
    public ScribeAssignmentFilterGroup ActiveFilterGroup { get; }
    public Action<ScribeAssignmentFilterGroup> OnFilterGroupChanged { get; }
    /// <summary>Whether the row for the given TaskId is currently expanded — lifted out of each row's own
    /// State into one dialog-owned set, for the same reason as
    /// <see cref="ActiveFilterGroup"/>: the title-bar toggle needs to read and bulk-mutate it.</summary>
    public Func<Guid, bool> IsExpanded { get; }
    public Action<Guid> OnToggleExpand { get; }
    public ScribeRowStyle Style { get; }
    /// <summary>Dialog-owned scroll controller — NOT disposed here, matching <see cref="ScribeReadContent"/>.</summary>
    public ScrollController ScrollController { get; }
    /// <summary>Row 2 subtitle label/descriptor lang keys — this class renders
    /// both the shared Inbox tab AND the Sent Assignment History tab, which are otherwise identical
    /// (same filter-pill row, same list layout), so the caller supplies distinct text for each rather than
    /// this widget guessing which role it's playing.</summary>
    public string LabelLangKey { get; }
    public string DescriptorLangKey { get; }
    public string EmptyHintLangKey { get; }
    /// <summary>Whether <see cref="ScribeTabHeader.Build"/> renders its Row 2 subtitle line, read from
    /// <c>modSystem.VisualTuning.ShowSubtitleRow</c>.</summary>
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
            // The 8-unit top padding lives INSIDE the scroll region so the divider above sits flush
            // against the viewport and this breathing room scrolls away with the content.
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

        // Shared Row 2 subtitle + Row 3 (the filter-pill row) + the divider.
        Widget header = ScribeTabHeader.Build(colors, style, Widget.LabelLangKey, Widget.DescriptorLangKey, Widget.ShowSubtitleRow, filterRow);

        // Rooted in the same Task Text Font + inset every other tab uses (ScribeReadContent/
        // ScribePinnedContent/the Guestbook/the Timer tab), so the Divider sits inset like theirs. The top
        // inset is 4 (a tighter gap between the title bar and the Row 2 subtitle); left/right/bottom are 10.
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

    /// <summary>One radio-button-style pill per filter group — filled when it's the sole active
    /// group, outlined otherwise. Tapping one selects it exclusively.</summary>
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

/// <summary>Lang key + thematic color for an assignment state's chip, shared by every row's own compact
/// state chip. The filter-CHIP row's own labels/colors are a separate, coarser
/// grouping — see <see cref="ScribeAssignmentFilterGroups"/> — which combines three of
/// these states into one filter pill while keeping their per-row chips visually distinct.</summary>
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

/// <summary>The Inbox/Sent-history filter-chip row's groups: coarser than
/// <see cref="ScribeAssignmentState"/> — <see cref="RejectedGroup"/> combines Declined/Cancelled/Discarded
/// into one pill, and <see cref="All"/> is a group with no per-row-chip equivalent. The pill row acts
/// as radio buttons (<see cref="ScribeInboxContentState"/>): exactly one group is visible at a time.</summary>
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

    /// <summary>Every <see cref="ScribeAssignmentState"/> a row must be in to show while this group is
    /// active.</summary>
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

    /// <summary>Lang key + representative swatch color for the pill itself (distinct from any one row's
    /// own state-chip color for <see cref="ScribeAssignmentFilterGroup.RejectedGroup"/>, which spans three
    /// visually-different per-row chips). Uses Declined's red rather than the blended
    /// Discarded color a per-row Discarded chip uses on its own.</summary>
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

/// <summary>One Inbox row: a leading chevron (the sole expand/collapse trigger), the task
/// text, depth indent, and a compact state chip when collapsed; assigner name, in-game date, and any
/// legal state-change action(s) additionally when expanded. Its expanded/collapsed state is lifted
/// out of this widget's own State into <see cref="Expanded"/>/
/// <see cref="OnToggleExpand"/>, owned by the dialog, so the title-bar expand/collapse-all toggle can read
/// and bulk-mutate it. Still keyed by the assignment's stable TaskId so its OTHER State (the Accept
/// picker) survives a data refresh (see <see cref="ScribeInboxContentState"/>'s remarks on reconcile).</summary>
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
    /// <summary>Selected index into <see cref="ScribeInboxRow.AcceptCandidates"/> when more than one
    /// exists (the placement picker). Lives on this State so it survives a data-only reconcile.</summary>
    private int selectedCandidateIndex;
    /// <summary>Whether the placement picker is currently revealed. With more than one candidate, Accept is
    /// a two-step tap: the first tap reveals the picker + a second Accept to confirm, so the picker never
    /// crowds the row's narrow content width.</summary>
    private bool showAcceptPicker;

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        var data = Widget.Data;
        var style = Widget.Style;

        // Leading chevron — the SOLE expand/collapse trigger. Reuses the triangle glyphs registered for
        // the editor's subtask/drag affordances.
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

    /// <summary>Assigner, in-game date, and any legal state-change action(s) for the viewing player's role
    /// A terminal state shows the
    /// metadata but no action row.</summary>
    private Widget BuildExpandedDetail(BuildContext context, ScribeInboxRowData data, ColorScheme colors, ScribeRowStyle style)
    {
        string assignerName = Widget.ResolvePlayerName(data.AssignerUid);
        var metaStyle = new TextStyle { FontSize = style.FontSize * 0.85f, Color = colors.OnSurfaceVariant };
        var metaLines = new List<Widget>
        {
            new Text(Lang.Get("scribe:scribe-assignment-assigned-by", assignerName, data.AssignedDate), metaStyle),
        };
        // Received stub: the moment a Task Notice's Sent-state record actually
        // reached the Assignee's inventory. Precedes AcceptedDate chronologically and can coexist with it
        // (an Accepted/terminal row was Received first) — only absent for a non-notice assignment, which
        // never passes through the Sent/Received states at all.
        if (data.ReceivedDate is { } receivedDate)
            metaLines.Add(new Text(Lang.Get("scribe:scribe-assignment-received-on", receivedDate), metaStyle));
        // Per-transition date stubs. AcceptedDate can coexist with a terminal date (Accepted always
        // precedes Completed/Discarded); the four terminal dates are mutually exclusive by construction.
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
        // Redirect trace: a non-recipient holder's confirmed
        // Accept redirected this record's target — not gated by ViewerRole, so it also shows (harmlessly)
        // in the redirected-to player's own Inbox view of the same record.
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

        // Terminal-record deletion: the ONE control a terminal row
        // shows when expanded, for either party (Assigner or Assignee) — never a state-change action, so
        // it's independent of the ViewerRole/State branches above, which only ever populate Unaccepted/
        // Accepted actions.
        if (data.State.IsTerminal())
        {
            // Visible label (not an icon-only hover control) so it reads consistently with the
            // Accept/Decline/Cancel/Discard buttons above — the row's action area always looks the same
            // shape, just with one button instead of two/none for a terminal record.
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

    /// <summary>The Accept control, gated by <see cref="ScribeInboxRow.AcceptCandidates"/>
    /// (the Accept transition requires a placement): disabled with an explanatory tooltip when
    /// nothing is eligible; a plain Accept button naming the sole candidate when exactly one is (the held
    /// item always wins outright and never reaches the multi-candidate branch — see
    /// <c>ScribeDialogBase.ComputeAcceptCandidates</c>). With more than one candidate, Accept is a TWO-STEP
    /// tap: the first tap only reveals the picker (stacked above a second Accept
    /// that actually confirms) rather than rendering picker + button side by side, which could overflow the
    /// row's width in the Assignment Desk/Inbox's narrow 1:1 content region and leave Accept unreachable.</summary>
    private Widget BuildAcceptControl(BuildContext context, ScribeInboxRowData data, ColorScheme colors)
    {
        var candidates = Widget.AcceptCandidates;
        if (candidates.Count == 0)
        {
            return new Tooltip(
                // Secondary (transparent + bordered), not Primary: LibGUI's Button has no disabled-background
                // variant, only a 45%-opacity dim on the label, so a disabled Primary would still paint its
                // full amber fill and read as active. Secondary's dim label plus transparent fill read as inert.
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
