using System;
using System.Collections.Generic;
using System.Diagnostics;        // Conditional (DEBUG-only scroll trace)
using System.Linq;
using Gui;                       // GuiDialogBlockEntityBase, WindowConfig
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle, FontWeight
using Gui.Widgets.Basic;         // Text, WindowFrame, VsIcon, Container, Button
using Gui.Widgets.Events;        // PointerEvent
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, Theme, ValueKey, Key
using Gui.Widgets.Input;         // Checkbox, FocusNode, GestureDetector, MouseRegion, Dropdown, DropdownItem
using Gui.Widgets.Inventory;     // ItemStackDisplay (Tracker/Link item icon)
using Gui.Widgets.Gestures;      // ScrollController
using Gui.Widgets.Layout;        // Column, Row, Expanded, Padding, SizedBox, Center, Align, Alignment, CrossAxisAlignment, MainAxisAlignment
using Gui.Widgets.Overlay;       // Tooltip
using Gui.Widgets.Painting;      // BoxStyle
using Gui.Widgets.Scroll;        // ListView, SingleChildScrollView, Scrollable, Scrollbar
using Gui.Core.Layout;           // MainAxisSize
using OpenTK.Mathematics;        // Vector2
using Scribe.Core;
using SkiaSharp;                 // SKBitmap (assigned-task stamp raster)
using Vintagestory.API.Client;
using Vintagestory.API.Common;   // ItemStack (Tracker/Link display item)
using Vintagestory.API.Config;   // Lang, GlobalConstants
using Vintagestory.API.MathTools;  // BlockPos

namespace Scribe;

/// <summary>A value snapshot of one Pin Tab row. A Tracker/Link's icon + name are resolved by the dialog and
/// passed in as <see cref="DisplayStack"/> / <see cref="DisplayName"/>, so the row widget stays API-free.</summary>
internal readonly record struct ScribePinRowData(
    Guid DocId, Guid TaskId, bool Done, string Text,
    ScribeBlockKind Kind = ScribeBlockKind.Task,
    ItemStack? DisplayStack = null, string? DisplayName = null,
    int TargetQuantity = 1, int CurrentQuantity = 0, string? LinkTarget = null, int Depth = 0,
    bool IsAcceptedAssignment = false,
    string? AssignerName = null, string? AssignedDate = null, string? AcceptedDate = null,
    bool IsStaticVsQuestObjective = false, string? ExtraInfo = null)
{
    public bool IsTracker => Kind == ScribeBlockKind.Tracker;
    public bool IsLink => Kind == ScribeBlockKind.Link;
    public bool IsCraft => Kind == ScribeBlockKind.Craft;
    public bool IsQuestObjective => Kind == ScribeBlockKind.QuestObjective;
    /// <summary>Renders an item icon + name instead of an editable text field.</summary>
    public bool IsItemKind => IsTracker || IsLink || IsCraft || IsQuestObjective;
    /// <summary>Kinds with a live have/need counter. Mirrors <see cref="ScribeBlock.IsCarriedCountTracked"/>.</summary>
    public bool IsCarriedCountTracked => IsTracker || IsCraft;
    /// <summary>The row's display label: Craft frames its output name, Tracker/Link show the resolved item
    /// name (their own Text is empty), a Task shows its authored text.</summary>
    public string Label => IsCraft ? Lang.Get("scribe:scribe-gui-craft-row-label", DisplayName ?? Text)
        : IsItemKind ? (DisplayName ?? Text) : Text;
}

/// <summary>
/// The Pin Tab's content tree: the player's pins as an editable, reorderable list plus the completion-policy
/// picker. Uses a NON-virtualized scroll view of ALL rows (like the editor) so every field's focus node
/// stays mounted and the dialog can coordinate cross-row focus/commit.
/// </summary>
internal sealed class ScribePinnedContent : StatefulWidget
{
    public ScribePinnedContent(
        IReadOnlyList<ScribePinRowData> rows,
        IReadOnlyDictionary<Guid, FocusNode> focusNodes,
        Guid? autoFocusTaskId,
        Action<Guid, string> onTextChanged,
        Action<Guid> onCommitText,
        Action<Guid, Guid> onToggleComplete,
        Action<Guid, Guid> onDelete,
        Action<Guid, Guid> onUnpin,
        Action<Guid> onOpenLink,
        Action<int, int> onReorder,
        ScribeCompletionPolicy completionPolicy,
        Action<ScribeCompletionPolicy> onCompletionPolicyChanged,
        EdgeInsets policyPickerPadding,
        ScribeRowStyle style,
        ScrollController scrollController,
        ScribeAnimationRegistry collapseRegistry,
        Action onDepartureSettled,
        ScribeAmbientLightSampler.Shade currentShade,
        System.Func<DropdownStyle, DropdownStyle>? decoratePolicyDropdownStyle = null,
        bool showSubtitleRow = true,
        SKBitmap? assignedStampBitmap = null,
        SKBitmap? externalStampBitmap = null)
    {
        Rows = rows;
        FocusNodes = focusNodes;
        AutoFocusTaskId = autoFocusTaskId;
        OnTextChanged = onTextChanged;
        OnCommitText = onCommitText;
        OnToggleComplete = onToggleComplete;
        OnDelete = onDelete;
        OnUnpin = onUnpin;
        OnOpenLink = onOpenLink;
        OnReorder = onReorder;
        CompletionPolicy = completionPolicy;
        OnCompletionPolicyChanged = onCompletionPolicyChanged;
        PolicyPickerPadding = policyPickerPadding;
        Style = style;
        ScrollController = scrollController;
        CollapseRegistry = collapseRegistry;
        OnDepartureSettled = onDepartureSettled;
        CurrentShade = currentShade;
        DecoratePolicyDropdownStyle = decoratePolicyDropdownStyle;
        ShowSubtitleRow = showSubtitleRow;
        AssignedStampBitmap = assignedStampBitmap;
        ExternalStampBitmap = externalStampBitmap;
    }

    public IReadOnlyList<ScribePinRowData> Rows { get; }
    public IReadOnlyDictionary<Guid, FocusNode> FocusNodes { get; }
    public Guid? AutoFocusTaskId { get; }
    public Action<Guid, string> OnTextChanged { get; }
    public Action<Guid> OnCommitText { get; }
    public Action<Guid, Guid> OnToggleComplete { get; }
    public Action<Guid, Guid> OnDelete { get; }
    public Action<Guid, Guid> OnUnpin { get; }
    /// <summary>Opens a pinned Tracker/Link's target Handbook page; never touches completion.</summary>
    public Action<Guid> OnOpenLink { get; }
    public Action<int, int> OnReorder { get; }
    public ScribeCompletionPolicy CompletionPolicy { get; }
    public Action<ScribeCompletionPolicy> OnCompletionPolicyChanged { get; }
    /// <summary>Inset around the picker header, matching the title row's inset
    /// (<c>left: 10 + 0.04·W, right: 0.04·W</c>); supplied by the dialog, which owns the layout width.</summary>
    public EdgeInsets PolicyPickerPadding { get; }
    public ScribeRowStyle Style { get; }
    /// <summary>Dialog-owned scroll controller shared by all views; NOT disposed here.</summary>
    public ScrollController ScrollController { get; }
    /// <summary>Host-owned collapse controllers for departing rows, passed to the <see cref="ScribeAnimatedList"/>.
    /// The dialog owns its lifetime; NOT disposed here.</summary>
    public ScribeAnimationRegistry CollapseRegistry { get; }
    /// <summary>Fired (deferred) when a departing row's collapse completes, so the dialog can re-clamp the
    /// shared scroll extent — see <see cref="ScribeAnimatedList.OnDepartureSettled"/>.</summary>
    public Action OnDepartureSettled { get; }
    /// <summary>Live illumination shade; the policy-caption tooltip renders in the global Overlay layer,
    /// outside the body's tint wrap, so it needs this to match the body in low light.</summary>
    public ScribeAmbientLightSampler.Shade CurrentShade { get; }
    /// <summary>Optional per-surface restyle of the completion-policy Dropdown; <c>null</c> uses the theme's
    /// default style verbatim.</summary>
    public System.Func<DropdownStyle, DropdownStyle>? DecoratePolicyDropdownStyle { get; }
    /// <summary>Whether <see cref="ScribeTabHeader.Build"/> renders its Row 2 subtitle line.</summary>
    public bool ShowSubtitleRow { get; }
    /// <summary>Full-color assigned-task stamp raster (see <see cref="ScribeAssignedTaskIcon"/>); null falls back
    /// to the plain SVG glyph.</summary>
    public SKBitmap? AssignedStampBitmap { get; }
    /// <summary>External-mod hover-info stamp raster (see <see cref="ScribeExternalInfoIcon"/>); null falls back
    /// to the plain SVG glyph.</summary>
    public SKBitmap? ExternalStampBitmap { get; }

    public override State CreateState() => new ScribePinnedContentState();
}

internal sealed class ScribePinnedContentState : State<ScribePinnedContent>
{
    // Drag state updates via SetState here, NOT the dialog's ForceRebuild, which would unmount the grip
    // mid-drag and drop the pointer capture.
    private int? dragFromIndex;
    private int? dragOverIndex;

    private void OnRowDragStart(int index) => SetState(() => { dragFromIndex = index; dragOverIndex = index; });

    private void OnRowDragOver(int index)
    {
        if (dragFromIndex is null || dragOverIndex == index) return;
        SetState(() => dragOverIndex = index);
    }

    private void OnRowDragEnd()
    {
        // Always repaint on release: a from==to OnReorder triggers no rebuild, so without SetState the
        // source row would stay dimmed with a ◀ handle.
        int? from = dragFromIndex;
        int? to = dragOverIndex;
        SetState(() => { dragFromIndex = null; dragOverIndex = null; });
        if (from is { } f && to is { } t)
        {
            Widget.OnReorder(f, t); // no-op inside if from == to
        }
    }

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        TextStyle labelStyle = new() { FontSize = 14, Color = colors.OnSurface };

        // Rows go through ScribeAnimatedList so a removed pin collapses out instead of snapping. The container
        // decides row order (live rows + collapsing ghosts); our layoutBuilder keeps the Pin Tab's own scroll
        // shape. Immediate policy: removals here are affirmative choices, so there is no undo window.
        //
        // Live rows are TaskId-keyed so field state (caret, unsaved buffer) survives a resync. A departing
        // row uses a static ScribeFrozenEditorRow ghost because a live row's checkbox/field/gestures would
        // stay live mid-collapse and its focus node is gone. Pinned:false = no resting tint.
        // The drop-target arrow mirrors the validity check ScribePinOrdering.Reorder's commit uses,
        // excluding the dragged pin's own cluster (dropping on your own pinned children is a no-op).
        List<int>? dragDepths = null;
        (int ClusterStart, int ClusterEnd) dragCluster = default;
        if (dragFromIndex is { } dragFrom)
        {
            dragDepths = Widget.Rows.Select(r => r.Depth).ToList();
            dragCluster = ScribeReorderValidity.Cluster(dragDepths, dragFrom);
        }
        bool IsValidDropTarget(int index)
        {
            if (dragDepths is null) return false;
            if (index >= dragCluster.ClusterStart && index < dragCluster.ClusterEnd) return false; // own cluster: no-op
            return ScribeReorderValidity.IsValidDropTarget(dragDepths, dragCluster.ClusterStart, dragCluster.ClusterEnd, index);
        }

        var items = Widget.Rows
            .Select((r, i) => new ScribeAnimatedListItem(
                Id: r.TaskId,
                Child: new ScribePinRow(
                    data: r,
                    index: i,
                    focusNode: Widget.FocusNodes.TryGetValue(r.TaskId, out var fn) ? fn : null,
                    autoFocus: Widget.AutoFocusTaskId == r.TaskId,
                    isDropTarget: dragFromIndex is not null && dragOverIndex == i && IsValidDropTarget(i),
                    isDragSource: dragFromIndex == i,
                    dragActive: dragFromIndex is not null,
                    onTextChanged: Widget.OnTextChanged,
                    onCommitText: Widget.OnCommitText,
                    onToggleComplete: Widget.OnToggleComplete,
                    onDelete: Widget.OnDelete,
                    onUnpin: Widget.OnUnpin,
                    onOpenLink: Widget.OnOpenLink,
                    onDragStart: OnRowDragStart,
                    onDragOver: OnRowDragOver,
                    onDragEnd: OnRowDragEnd,
                    style: Widget.Style,
                    assignedStampBitmap: Widget.AssignedStampBitmap,
                    externalStampBitmap: Widget.ExternalStampBitmap,
                    currentShade: Widget.CurrentShade,
                    // Keyed by TaskId, not index, so identity tracks the pin across reorder/resync.
                    key: new ValueKey<Guid>(r.TaskId)),
                Ghost: new ScribeFrozenEditorRow(
                    // Snapshots the real Kind + resolved item fields so a removed Tracker/Link collapses
                    // showing its icon + name rather than a blank task row.
                    new ScribeEditRowData(Index: i, Kind: r.Kind, Done: r.Done, Pinned: false, TaskId: r.TaskId,
                        Text: r.Text, DisplayStack: r.DisplayStack, DisplayName: r.DisplayName,
                        TargetQuantity: r.TargetQuantity, CurrentQuantity: r.CurrentQuantity, LinkTarget: r.LinkTarget,
                        Depth: r.Depth, IsAcceptedAssignment: r.IsAcceptedAssignment,
                        AssignerName: r.AssignerName, AssignedDate: r.AssignedDate, AcceptedDate: r.AcceptedDate,
                        IsStaticVsQuestObjective: r.IsStaticVsQuestObjective, ExtraInfo: r.ExtraInfo),
                    Widget.Style, Widget.AssignedStampBitmap, Widget.ExternalStampBitmap)))
            .ToList();

        Widget scrollBody = new ScribeAnimatedList(
            items: items,
            registry: Widget.CollapseRegistry,
            policy: ScribeListRemovalPolicy.Immediate,
            onDepartureSettled: Widget.OnDepartureSettled,
            // Empty-state prompt only when no live rows AND no ghost still collapsing, so it doesn't pop
            // in before the last removed row finishes collapsing.
            layoutBuilder: rows => rows.Count == 0
                ? new Padding(
                    EdgeInsets.All(12),
                    child: new Text(
                        Lang.Get("scribe:scribe-gui-pintab-empty"),
                        new TextStyle { FontSize = 14, Color = colors.OnSurfaceVariant, SoftWrap = true }))
                // Top padding is inside the scroll region so the divider above sits flush with the viewport.
                : new Scrollbar(
                    controller: Widget.ScrollController,
                    child: new SingleChildScrollView(
                        controller: Widget.ScrollController,
                        child: new Padding(EdgeInsets.Only(top: 8f), child: new Column(
                            spacing: 0,
                            crossAxisAlignment: CrossAxisAlignment.Stretch,
                            mainAxisSize: MainAxisSize.Min,
                            children: rows))))
                { AutoHide = false });

        // Completion-policy picker: the same shared per-player preference the Settings window edits. The
        // caption scales with the window text size (derived from the row Style) and carries the same hover
        // helptext as Settings.
        float scale = Widget.Style.FontSize / ScribeRowConstants.BaseWindowFontSize;
        string taskFont = ScribeTaskFont.Resolve(Widget.Style.TaskFontFamily);
        // Font and the smaller 13*scale size are explicit rather than inherited from the tab's DefaultTextStyle.
        Widget policyCaption = new Text(Lang.Get("scribe:settings-completionpolicy"),
            new TextStyle
            {
                FontSize = ScribeTaskFont.LayoutSize(Widget.Style.TaskFontFamily, 13 * scale),
                Color = colors.OnSurfaceVariant,
                FontFamily = ScribeTaskFont.ButtonFamily,
            });
        policyCaption = ScribeGlobalTint.ShadedTooltip(
            child: policyCaption,
            // Shades bubble + text like the shared WithTooltip helper. Renders OUTSIDE the tab subtree, so
            // the DefaultTextStyle ancestor does not reach it: it needs an explicit task font.
            content: new Padding(
                EdgeInsets.All(6),
                child: new Text(
                    Lang.Get("scribe:settings-completionpolicy-help"),
                    new TextStyle
                    {
                        FontSize = ScribeTaskFont.LayoutSize(Widget.Style.TaskFontFamily, 13 * scale),
                        Color = colors.OnSurface,
                        SoftWrap = true,
                        FontFamily = taskFont,
                    })),
            baseTheme: Theme.Of(context),
            shade: Widget.CurrentShade);

        // Swap Caudex onto the dropdown's shared TextStyle (trigger label and menu items). Explicit because the
        // popup menu renders in a global overlay outside the DefaultTextStyle subtree.
        var dropdownStyle = Theme.Of(context).DropdownStyle;
        dropdownStyle = dropdownStyle with { TextStyle = dropdownStyle.TextStyle with { FontFamily = ScribeTaskFont.ButtonFamily } };
        // Applied AFTER the font tweak so the surface override composes on top.
        if (Widget.DecoratePolicyDropdownStyle is { } decorate) dropdownStyle = decorate(dropdownStyle);

        var policyPicker = new Column(
            spacing: 4,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                policyCaption,
                new Dropdown<ScribeCompletionPolicy>(
                    value: Widget.CompletionPolicy,
                    // Display order must stay in sync with the Settings picker.
                    items: new List<DropdownItem<ScribeCompletionPolicy>>
                    {
                        new() { Value = ScribeCompletionPolicy.Keep,      Label = Lang.Get("scribe:scribe-completion-keep") },
                        new() { Value = ScribeCompletionPolicy.Sink,      Label = Lang.Get("scribe:scribe-completion-sink") },
                        new() { Value = ScribeCompletionPolicy.Unpin,     Label = Lang.Get("scribe:scribe-completion-unpin") },
                        new() { Value = ScribeCompletionPolicy.UnpinSink, Label = Lang.Get("scribe:scribe-completion-unpinsink") },
                        new() { Value = ScribeCompletionPolicy.Delete,    Label = Lang.Get("scribe:scribe-completion-delete") },
                    },
                    onChanged: v => Widget.OnCompletionPolicyChanged(v),
                    style: dropdownStyle),
            });

        // Shared subtitle row + the policy picker (inset to match the title row) + the divider.
        Widget header = ScribeTabHeader.Build(colors, Widget.Style,
            "scribe:scribe-gui-nav-pinned", "scribe:scribe-gui-subtitle-pinned",
            Widget.ShowSubtitleRow,
            new Padding(Widget.PolicyPickerPadding, child: policyPicker));

        // Root the subtree in the player's task font + window-scaled size. The policy tooltip, dropdown menu and
        // ScribeMultilineField render outside it or don't inherit, so they keep explicit fonts.
        return ScribeTextDefaults.Wrap(Widget.Style.TaskFontFamily, Widget.Style.FontSize, new Padding(
            EdgeInsets.Ltrb(10, 4, 10, 10),
            child: new Column(
                spacing: 0,
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: new Widget[]
                {
                    header,
                    new Expanded(child: scrollBody),
                })));
    }
}

/// <summary>
/// One Pin Tab row: drag grip + completion checkbox + editable text field, with hover-only unpin and delete
/// buttons on the right (the <see cref="ScribeEditRow"/> shape fed from a pin). Actions are addressed by
/// the pin's <c>(DocId, TaskId)</c>.
/// </summary>
internal sealed class ScribePinRow : StatefulWidget
{
    public ScribePinRow(
        ScribePinRowData data,
        int index,
        FocusNode? focusNode,
        bool autoFocus,
        bool isDropTarget,
        bool isDragSource,
        bool dragActive,
        Action<Guid, string> onTextChanged,
        Action<Guid> onCommitText,
        Action<Guid, Guid> onToggleComplete,
        Action<Guid, Guid> onDelete,
        Action<Guid, Guid> onUnpin,
        Action<Guid> onOpenLink,
        Action<int> onDragStart,
        Action<int> onDragOver,
        Action onDragEnd,
        ScribeRowStyle style,
        SKBitmap? assignedStampBitmap = null,
        SKBitmap? externalStampBitmap = null,
        ScribeAmbientLightSampler.Shade currentShade = default,
        Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        Data = data;
        Index = index;
        FocusNode = focusNode;
        AutoFocus = autoFocus;
        IsDropTarget = isDropTarget;
        IsDragSource = isDragSource;
        DragActive = dragActive;
        OnTextChanged = onTextChanged;
        OnCommitText = onCommitText;
        OnToggleComplete = onToggleComplete;
        OnDelete = onDelete;
        OnUnpin = onUnpin;
        OnOpenLink = onOpenLink;
        OnDragStart = onDragStart;
        OnDragOver = onDragOver;
        OnDragEnd = onDragEnd;
        Style = style;
        AssignedStampBitmap = assignedStampBitmap;
        ExternalStampBitmap = externalStampBitmap;
        CurrentShade = currentShade;
    }

    public ScribePinRowData Data { get; }
    public int Index { get; }
    public FocusNode? FocusNode { get; }
    public bool AutoFocus { get; }
    /// <summary>True while a drag is in progress and this row is the current drop target (grip shows ▶).</summary>
    public bool IsDropTarget { get; }
    /// <summary>True for the row being dragged (grip shows ◀, content dims). Wins over
    /// <see cref="IsDropTarget"/> when the cursor is back over the source row.</summary>
    public bool IsDragSource { get; }
    /// <summary>True while ANY grip-drag is in progress; non-participating rows hide their grip glyph.</summary>
    public bool DragActive { get; }
    public Action<Guid, string> OnTextChanged { get; }
    public Action<Guid> OnCommitText { get; }
    public Action<Guid, Guid> OnToggleComplete { get; }
    public Action<Guid, Guid> OnDelete { get; }
    public Action<Guid, Guid> OnUnpin { get; }
    public Action<Guid> OnOpenLink { get; }
    public Action<int> OnDragStart { get; }
    public Action<int> OnDragOver { get; }
    public Action OnDragEnd { get; }
    public ScribeRowStyle Style { get; }
    /// <summary>See <see cref="ScribePinnedContent.AssignedStampBitmap"/>.</summary>
    public SKBitmap? AssignedStampBitmap { get; }
    /// <summary>See <see cref="ScribePinnedContent.ExternalStampBitmap"/>.</summary>
    public SKBitmap? ExternalStampBitmap { get; }
    public ScribeAmbientLightSampler.Shade CurrentShade { get; }

    public override State CreateState() => new ScribePinRowState();
}

internal sealed class ScribePinRowState : State<ScribePinRow>
{
    private bool done;
    /// <summary>True while the pointer is over this row; gates delete/unpin. The grip is NOT hover-gated so a
    /// drag it started can't lose pointer capture mid-move.</summary>
    private bool hovered;

    public override void InitState()
    {
        base.InitState();
        done = Widget.Data.Done;
    }

    /// <summary>Resyncs the optimistic <see cref="done"/> when the authoritative completion flips externally
    /// (a server re-push reuses this keyed row, so the <see cref="InitState"/> seed would go stale). Gated on
    /// the value actually changing so a chrome-only reconcile doesn't stomp an in-flight optimistic tick.</summary>
    public override void UpdateWidget(ScribePinRow oldWidget)
    {
        base.UpdateWidget(oldWidget);
        if (oldWidget.Data.Done != Widget.Data.Done) done = Widget.Data.Done;
    }

    /// <summary>Item icon + name (a hyperlink via <see cref="ScribePinRow.OnOpenLink"/>), plus a left-hand
    /// have/need counter for a Tracker. Must match <c>ScribeReadRowState.BuildItemContent</c> so all views
    /// render these rows identically.</summary>
    private Widget BuildItemContent(ColorScheme colors, ScribeRowStyle style)
    {
        var data = Widget.Data;
        float iconSize = ScribeRowConstants.ItemIconSize
            * (style.ControlSize / ScribeRowConstants.RowCheckboxSize);
        float lineHeight = ScribeRowControlNudge.TextLineHeight(style.FontSize);
        // A Quest Link's icon renders in the leading slot, so this inline slot has none.
        bool isQuestLink = ScribeLinkTarget.IsQuest(data.LinkTarget);
        float bandHeight = ScribeRowControlNudge.ItemVisualBandHeight(
            style, data.LinkTarget, data.IsStaticVsQuestObjective, data.DisplayStack);
        // Primary, or the row's LinkColor override where Primary is illegible on a dark surface. A Quest Link
        // uses QuestLinkColor so it reads distinct.
        Vector4 linkColor = data.IsStaticVsQuestObjective
            ? colors.OnSurface
            : isQuestLink
            ? style.QuestLinkColor ?? ScribeTheme.QuestLinkAccent
            : style.LinkColor ?? colors.Primary;
        Widget icon = data.IsStaticVsQuestObjective
            ? ScribeLinkIcon.BuildObjective(data.DisplayStack, iconSize, colors.OnSurfaceVariant,
                lineHeight, heightNeutral: false)
            : ScribeLinkIcon.Build(data.DisplayStack, data.LinkTarget, iconSize, linkColor,
                lineHeight, heightNeutral: false);
        if (data.IsStaticVsQuestObjective)
            icon = ScribeCenterIfShort.InBand(icon, bandHeight);

        Widget name = ScribeCenterIfShort.Name(
            ScribeItemLabel.Build(data.Label, linkColor, style), style, bandHeight);
        bool canOpen = !data.IsStaticVsQuestObjective || data.DisplayStack is not null;
        Widget nameLink = new Expanded(child: canOpen
            ? new GestureDetector(
                onPress: e => { e.Handled = true; Widget.OnOpenLink(data.TaskId); },
                child: name)
            : name);

        var rowChildren = new List<Widget>();
        if (data.IsLink)
        {
            if (!isQuestLink) rowChildren.Add(icon);
            rowChildren.Add(nameLink);
        }
        else // Tracker or Craft parent: "have / need" counter on the left, then icon + name.
        {
            bool satisfied = data.CurrentQuantity >= data.TargetQuantity;
            rowChildren.Add(ScribeCenterIfShort.InBand(
                ScribeTrackerCounterText.Build(
                    data.CurrentQuantity, data.TargetQuantity, satisfied,
                    strongColor: linkColor, mutedColor: colors.OnSurfaceVariant, lineHeight: lineHeight),
                bandHeight));
            rowChildren.Add(icon);
            rowChildren.Add(nameLink);
        }

        // 4px left inset must match the read item row.
        return new Padding(
            EdgeInsets.Only(left: 4f, right: style.FieldPadX),
            child: new Row(
                spacing: style.CheckboxTextGap,
                crossAxisAlignment: CrossAxisAlignment.Start,
                mainAxisSize: MainAxisSize.Max,
                children: rowChildren));
    }

    public override Widget Build(BuildContext context)
    {
        var data = Widget.Data;
        var style = Widget.Style;
        var colors = Theme.Of(context).ColorScheme;

        var children = new List<Widget>();

        // Grip is always present on the far left. During a drag: source row = ◀, prospective drop row = ▶,
        // any other row = same-size empty box so the column width is unchanged. Source wins over drop target.
        // The tablet supplies its own ink via style.GripGlyphColor.
        Vector4 gripColor = style.GripGlyphColor ?? colors.OnSurfaceVariant;
        Widget gripGlyph =
            Widget.IsDragSource ? new ScribeVsIconGlyph("scribetriangleleft", style.ControlSize, gripColor)
            : Widget.IsDropTarget ? new ScribeVsIconGlyph("scribetriangleright", style.ControlSize, colors.Primary)
            : Widget.DragActive ? new SizedBox(width: style.ControlSize, height: style.ControlSize)
            : new ScribeVsIconGlyph("scribegrip", style.ControlSize, gripColor);

        children.Add(new Padding(
            ScribeRowControlNudge.GripInsets(style, Widget.Data.IsItemKind, Widget.Data.LinkTarget,
                Widget.Data.IsStaticVsQuestObjective, Widget.Data.DisplayStack),
            child: new GestureDetector(
                onPress: _ => Widget.OnDragStart(Widget.Index),
                onRelease: _ => Widget.OnDragEnd(),
                child: gripGlyph)));

        // Dragged row's content dims per-child so the grip keeps full opacity. Always present (1.0/0.5) so
        // no widget swap occurs.
        float contentOpacity = Widget.IsDragSource ? 0.5f : 1f;

        // Completes with no undo delay; flips optimistically and the server re-push reconciles. A pinned note
        // or QuestObjective isn't player-completable (see ScribeBlock.IsCompletable), so its checkbox unpins
        // instead of sending a toggle the server would silently no-op.
        children.Add(new Opacity(contentOpacity, child: new Padding(
            EdgeInsets.Only(top: ScribeRowControlNudge.CheckboxAndGripTop(
                style, data.IsItemKind, data.LinkTarget,
                data.IsStaticVsQuestObjective, data.DisplayStack)),
            // A Quest Link's slot shows the quest-marker icon; a checkbox would be misleading.
            child: ScribeRowControlNudge.BuildLeadingControl(
                context, style, data.LinkTarget, done,
                _ =>
                {
                    if (data.Kind is ScribeBlockKind.Text or ScribeBlockKind.QuestObjective)
                    {
                        Widget.OnUnpin(data.DocId, data.TaskId);
                        return;
                    }
                    SetState(() => done = !done);
                    Widget.OnToggleComplete(data.DocId, data.TaskId);
                }))));

        // Assignment marker, placed like the Read/Editor rows; takes space only for an accepted assignment.
        if (data.IsAcceptedAssignment)
            children.Add(ScribeAssignedTaskIcon.Build(style, colors.OnSurfaceVariant, data.IsItemKind, Widget.AssignedStampBitmap,
                context: context, currentShade: Widget.CurrentShade,
                assignerName: data.AssignerName, assignedDate: data.AssignedDate, acceptedDate: data.AcceptedDate));

        // External-mod hover-info marker; independent of the assignment marker (both can show).
        if (!string.IsNullOrEmpty(data.ExtraInfo))
            children.Add(ScribeExternalInfoIcon.Build(style, colors.OnSurfaceVariant, data.IsItemKind, Widget.ExternalStampBitmap,
                context: context, currentShade: Widget.CurrentShade, extraInfo: data.ExtraInfo));

        // Item kinds render the referenced item (their own Text is empty); otherwise an editable field that
        // buffers on every keystroke (OnTextChanged) and commits in place on Enter/blur (no insert-below here).
        if (data.IsItemKind)
        {
            children.Add(new Expanded(child: new Opacity(contentOpacity, child: BuildItemContent(colors, style))));
        }
        else
        {
            children.Add(new Expanded(child: new Opacity(contentOpacity, child: new ScribeMultilineField(
                initialText: data.Text,
                focusNode: Widget.FocusNode,
                fontSize: style.FontSize,
                fontFamily: ScribeTaskFont.Resolve(style.TaskFontFamily),
                padX: style.FieldPadX,
                padY: style.FieldPadY,
                autoFocus: Widget.AutoFocus,
                maxLength: ScribeDocumentCodec.MaxTaskTextLength,
                // Same focus border as an Edit-view row.
                focusBorderColor: style.InputFocusBorderColor,
                onChanged: text => Widget.OnTextChanged(data.TaskId, text),
                onCommitAndAdvance: () => Widget.OnCommitText(data.TaskId),
                onCommitAndRetreat: () => Widget.OnCommitText(data.TaskId),
                onInsertTaskBelow: () => Widget.OnCommitText(data.TaskId),
                onBlur: () => Widget.OnCommitText(data.TaskId)))));
        }

        // Subtask indent matches the editor/read rows.
        float subtaskIndent = data.Depth > 0 ? style.SubtaskIndent : 0f;
        Widget rowBody = new Padding(
            EdgeInsets.Only(
                left: style.RowHorizontalPadding + subtaskIndent,
                right: style.RowHorizontalPadding,
                top: style.RowVerticalPadding,
                bottom: style.RowVerticalPadding),
            child: new Row(
                spacing: style.CheckboxTextGap,
                crossAxisAlignment: CrossAxisAlignment.Start,
                mainAxisSize: MainAxisSize.Max,
                children: children));

        // No resting tint (every row is pinned). The Container stays ALWAYS present (transparent) so the
        // widget type never swaps mid-drag, which would unmount the field's State.
        rowBody = new Container(
            style: new BoxStyle
            {
                Color = Vector4.Zero,
                BorderColor = Vector4.Zero,
                BorderThickness = 0f,
            },
            child: rowBody);

        // Hover-only buttons: delete right-most (the task itself), unpin to its left (just the pin).
        var stackChildren = new List<Widget> { rowBody };
        if (hovered)
        {
            float btn = style.ControlSize;
            float gap = 4f;
            float boxW = btn - ScribeRowButton.BoxShrink;
            float btnRight = gap + 1f;
            float btnTop = ScribeRowControlNudge.FloatingButtonTop(style, data.IsItemKind);
            stackChildren.Add(new Positioned(
                right: btnRight, top: btnTop,
                child: new ScribeRowButton(
                    iconName: "scribeclose",
                    iconColor: colors.Error,
                    size: btn,
                    onTap: () => Widget.OnDelete(data.DocId, data.TaskId))));
            stackChildren.Add(new Positioned(
                right: btnRight + boxW + gap, top: btnTop,
                child: new ScribeRowButton(
                    iconName: "scribepin",
                    iconColor: colors.Primary,
                    size: btn,
                    onTap: () => Widget.OnUnpin(data.DocId, data.TaskId),
                    iconScale: 1.1f))); // pin glyph +10%
        }

        return new MouseRegion(
            onEnter: _ =>
            {
                if (!hovered) SetState(() => hovered = true);
                Widget.OnDragOver(Widget.Index);
            },
            onExit: _ => { if (hovered) SetState(() => hovered = false); },
            child: new Stack(stackChildren));
    }
}

/// <summary>Top-margin nudges that center a row's shorter controls on a single-line input without moving
/// them when the text wraps. Computed as <c>(inputHeight − controlHeight) / 2</c> (not linear in the font
/// scale), mirroring <c>ScribeMultilineFieldRender</c>'s single-line height formula.</summary>
