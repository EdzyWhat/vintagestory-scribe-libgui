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
    string TargetPlayerUid, string AssignedDate, bool Seen, ScribeAssignmentActor ViewerRole);

/// <summary>One Accept-placement candidate (assignment-state-machine's placement requirement) — an
/// eligible (writeable Scribe document) item's slot identity, exactly what
/// <see cref="ScribeAssignmentActionMessage"/> needs to name the target, plus a display label for the
/// picker shown when more than one candidate exists.</summary>
internal readonly record struct ScribeAcceptCandidate(string InventoryId, int SlotId, string Label);

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
    public ScribeRowStyle Style { get; }
    /// <summary>Dialog-owned scroll controller — NOT disposed here, matching <see cref="ScribeReadContent"/>.</summary>
    public ScrollController ScrollController { get; }
    public string EmptyHintLangKey { get; }

    public override State CreateState() => new ScribeInboxContentState();
}

internal sealed class ScribeInboxContentState : State<ScribeInboxContent>
{
    /// <summary>Which assignment states are currently visible (inbox-tab: "The Inbox tab can filter by
    /// assignment state via a chip row"). Defaults to every state shown — nothing is hidden until the
    /// player narrows it themselves. Lives on this State so it survives a data-only reconcile (the
    /// dialog's <c>RebuildBody</c> on an assignment sync) rather than resetting on every server push.</summary>
    private readonly HashSet<ScribeAssignmentState> activeFilters = new(AllStates());

    private static ScribeAssignmentState[] AllStates() =>
        (ScribeAssignmentState[])Enum.GetValues(typeof(ScribeAssignmentState));

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        var style = Widget.Style;

        var visibleRows = Widget.Rows.Where(r => activeFilters.Contains(r.State)).ToList();

        Widget filterRow = new Wrap(
            spacing: 6f,
            runSpacing: 6f,
            children: AllStates().Select(s => BuildFilterChip(s, colors)).ToList());

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
                                key: new ValueKey<Guid>(r.TaskId)))
                            .ToList())))
            { AutoHide = false };

        return new Column(
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[]
            {
                new Padding(EdgeInsets.Only(bottom: 8f), child: filterRow),
                new Divider(),
                new Expanded(child: new Padding(EdgeInsets.Only(top: 4f), child: list)),
            });
    }

    /// <summary>One toggleable pill per state (inbox-tab: "the active/inactive state of every chip is
    /// visible without opening any additional control") — filled when active, outlined when inactive.</summary>
    private Widget BuildFilterChip(ScribeAssignmentState state, ColorScheme colors)
    {
        bool active = activeFilters.Contains(state);
        var (labelKey, chipColor) = ScribeAssignmentChip.For(state, colors);
        Vector4 bg = active ? chipColor with { W = 1f } : colors.SurfaceHigh with { W = 1f };
        Vector4 fg = active ? ScribeRowConstants.NavActiveGlyph : colors.OnSurfaceVariant;

        return new GestureDetector(
            onTap: _ => SetState(() =>
            {
                if (!activeFilters.Remove(state)) activeFilters.Add(state);
            }),
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

/// <summary>Lang key + thematic color for an assignment state's chip, shared by the filter-chip row and
/// each row's own compact state chip so the two can never disagree (inbox-tab requirements).</summary>
internal static class ScribeAssignmentChip
{
    public static (string LangKey, Vector4 Color) For(ScribeAssignmentState state, ColorScheme colors) => state switch
    {
        ScribeAssignmentState.Unaccepted => ("scribe:scribe-assignment-state-unaccepted", ScribeRowConstants.NavActiveEdit),
        ScribeAssignmentState.Accepted => ("scribe:scribe-assignment-state-accepted", ScribeRowConstants.NavActivePinned),
        ScribeAssignmentState.Declined => ("scribe:scribe-assignment-state-declined", colors.OnSurfaceVariant with { W = 1f }),
        ScribeAssignmentState.Cancelled => ("scribe:scribe-assignment-state-cancelled", colors.OnSurfaceVariant with { W = 1f }),
        ScribeAssignmentState.Discarded => ("scribe:scribe-assignment-state-discarded", colors.OnSurfaceVariant with { W = 1f }),
        ScribeAssignmentState.Completed => ("scribe:scribe-assignment-state-completed", ScribeRowConstants.NavActiveRead),
        _ => ("scribe:scribe-assignment-state-unaccepted", colors.OnSurfaceVariant with { W = 1f }),
    };
}

/// <summary>One Inbox row: a leading chevron (the sole expand/collapse trigger — inbox-tab), the task
/// text, depth indent, and a compact state chip when collapsed; assigner name, in-game date, and any
/// legal state-change action(s) additionally when expanded. Owns its own <c>expanded</c> bool (§7.1),
/// keyed by the assignment's stable TaskId so it survives a data refresh (see
/// <see cref="ScribeInboxContentState"/>'s remarks on reconcile).</summary>
internal sealed class ScribeInboxRow : StatefulWidget
{
    public ScribeInboxRow(ScribeInboxRowData data, Func<string, string> resolvePlayerName,
        Action<Guid, ScribeAssignmentAction> onAction, ScribeRowStyle style,
        Action<Guid, ScribeAcceptCandidate>? onAccept = null,
        IReadOnlyList<ScribeAcceptCandidate>? acceptCandidates = null,
        Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        Data = data;
        ResolvePlayerName = resolvePlayerName;
        OnAction = onAction;
        Style = style;
        OnAccept = onAccept ?? ((_, _) => { });
        AcceptCandidates = acceptCandidates ?? Array.Empty<ScribeAcceptCandidate>();
    }

    public ScribeInboxRowData Data { get; }
    public Func<string, string> ResolvePlayerName { get; }
    public Action<Guid, ScribeAssignmentAction> OnAction { get; }
    public Action<Guid, ScribeAcceptCandidate> OnAccept { get; }
    public IReadOnlyList<ScribeAcceptCandidate> AcceptCandidates { get; }
    public ScribeRowStyle Style { get; }

    public override State CreateState() => new ScribeInboxRowState();
}

internal sealed class ScribeInboxRowState : State<ScribeInboxRow>
{
    private bool expanded;
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
            iconName: expanded ? "scribetriangledown" : "scribetriangleright",
            iconColor: colors.OnSurfaceVariant,
            size: style.ControlSize,
            onTap: () => SetState(() => expanded = !expanded));

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
            new Text(data.Text, new TextStyle { FontSize = style.FontSize, Color = colors.OnSurface, SoftWrap = true }));

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
        if (expanded) children.Add(BuildExpandedDetail(context, data, colors, style));

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
        var meta = new Text(
            Lang.Get("scribe:scribe-assignment-assigned-by", assignerName, data.AssignedDate),
            new TextStyle { FontSize = style.FontSize * 0.85f, Color = colors.OnSurfaceVariant });

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

        var rowChildren = new List<Widget> { meta };
        if (actions.Count > 0)
            rowChildren.Add(new Row(spacing: 8f, mainAxisSize: MainAxisSize.Min, children: actions));

        return new Column(
            crossAxisAlignment: CrossAxisAlignment.Start,
            mainAxisSize: MainAxisSize.Min,
            spacing: 4f,
            children: rowChildren);
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
                child: new Button(
                    child: new Text(Lang.Get("scribe:scribe-assignment-action-accept"),
                        new TextStyle { FontSize = 13, Color = colors.OnSurfaceVariant, FontFamily = ScribeTaskFont.ButtonFamily }),
                    variant: ButtonVariant.Primary,
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
