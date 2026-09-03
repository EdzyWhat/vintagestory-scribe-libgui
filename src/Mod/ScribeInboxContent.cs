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
/// relative to it (Assignee for a Received-list row, Assigner for a Sent-list row — design.md Decision
/// 5: one shared row shape for both, not a divergent one-off). A value snapshot, mirroring
/// <see cref="ScribeReadRowData"/>'s discipline.</summary>
internal readonly record struct ScribeInboxRowData(
    Guid TaskId, string Text, int Depth, ScribeAssignmentState State, string AssignerUid,
    string TargetPlayerUid, string AssignedDate, bool Seen, ScribeAssignmentActor ViewerRole,
    string? DisplayName = null, string? AcceptedDate = null, string? DeclinedDate = null,
    string? CancelledDate = null, string? DiscardedDate = null, string? CompletedDate = null,
    string? AcceptedIntoLabel = null)
{
    /// <summary>What this row actually shows (playtest 2026-08-31 bug fix): a Task/Text row's own
    /// authored <see cref="Text"/> is blank by convention for a Tracker/Link/Craft row (its label lives
    /// on <see cref="ScribeBlock.TargetItemCode"/>/<see cref="ScribeBlock.LinkTarget"/> instead — see
    /// <see cref="ScribeAssignmentStore.TryCreate"/>'s remarks) — this Inbox/Sent-history row previously
    /// rendered <see cref="Text"/> unconditionally and showed those kinds blank. The caller resolves
    /// <see cref="DisplayName"/> the same way the read view does (<c>ScribeDialogBase.ResolveRowItem</c>)
    /// and this falls back to <see cref="Text"/> when it's null (every Task/Text row).</summary>
    public string Label => DisplayName ?? Text;
}

/// <summary>
/// The shared Inbox tab content (add-assignment-and-quest-support §7 / <c>inbox-tab</c> spec): a state
/// filter-chip row, always visible, above a scrollable list of expand/collapse rows. One implementation,
/// shared by the standalone Inbox block, the Assignment Desk's Inbox tab, and the nav-button-opened Inbox
/// view on Lectern/Scriptorium/Chalkboard (§7.5) — none of them fork this widget.
///
/// <para>NOT YET WIRED (§7.1 note): a row has no completable checkbox/tracker control yet, because the
/// assignment store (<see cref="Scribe.Core.ScribeAssignmentStore"/>) doesn't track a Done flag at all —
/// that only exists once the assigned content is placed into the Assignee's own document at Accept time
/// (§9.1, not yet built). Until then a row shows only its text, depth indent, and state chip, per the
/// spec's "if applicable" carve-out.</para>
/// </summary>
internal sealed class ScribeInboxContent : StatefulWidget
{
    public ScribeInboxContent(
        IReadOnlyList<ScribeInboxRowData> rows,
        Func<string, string> resolvePlayerName,
        Action<Guid, ScribeAssignmentAction> onAction,
        ScribeRowStyle style,
        ScrollController scrollController,
        string emptyHintLangKey = "scribe:scribe-gui-inbox-empty",
        Action<Guid, ScribeAcceptCandidate>? onAccept = null,
        IReadOnlyList<ScribeAcceptCandidate>? acceptCandidates = null,
        Action<Guid>? onDelete = null,
        ScribeAssignmentFilterGroup activeFilterGroup = ScribeAssignmentFilterGroup.All,
        Action<ScribeAssignmentFilterGroup>? onFilterGroupChanged = null,
        Func<Guid, bool>? isExpanded = null,
        Action<Guid>? onToggleExpand = null,
        Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        Rows = rows;
        ResolvePlayerName = resolvePlayerName;
        OnAction = onAction;
        Style = style;
        ScrollController = scrollController;
        EmptyHintLangKey = emptyHintLangKey;
        OnAccept = onAccept ?? ((_, _) => { });
        AcceptCandidates = acceptCandidates ?? Array.Empty<ScribeAcceptCandidate>();
        OnDelete = onDelete ?? (_ => { });
        ActiveFilterGroup = activeFilterGroup;
        OnFilterGroupChanged = onFilterGroupChanged ?? (_ => { });
        IsExpanded = isExpanded ?? (_ => false);
        OnToggleExpand = onToggleExpand ?? (_ => { });
    }

    public IReadOnlyList<ScribeInboxRowData> Rows { get; }
    /// <summary>Resolves an assigner/assignee UID to a display name for the expanded row's "Assigned by"
    /// line. Client-side UID→name resolution has no dedicated cache in this mod (unlike the Guestbook,
    /// which stores the name directly at write time) — the dialog passes
    /// <c>capi.World.PlayerByUid(uid)?.PlayerName ?? uid</c>, which resolves correctly for any player the
    /// client has already seen (always true in singleplayer; true in multiplayer once that player has
    /// been online this session) and degrades to the raw UID otherwise rather than showing nothing.</summary>
    public Func<string, string> ResolvePlayerName { get; }
    /// <summary>Requests a Decline/Cancel/Discard transition for the given assignment (Accept goes through
    /// <see cref="OnAccept"/> instead — it needs a placement target). The server re-validates the
    /// transition (<see cref="Scribe.Core.ScribeAssignmentTransitions"/>) — this is a request, not a
    /// locally-applied change; the row updates once the server's resync arrives.</summary>
    public Action<Guid, ScribeAssignmentAction> OnAction { get; }
    /// <summary>Requests an Accept transition naming the resolved placement target
    /// (assignment-state-machine's placement requirement). Defaults to a no-op — irrelevant for a
    /// Sent-history view (<see cref="ScribeAssignmentActor.Assigner"/> never renders an Accept control).</summary>
    public Action<Guid, ScribeAcceptCandidate> OnAccept { get; }
    /// <summary>This player's current Accept-placement candidates (held item alone, or every eligible
    /// inventory item when nothing is held) — see <c>ScribeDialogBase.ComputeAcceptCandidates</c>. Empty
    /// disables the Accept control with an explanatory tooltip; more than one shows a picker. Defaults to
    /// empty — irrelevant for a Sent-history view.</summary>
    public IReadOnlyList<ScribeAcceptCandidate> AcceptCandidates { get; }
    /// <summary>Requests permanent deletion of a terminal-state assignment record
    /// (manage-terminal-assignment-records). The server re-validates the terminal-state and
    /// Assigner-or-Assignee restrictions — this is a request, not a locally-applied change; the row
    /// disappears once the server's resync arrives. Defaults to a no-op.</summary>
    public Action<Guid> OnDelete { get; }
    /// <summary>Which filter-chip group is active — lifted out of this widget's own State (manage-terminal-
    /// assignment-records) so the dialog's title-bar expand/collapse-all toggle can compute "currently
    /// visible rows" the same way this content does. Owned and persisted by the dialog; a filter tap routes
    /// through <see cref="OnFilterGroupChanged"/> rather than a local <c>SetState</c>.</summary>
    public ScribeAssignmentFilterGroup ActiveFilterGroup { get; }
    public Action<ScribeAssignmentFilterGroup> OnFilterGroupChanged { get; }
    /// <summary>Whether the row for the given TaskId is currently expanded — lifted out of each row's own
    /// State (manage-terminal-assignment-records) into one dialog-owned set, for the same reason as
    /// <see cref="ActiveFilterGroup"/>: the title-bar toggle needs to read and bulk-mutate it.</summary>
    public Func<Guid, bool> IsExpanded { get; }
    public Action<Guid> OnToggleExpand { get; }
    public ScribeRowStyle Style { get; }
    /// <summary>Dialog-owned scroll controller — NOT disposed here, matching <see cref="ScribeReadContent"/>.</summary>
    public ScrollController ScrollController { get; }
    public string EmptyHintLangKey { get; }

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
            children: AllGroups().Select(g => BuildFilterChip(g, colors)).ToList());

        Widget list = visibleRows.Count == 0
            ? new Center(child: new Text(
                Lang.Get(Widget.EmptyHintLangKey),
                new TextStyle { Color = colors.OnSurfaceVariant, SoftWrap = true, Align = TextAlignment.Center }))
            : new Scrollbar(
                controller: Widget.ScrollController,
                child: new SingleChildScrollView(
                    controller: Widget.ScrollController,
                    child: new Column(
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
                            .ToList())))
            { AutoHide = false };

        // Rooted in the same Task Text Font + EdgeInsets.All(10) inset every other tab uses
        // (ScribeReadContent/ScribePinnedContent/the Guestbook/the Timer tab) — this tab used to return its
        // Column bare, so its Divider spanned edge-to-edge instead of sitting inset like theirs (refine-
        // assignment-desk-inbox-ux 11.1).
        return ScribeTextDefaults.Wrap(style.TaskFontFamily, style.FontSize, new Padding(
            EdgeInsets.All(10),
            child: new Column(
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: new Widget[]
                {
                    new Padding(EdgeInsets.Only(bottom: 8f), child: filterRow),
                    new Divider(),
                    new Expanded(child: new Padding(EdgeInsets.Only(top: 4f), child: list)),
                })));
    }

    /// <summary>One radio-button-style pill per filter group (inbox-tab: "the active/inactive state of
    /// every chip is visible without opening any additional control") — filled when it's the sole active
    /// group, outlined otherwise. Tapping one selects it exclusively (triage 2026-08-31).</summary>
    private Widget BuildFilterChip(ScribeAssignmentFilterGroup group, ColorScheme colors)
    {
        bool active = Widget.ActiveFilterGroup == group;
        var (labelKey, chipColor) = ScribeAssignmentFilterGroups.LabelAndColor(group);
        Vector4 bg = active ? chipColor with { W = 1f } : colors.SurfaceHigh with { W = 1f };
        Vector4 fg = active ? ScribeRowConstants.NavActiveGlyph : colors.OnSurfaceVariant;

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
                child: new Text(Lang.Get(labelKey), new TextStyle { FontSize = 12f, Color = fg })));
    }
}

/// <summary>Lang key + thematic color for an assignment state's chip, shared by every row's own compact
/// state chip (inbox-tab requirements). The filter-CHIP row's own labels/colors are a separate, coarser
/// grouping — see <see cref="ScribeAssignmentFilterGroups"/> — since triage 2026-08-31 combined three of
/// these six states into one filter pill while keeping their per-row chips visually distinct.</summary>
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
        _ => ("scribe:scribe-assignment-state-unaccepted", ScribeRowConstants.AssignmentChipNew),
    };
}

/// <summary>The Inbox/Sent-history filter-chip row's groups (triage 2026-08-31): coarser than
/// <see cref="ScribeAssignmentState"/> — <see cref="RejectedGroup"/> combines Declined/Cancelled/Discarded
/// into one pill, and <see cref="All"/> is a new group with no per-row-chip equivalent. The pill row acts
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
    /// visually-different per-row chips). Uses Declined's red (glance feedback on 14.10, 2026-08-31: "mark
    /// this complete, but update color of the combined pill to Declined") rather than the blended
    /// Discarded color a per-row Discarded chip still uses on its own.</summary>
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

/// <summary>One Inbox row: a leading chevron (the sole expand/collapse trigger — inbox-tab), the task
/// text, depth indent, and a compact state chip when collapsed; assigner name, in-game date, and any
/// legal state-change action(s) additionally when expanded. Its expanded/collapsed state (§7.1) is lifted
/// out of this widget's own State (manage-terminal-assignment-records) into <see cref="Expanded"/>/
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
    /// <summary>Whether the placement picker is currently revealed (playtest fix 2026-08-30: the picker
    /// used to render unconditionally alongside the Accept button, which could crowd/overflow the row's
    /// narrow content width and leave the Accept button unreachable). Accept is now a two-step tap when
    /// there's more than one candidate: the first tap reveals the picker + a second Accept to confirm.</summary>
    private bool showAcceptPicker;

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        var data = Widget.Data;
        var style = Widget.Style;

        // Leading chevron — the SOLE expand/collapse trigger (inbox-tab: "no other click target on the
        // row ... toggles expand/collapse"). Reuses the existing triangle glyphs registered for the
        // editor's subtask/drag affordances rather than a new asset.
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
    /// (inbox-tab: "An expanded row shows assigner, date, and legal actions"). A terminal state shows the
    /// metadata but no action row.</summary>
    private Widget BuildExpandedDetail(BuildContext context, ScribeInboxRowData data, ColorScheme colors, ScribeRowStyle style)
    {
        string assignerName = Widget.ResolvePlayerName(data.AssignerUid);
        var metaStyle = new TextStyle { FontSize = style.FontSize * 0.85f, Color = colors.OnSurfaceVariant };
        var metaLines = new List<Widget>
        {
            new Text(Lang.Get("scribe:scribe-assignment-assigned-by", assignerName, data.AssignedDate), metaStyle),
        };
        // Per-transition history stubs (triage 2026-08-31: "we should also see stubs for when it was
        // accepted, discarded, etc."). AcceptedDate can coexist with a terminal date (Accepted always
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

        // Terminal-record deletion (manage-terminal-assignment-records): the ONE control a terminal row
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
    /// (assignment-state-machine's placement requirement): disabled with an explanatory tooltip when
    /// nothing is eligible; a plain Accept button naming the sole candidate when exactly one is (the held
    /// item always wins outright and never reaches the multi-candidate branch — see
    /// <c>ScribeDialogBase.ComputeAcceptCandidates</c>). With more than one candidate, Accept is a TWO-STEP
    /// tap (playtest fix 2026-08-30): the first tap only reveals the picker (stacked above a second Accept
    /// that actually confirms) rather than rendering picker + button side by side, which could overflow the
    /// row's width in the Assignment Desk/Inbox's narrow 1:1 content region and leave Accept unreachable.</summary>
    private Widget BuildAcceptControl(BuildContext context, ScribeInboxRowData data, ColorScheme colors)
    {
        var candidates = Widget.AcceptCandidates;
        if (candidates.Count == 0)
        {
            return new Tooltip(
                // Secondary (transparent + bordered), not Primary (triage 2026-08-31: a disabled Primary
                // button still paints its full amber fill — LibGUI's Button has no disabled-background
                // variant, only a 45%-opacity dim on the label — so grey-text-on-solid-amber read as
                // active, not inert). Secondary's own dim label plus its transparent fill together read
                // clearly as "can't tap this" without needing a custom style override.
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
