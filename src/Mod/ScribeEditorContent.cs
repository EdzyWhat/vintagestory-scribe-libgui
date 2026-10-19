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
using OpenTK.Mathematics;        // Vector2, Vector4
using Scribe.Core;
using SkiaSharp;                 // SKBitmap (assigned-task stamp raster)
using Vintagestory.API.Client;
using Vintagestory.API.Common;   // ItemStack (Tracker/Link display item)
using Vintagestory.API.Config;   // Lang, GlobalConstants
using Vintagestory.API.MathTools;  // BlockPos

namespace Scribe;

/// <summary>Value snapshot of one editable block plus its index. The live text lives in the dialog's scratch
/// document, so this only seeds the row. Item kinds show an icon + name resolved by the dialog
/// (<see cref="DisplayStack"/>/<see cref="DisplayName"/>), which keeps this row widget API-free.</summary>
internal readonly record struct ScribeEditRowData(
    int Index, ScribeBlockKind Kind, bool Done, bool Pinned, Guid TaskId, string Text,
    ItemStack? DisplayStack = null, string? DisplayName = null,
    int TargetQuantity = 1, int CurrentQuantity = 0, string? LinkTarget = null, int Depth = 0,
    bool ReadOnly = false, bool CompletionAndPinLive = true, bool IsAcceptedAssignment = false,
    string? AssignerName = null, string? AssignedDate = null, string? AcceptedDate = null,
    bool IsStaticVsQuestObjective = false, string? ExtraInfo = null)
{
    public bool IsTask => Kind == ScribeBlockKind.Task;
    public bool IsTracker => Kind == ScribeBlockKind.Tracker;
    public bool IsLink => Kind == ScribeBlockKind.Link;
    public bool IsCraft => Kind == ScribeBlockKind.Craft;
    public bool IsQuestObjective => Kind == ScribeBlockKind.QuestObjective;
    /// <summary>Kinds that render as an item icon + name (their own Text is empty).</summary>
    public bool IsItemKind => IsTracker || IsLink || IsCraft || IsQuestObjective;
    /// <summary>Kinds with a live have/need counter + target stepper (Tracker, Craft). Excludes QuestObjective,
    /// whose numbers are watcher-driven. Mirrors <see cref="ScribeBlock.IsCarriedCountTracked"/>.</summary>
    public bool IsCarriedCountTracked => IsTracker || IsCraft;
    /// <summary>Kinds that get a completion checkbox; freeform Text and QuestObjective do not.
    /// Mirrors <see cref="ScribeBlock.IsCompletable"/>.</summary>
    public bool Completable => Kind is not ScribeBlockKind.Text and not ScribeBlockKind.QuestObjective;
    /// <summary>The row's display label: Craft frames its output name, item kinds use the resolved item name,
    /// otherwise the authored text.</summary>
    public string Label => IsCraft ? Lang.Get("scribe:scribe-gui-craft-row-label", DisplayName ?? Text)
        : IsItemKind ? (DisplayName ?? Text) : Text;
}

/// <summary>
/// A static, non-interactive snapshot of a deleted row, shown while it collapses out of the list. It mirrors
/// the editor row's column layout so it aligns, but has no field, gestures, or controls; the
/// <see cref="ScribeRowSizeAnimation"/> wrapping it animates it to zero height and removes it.
/// </summary>
internal sealed class ScribeFrozenEditorRow : StatelessWidget
{
    private readonly ScribeEditRowData data;
    private readonly ScribeRowStyle style;
    private readonly SKBitmap? assignedStampBitmap;
    private readonly SKBitmap? externalStampBitmap;

    public ScribeFrozenEditorRow(ScribeEditRowData data, ScribeRowStyle style, SKBitmap? assignedStampBitmap = null,
        SKBitmap? externalStampBitmap = null)
    {
        this.data = data;
        this.style = style;
        this.assignedStampBitmap = assignedStampBitmap;
        this.externalStampBitmap = externalStampBitmap;
    }

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        // Font family + base size inherited from the tab's DefaultTextStyle ancestor.
        TextStyle textStyle = new() { Color = colors.OnSurface, SoftWrap = true };

        var children = new List<Widget>
        {
            // Invisible grip-column spacer (same GripInsets as the editor row) keeps columns aligned.
            new Padding(
                ScribeRowControlNudge.GripInsets(style, data.IsItemKind, data.LinkTarget,
                    data.IsStaticVsQuestObjective, data.DisplayStack),
                child: new Opacity(
                    opacity: 0f,
                    child: new ScribeVsIconGlyph("scribegrip", style.ControlSize, colors.OnSurfaceVariant))),
        };
        if (data.Completable)
        {
            // Disabled checkbox (no onChanged) showing the row's last done-state.
            children.Add(new Padding(
                EdgeInsets.Only(top: ScribeRowControlNudge.CheckboxAndGripTop(
                    style, data.IsItemKind, data.LinkTarget,
                    data.IsStaticVsQuestObjective, data.DisplayStack)),
                child: ScribeRowControlNudge.BuildTaskCheckbox(context, style, data.Done, onChanged: null)));
        }

        // Assignment marker; takes space only for accepted assignments.
        if (data.IsAcceptedAssignment)
            children.Add(ScribeAssignedTaskIcon.Build(style, colors.OnSurfaceVariant, data.IsItemKind, assignedStampBitmap));

        // External-mod hover-info marker; takes space only when present.
        if (!string.IsNullOrEmpty(data.ExtraInfo))
            children.Add(ScribeExternalInfoIcon.Build(style, colors.OnSurfaceVariant, data.IsItemKind, externalStampBitmap));

        // Display-only field renderer (same as Read) so a collapsing Task/Note keeps Edit wrap + line-box.
        // Item kinds stay a padded label — they have no multiline field.
        children.Add(new Expanded(child: data.IsItemKind
            ? new Padding(
                EdgeInsets.Symmetric(vertical: style.FieldPadY, horizontal: style.FieldPadX),
                child: ScribeTaskFont.OffsetWrap(style.TaskFontFamily, style.FontSize,
                    new Text(data.Label, textStyle)))
            : ScribeTaskTextDisplay.Build(data.Label, style, colors.OnSurface)));

        Widget rowBody = new Padding(
            EdgeInsets.Symmetric(vertical: style.RowVerticalPadding, horizontal: style.RowHorizontalPadding),
            child: new Row(
                spacing: style.CheckboxTextGap,
                crossAxisAlignment: CrossAxisAlignment.Start,
                mainAxisSize: MainAxisSize.Max,
                children: children));

        if (data.Pinned)
        {
            rowBody = new Container(
                style: new BoxStyle { Color = ScribeRowConstants.PinnedTint(colors) },
                child: rowBody);
        }

        return rowBody;
    }
}

/// <summary>
/// The editor view's content tree: a NON-virtualized <see cref="SingleChildScrollView"/> + <see cref="Column"/>
/// of ALL rows. A virtualized <see cref="ListView"/> would unmount off-screen rows and destroy their focus
/// nodes, dropping focus/caret when a focused row grows past the viewport. A footer carries the add control
/// and "Done editing".
/// </summary>
internal sealed class ScribeEditorContent : StatefulWidget
{
    public ScribeEditorContent(
        IReadOnlyList<ScribeEditRowData> blocks,
        IReadOnlyList<FocusNode> focusNodes,
        int? autoFocusIndex,
        Action<int, string> onTextChanged,
        Action<int> onCommitAndAdvance,
        Action<int> onCommitAndRetreat,
        Action<int> onInsertTaskBelow,
        Action<int> onRowBlurred,
        Action<int> onMaxLengthReached,
        Action<int> onCaretMoved,
        Action<int> onPointerFocus,
        Action<int> onJumpToFirstRow,
        Action<int> onJumpToLastRow,
        Action<int> onToggleTask,
        Action<int> onDeleteBlock,
        Action<int> onTogglePinned,
        Action<int> onGripTap,
        Action<int, int> onReorderBlock,
        Action<int, int> onTrackerQuantityChanged,
        Action<ScribeAddKind> onAdd,
        IReadOnlyList<ScribeQuestCatalogEntry> questCatalog,
        Action<ScribeQuestCatalogEntry> onAddQuestLink,
        Action onSwitchToRead,
        Action onOpenEditorReference,
        Action? onOpenSettings,
        EdgeInsets footerButtonPadding,
        ScribeRowStyle style,
        ScrollController scrollController,
        ScribeAnimationRegistry collapseRegistry,
        Action onDepartureSettled,
        ScribeAmbientLightSampler.Shade currentShade,
        string hintLangKey = "scribe:scribe-gui-edit-hint",
        bool addTaskEnabled = true,
        bool showSwitchToRead = true,
        System.Action<Guid>? onOpenLink = null,
        bool supportsTabHeader = true,
        bool showSubtitleRow = true,
        SKBitmap? assignedStampBitmap = null,
        SKBitmap? externalStampBitmap = null)
    {
        Blocks = blocks;
        FocusNodes = focusNodes;
        AutoFocusIndex = autoFocusIndex;
        OnTextChanged = onTextChanged;
        OnCommitAndAdvance = onCommitAndAdvance;
        OnCommitAndRetreat = onCommitAndRetreat;
        OnInsertTaskBelow = onInsertTaskBelow;
        OnRowBlurred = onRowBlurred;
        OnMaxLengthReached = onMaxLengthReached;
        OnCaretMoved = onCaretMoved;
        OnPointerFocus = onPointerFocus;
        OnJumpToFirstRow = onJumpToFirstRow;
        OnJumpToLastRow = onJumpToLastRow;
        OnToggleTask = onToggleTask;
        OnDeleteBlock = onDeleteBlock;
        OnTogglePinned = onTogglePinned;
        OnGripTap = onGripTap;
        OnReorderBlock = onReorderBlock;
        OnTrackerQuantityChanged = onTrackerQuantityChanged;
        OnAdd = onAdd;
        QuestCatalog = questCatalog;
        OnAddQuestLink = onAddQuestLink;
        OnSwitchToRead = onSwitchToRead;
        OnOpenEditorReference = onOpenEditorReference;
        OnOpenSettings = onOpenSettings;
        FooterButtonPadding = footerButtonPadding;
        Style = style;
        ScrollController = scrollController;
        CollapseRegistry = collapseRegistry;
        OnDepartureSettled = onDepartureSettled;
        CurrentShade = currentShade;
        HintLangKey = hintLangKey;
        AddTaskEnabled = addTaskEnabled;
        ShowSwitchToRead = showSwitchToRead;
        OnOpenLink = onOpenLink;
        SupportsTabHeader = supportsTabHeader;
        ShowSubtitleRow = showSubtitleRow;
        AssignedStampBitmap = assignedStampBitmap;
        ExternalStampBitmap = externalStampBitmap;
    }

    public IReadOnlyList<ScribeEditRowData> Blocks { get; }
    public IReadOnlyList<FocusNode> FocusNodes { get; }
    public int? AutoFocusIndex { get; }
    public Action<int, string> OnTextChanged { get; }
    public Action<int> OnCommitAndAdvance { get; }
    public Action<int> OnCommitAndRetreat { get; }
    public Action<int> OnInsertTaskBelow { get; }
    /// <summary>A row's field lost focus; the dialog removes the row if it is an empty task.</summary>
    public Action<int> OnRowBlurred { get; }
    /// <summary>A row hit its per-kind character cap; the dialog shows a "limited to N characters" notice.</summary>
    public Action<int> OnMaxLengthReached { get; }
    /// <summary>A row's caret moved by keyboard nav (no text change) — the dialog follows it into view. See <see cref="ScribeDialogBase.NotifyCaretMoved"/>.</summary>
    public Action<int> OnCaretMoved { get; }
    /// <summary>A row was focused by a mouse click — the dialog suppresses scroll-into-view for it. See <see cref="ScribeDialogBase.NotifyPointerFocus"/>.</summary>
    public Action<int> OnPointerFocus { get; }
    /// <summary>Cmd/Ctrl+Up in a row — the dialog jumps focus to the first row, caret at its start. See <see cref="ScribeDialogBase.EditorJumpToFirstRow"/>.</summary>
    public Action<int> OnJumpToFirstRow { get; }
    /// <summary>Cmd/Ctrl+Down in a row — the dialog jumps focus to the last row, caret at its end. See
    /// <see cref="ScribeDialogBase.EditorJumpToLastRow"/>.</summary>
    public Action<int> OnJumpToLastRow { get; }
    public Action<int> OnToggleTask { get; }
    public Action<int> OnDeleteBlock { get; }
    public Action<int> OnTogglePinned { get; }
    /// <summary>A single tap (not a drag) on a row's grip toggles subtask depth 0↔1; the dispatcher suppresses
    /// the tap during a drag.</summary>
    public Action<int> OnGripTap { get; }
    /// <summary>Reorder a block from one index to another (drag drop).</summary>
    public Action<int, int> OnReorderBlock { get; }
    /// <summary>A Tracker stepper changed its target quantity: (blockIndex, newTarget). Persisted by the normal
    /// editor flush.</summary>
    public Action<int, int> OnTrackerQuantityChanged { get; }
    /// <summary>Add a block of the chosen kind (the picker's primary kind, or one picked from its list).</summary>
    public Action<ScribeAddKind> OnAdd { get; }
    /// <summary>The installed quest mod's catalog for the picker's "Quest Link" option; empty hides the option.</summary>
    public IReadOnlyList<ScribeQuestCatalogEntry> QuestCatalog { get; }
    /// <summary>Add a quest Link for the chosen catalog entry.</summary>
    public Action<ScribeQuestCatalogEntry> OnAddQuestLink { get; }
    /// <summary>Whether the add control may add another block. Finite tiers pass false at their entry cap, which
    /// DIMS the buttons; they stay clickable so the tap surfaces the cap notice.</summary>
    public bool AddTaskEnabled { get; }
    public Action OnSwitchToRead { get; }
    /// <summary>Whether the footer shows "Done editing". False for the always-edit tablet, which has no Read
    /// view to land in.</summary>
    public bool ShowSwitchToRead { get; }
    /// <summary>The dialog's illumination shade, threaded so the add-kind picker can re-tint its floating
    /// drop-up menu: the Overlay layer sits OUTSIDE the body's <see cref="ScribeGlobalTint"/> wrap.
    /// Refreshed on every <c>RebuildBody()</c>.</summary>
    public ScribeAmbientLightSampler.Shade CurrentShade { get; }
    /// <summary>Open the "Scribe Editor Features" handbook page; wired from the dialog so this widget stays
    /// free of the VS API.</summary>
    public Action OnOpenEditorReference { get; }
    /// <summary>Open Scribe Settings from a footer gear button. Non-null only for the tablet, which has no nav
    /// column to reach Settings; tabbed dialogs pass null and the gear is omitted.</summary>
    public Action? OnOpenSettings { get; }
    /// <summary>Horizontal padding around the footer button row, supplied by the dialog (which owns the layout
    /// width).</summary>
    public EdgeInsets FooterButtonPadding { get; }
    public ScribeRowStyle Style { get; }
    /// <summary>Dialog-owned scroll controller shared by both views; NOT disposed here so the scroll offset
    /// survives view-switch rebuilds.</summary>
    public ScrollController ScrollController { get; }
    /// <summary>Host-owned collapse controllers (keyed by TaskId) so a collapse resumes across rebuild remounts.
    /// The dialog reads <c>AnyAnimating</c> in <c>OnRenderGUI</c> to drive the scroll-pin + hover latch.</summary>
    public ScribeAnimationRegistry CollapseRegistry { get; }
    /// <summary>Fired (deferred, safe) after a departing row's collapse completes, so the dialog can re-clamp
    /// the scroll extent.</summary>
    public Action OnDepartureSettled { get; }
    public string HintLangKey { get; }
    /// <summary>Optional click-to-open-Handbook dispatch for an item row's name label, keyed by TaskId.
    /// Non-null only on surfaces that opt into link activation (the tablet); null leaves the name a plain
    /// editable region.</summary>
    public System.Action<Guid>? OnOpenLink { get; }
    /// <summary>Whether this surface renders the shared Row 2 subtitle / tab header. False only for the Tablet,
    /// which has its own title-bar chrome; the divider is omitted too.</summary>
    public bool SupportsTabHeader { get; }
    /// <summary>Whether the Row 2 subtitle line renders (from <c>VisualTuning.ShowSubtitleRow</c>); ignored when
    /// <see cref="SupportsTabHeader"/> is false.</summary>
    public bool ShowSubtitleRow { get; }
    /// <summary>The assigned-task stamp raster, resolved by the dialog so this widget stays API-free; null falls
    /// back to the SVG glyph.</summary>
    public SKBitmap? AssignedStampBitmap { get; }
    /// <summary>The external-mod hover-info stamp raster, resolved by the dialog so this widget stays API-free;
    /// null falls back to the SVG glyph.</summary>
    public SKBitmap? ExternalStampBitmap { get; }

    public override State CreateState() => new ScribeEditorContentState();
}

internal sealed class ScribeEditorContentState : State<ScribeEditorContent>
{
    // Drag-reorder state lives in this State and updates via SetState, NOT the dialog's ForceRebuild, which
    // would unmount the grip mid-drag and drop the pointer capture. Both null when no drag is active.
    private int? dragFromIndex;
    private int? dragOverIndex;
    /// <summary>True after a grip drag started this gesture. Survives the remount from
    /// <see cref="OnRowDragStart"/>, so a from==to release still suppresses <see cref="OnGripTap"/>.</summary>
    private bool skipGripTap;

    /// <summary>Grip pressed: reset the tap-suppression flag. A drag starts only after the pointer passes a
    /// movement threshold.</summary>
    private void OnRowGripPress() => skipGripTap = false;

    /// <summary>Grip passed the drag threshold: begin a drag. The dispatcher auto-captures the grip on press,
    /// so moves/release keep arriving here across sibling rows.</summary>
    private void OnRowDragStart(int index)
    {
        skipGripTap = true;
        SetState(() =>
        {
            dragFromIndex = index;
            dragOverIndex = index;
        });
    }

    /// <summary>A tap on the grip (press and release without starting a drag) toggles depth. Once a
    /// drag has started this gesture, skip — including a from==to cancel.</summary>
    private void OnGripTapAttempt(int index)
    {
        if (skipGripTap) { skipGripTap = false; return; }
        Widget.OnGripTap(index);
    }

    /// <summary>The cursor entered a row during a drag. The dispatcher fires enter/exit on the hovered row even
    /// while the grip holds capture, so no manual hit-testing is needed.</summary>
    private void OnRowDragOver(int index)
    {
        if (dragFromIndex is null) return;
        if (dragOverIndex == index) return;
        SetState(() => dragOverIndex = index);
    }

    /// <summary>Grip released: commit the reorder if the drop row differs from the start, then clear
    /// the drag. The actual MoveBlock + rebuild happens in the dialog (OnReorderBlock).</summary>
    private void OnRowDragEnd()
    {
        // ALWAYS repaint on release so the dimmed row and ◀/▶ arrows revert, even on a no-op release
        // (OnReorderBlock's from==to branch does not ForceRebuild).
        int? from = dragFromIndex;
        int? to = dragOverIndex;
        SetState(() => { dragFromIndex = null; dragOverIndex = null; });
        if (from is { } f && to is { } t)
        {
            Widget.OnReorderBlock(f, t); // no-op inside if from == to
        }
    }

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        TextStyle buttonTextStyle = new() { FontSize = 14, Color = colors.OnPrimary, FontFamily = ScribeTaskFont.ButtonFamily };

        // Rows go through ScribeAnimatedList, which diffs the TaskId-keyed set and collapses a deleted row out of
        // a frozen ghost (Immediate policy: delete is an affirmative tap, no undo window). It decides row ORDER and
        // hands the list to our layoutBuilder, which keeps the editor's own Scrollbar > SingleChildScrollView >
        // Column shape.
        //
        // Each item supplies an explicit frozen ghost: a live row can't be frozen in place (its field and gestures
        // would stay live and its focus node is gone). Drag state stays in THIS State and is baked into each row.
        //
        // The drop-target arrow mirrors the validity check ReorderEditorBlock's commit uses, and excludes the
        // dragged row's own cluster (dropping on your own children is a no-op).
        List<int>? dragDepths = null;
        (int ClusterStart, int ClusterEnd) dragCluster = default;
        if (dragFromIndex is { } dragFrom)
        {
            dragDepths = Widget.Blocks.Select(b => b.Depth).ToList();
            dragCluster = ScribeReorderValidity.Cluster(dragDepths, dragFrom);
        }
        bool IsValidDropTarget(int index)
        {
            if (dragDepths is null) return false;
            if (index >= dragCluster.ClusterStart && index < dragCluster.ClusterEnd) return false; // own cluster: no-op
            return ScribeReorderValidity.IsValidDropTarget(dragDepths, dragCluster.ClusterStart, dragCluster.ClusterEnd, index);
        }

        var items = Widget.Blocks
            .Select(b => new ScribeAnimatedListItem(
                Id: b.TaskId,
                Child: new ScribeEditRow(
                    data: b,
                    focusNode: b.Index < Widget.FocusNodes.Count ? Widget.FocusNodes[b.Index] : null,
                    autoFocus: Widget.AutoFocusIndex == b.Index,
                    isDropTarget: dragFromIndex is not null && dragOverIndex == b.Index && IsValidDropTarget(b.Index),
                    // Origin row of the drag: shows the ◀ glyph and dims; wins over the drop-target glyph.
                    isDragSource: dragFromIndex == b.Index,
                    // Any drag in progress → non-source/non-target rows hide their grip glyph.
                    dragActive: dragFromIndex is not null,
                    onTextChanged: Widget.OnTextChanged,
                    onCommitAndAdvance: Widget.OnCommitAndAdvance,
                    onCommitAndRetreat: Widget.OnCommitAndRetreat,
                    onInsertTaskBelow: Widget.OnInsertTaskBelow,
                    onRowBlurred: Widget.OnRowBlurred,
                    onMaxLengthReached: Widget.OnMaxLengthReached,
                    onCaretMoved: Widget.OnCaretMoved,
                    onPointerFocus: Widget.OnPointerFocus,
                    onJumpToFirstRow: Widget.OnJumpToFirstRow,
                    onJumpToLastRow: Widget.OnJumpToLastRow,
                    onToggleTask: Widget.OnToggleTask,
                    onDelete: Widget.OnDeleteBlock,
                    onTogglePinned: Widget.OnTogglePinned,
                    onTrackerQuantityChanged: Widget.OnTrackerQuantityChanged,
                    onDragStart: OnRowDragStart,
                    onDragOver: OnRowDragOver,
                    onDragEnd: OnRowDragEnd,
                    onGripPress: OnRowGripPress,
                    onGripTap: OnGripTapAttempt,
                    // Non-null only on the tablet: item-row names open the Handbook.
                    onOpenLink: Widget.OnOpenLink,
                    style: Widget.Style,
                    assignedStampBitmap: Widget.AssignedStampBitmap,
                    externalStampBitmap: Widget.ExternalStampBitmap,
                    currentShade: Widget.CurrentShade,
                    // Keyed by TaskId, not list index: reconciliation is positional, so a stable key lets an
                    // unshifted slot's row be reused and keep its field State (caret + unsaved buffer) across
                    // RebuildBody(). A delete/insert ABOVE the focused row still remounts it; text survives via
                    // the scratch write-through.
                    key: new ValueKey<Guid>(b.TaskId)),
                Ghost: new ScribeFrozenEditorRow(b, Widget.Style, Widget.AssignedStampBitmap, Widget.ExternalStampBitmap)))
            .ToList();

        // Scrollbar with AutoHide off: permanently visible like the native GUI, and avoids rebuild-driven fade
        // flicker. The empty-hint check is INSIDE layoutBuilder (rows.Count == 0) so the hint doesn't pop in
        // before the last removed row finishes collapsing.
        Widget scrollBody = new ScribeAnimatedList(
            items: items,
            registry: Widget.CollapseRegistry,
            policy: ScribeListRemovalPolicy.Immediate,
            onDepartureSettled: Widget.OnDepartureSettled,
            // Every appearance slides in via the container's uniform motion, holding the row at FULL height so an
            // auto-focused new row keeps its caret visible and clicks exact. The editor only supplies the row set.
            layoutBuilder: rows => rows.Count == 0
                // Font family + size inherited from the tab's DefaultTextStyle; only color and centered-wrap are explicit.
                ? new Center(child: new Text(
                    Lang.Get(Widget.HintLangKey),
                    new TextStyle { Color = colors.OnSurfaceVariant, SoftWrap = true, Align = TextAlignment.Center }))
                : new Scrollbar(
                    controller: Widget.ScrollController,
                    // Top padding lives INSIDE the scroll region so the divider above sits flush; skipped on the
                    // Tablet (no header).
                    child: new SingleChildScrollView(
                        controller: Widget.ScrollController,
                        child: new Padding(EdgeInsets.Only(top: Widget.SupportsTabHeader ? 8f : 0f), child: new Column(
                            // spacing 0: row separation lives in each row's vertical padding, matching the read
                            // ListView so rows stay pixel-aligned across a view switch.
                            spacing: 0,
                            crossAxisAlignment: CrossAxisAlignment.Stretch,
                            mainAxisSize: MainAxisSize.Min,
                            children: rows))))
                { AutoHide = false });

        // Root the tab subtree in the player's Task Text Font + scaled base size so the empty hint and ghost rows
        // inherit them. Live rows use ScribeMultilineField, a custom RenderBox that ignores DefaultTextStyle, so
        // it keeps its own explicit font; footer buttons keep the Caudex button font.
        // Shared Row 2 subtitle + divider; the Tablet renders neither (its title bar is the whole header).
        var bodyChildren = new List<Widget>();
        if (Widget.SupportsTabHeader)
        {
            bodyChildren.Add(ScribeTabHeader.Build(colors, Widget.Style,
                "scribe:scribe-gui-nav-edit", "scribe:scribe-gui-subtitle-edit", Widget.ShowSubtitleRow));
        }
        // The scroll body keeps its exact height: the add-kind picker's menu is a FLOATING drop-up over it, so
        // nothing reflows.
        bodyChildren.Add(new Expanded(child: scrollBody));

        // Top inset 4 tightens the gap to the Row 2 subtitle; the Tablet has no Row 2, so it keeps 10.
        return ScribeTextDefaults.Wrap(Widget.Style.TaskFontFamily, Widget.Style.FontSize, new Padding(
            EdgeInsets.Ltrb(10, Widget.SupportsTabHeader ? 4 : 10, 10, 10),
            child: new Column(
                spacing: 0,
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: bodyChildren.Concat(new Widget[]
                {
                    new Padding(
                        EdgeInsets.Ltrb(Widget.FooterButtonPadding.Left, 8f, Widget.FooterButtonPadding.Right, Widget.FooterButtonPadding.Bottom),
                        child: new Row(
                        spacing: 8,
                        // Center so the non-Expanded icon Button sits vertically centered. NOT Stretch: that gives
                        // it an unbounded axis a greedy child (Center/Align) would balloon to fill the dialog.
                        crossAxisAlignment: CrossAxisAlignment.Center,
                        mainAxisSize: MainAxisSize.Max,
                        children: BuildFooterButtons(buttonTextStyle, colors, context))),
                }).ToList())));
    }

    /// <summary>A footer button's label: cuneiform strokes on the tablet cuneiform branch, else <see cref="Text"/>.
    /// Em size tracks the label font size; ink is the button-label color. A null bundle falls back to the
    /// readable label so a button is never blank.
    ///
    /// <para>Returned DIRECTLY with NO <see cref="Center"/>/<see cref="Align"/> wrapper: a Center takes the
    /// parent's full finite <c>MaxHeight</c> and would balloon the button inside the "Add task"
    /// <see cref="Expanded"/>. Both label widgets self-size.</para></summary>
    private Widget BuildButtonLabel(string label, TextStyle labelStyle)
    {
        if (Widget.Style.UseCuneiform && Widget.Style.CuneiformBundle is { } bundle)
        {
            return new CuneiformText(
                text: label,
                fontSizeEm: labelStyle.FontSize,
                inkColor: labelStyle.Color,
                bundle: bundle,
                // No glow: the halo muddies the label against the solid Primary button.
                glow: default,
                glyphDrawScale: CuneiformMetrics.GlyphDrawScale);
        }
        return new Text(label, labelStyle);
    }

    /// <summary>The footer button row: add picker, optional "Done editing" (omitted on the always-edit tablet),
    /// and the trailing icon buttons.</summary>
    private Widget[] BuildFooterButtons(TextStyle buttonTextStyle, ColorScheme colors, BuildContext context)
    {
        // Cuneiform labels render taller than TTF, so trim the labelled buttons' vertical padding (10 → 6) to
        // keep the footer height; the normal path keeps the default theme padding.
        const float cuneiformLabelPadY = 6f;
        ButtonStyle? labelButtonStyle = Widget.Style.UseCuneiform
            ? Theme.Of(context).ButtonStyle with { Padding = EdgeInsets.Symmetric(cuneiformLabelPadY, 20) }
            : null;

        // Height parity for the trailing icon buttons on the cuneiform path: force the content box to the Add-task
        // label's content height (FontSize × LineHeightRatio) via a fully-bounded SizedBox (tight on both axes so
        // the inner Center can't balloon) with the same vertical padding. The normal path keeps the bare 17f glyph
        // + All(7).
        const float iconGlyphSize = 17f;
        float? iconContentHeight = Widget.Style.UseCuneiform
            ? buttonTextStyle.FontSize * CuneiformMetrics.LineHeightRatio
            : null;
        Widget IconButtonChild(string iconName)
        {
            var glyph = new ScribeVsIconGlyph(iconName, iconGlyphSize, colors.OnPrimary);
            return iconContentHeight is { } h
                // Square, height-locked box centering the glyph.
                ? new SizedBox(width: h, height: h, child: new Center(child: glyph))
                : glyph;
        }
        ButtonStyle iconButtonStyle = Theme.Of(context).ButtonStyle with
        {
            Padding = Widget.Style.UseCuneiform
                ? EdgeInsets.Symmetric(cuneiformLabelPadY, cuneiformLabelPadY)
                : EdgeInsets.All(7),
        };

        var buttons = new List<Widget>
        {
            // Segmented add control; ScribeAddKindPicker owns its selected kind, open state and overlay.
            // AddTaskEnabled dims it at a finite tier's cap, but it stays clickable (the tap raises the cap notice).
            new Expanded(child: new ScribeAddKindPicker(
                onAdd: Widget.OnAdd,
                addTaskEnabled: Widget.AddTaskEnabled,
                style: Widget.Style,
                // The drop-up menu paints in the Overlay, outside the dialog's ScribeGlobalTint wrap, so the
                // picker re-tints it to match the window.
                currentShade: Widget.CurrentShade,
                questCatalog: Widget.QuestCatalog,
                onAddQuestLink: Widget.OnAddQuestLink)),
        };

        if (Widget.ShowSwitchToRead)
        {
            buttons.Add(new Expanded(child: new Button(
                child: BuildButtonLabel(Lang.Get("scribe:scribe-gui-switch-to-read"), buttonTextStyle),
                style: labelButtonStyle,
                onTap: _ => Widget.OnSwitchToRead())));
        }

        buttons.Add(
                            // Trailing info button: non-Expanded so it hugs the right edge. Its glyph is set directly
                            // with NO Center wrapper, which would greedily expand the button. Opens the editor
                            // reference handbook page; the tooltip labels it.
                            ScribeGlobalTint.ShadedTooltip(
                                child: new Button(
                                    // 17f glyph (height-locked square on the cuneiform path; see IconButtonChild).
                                    child: IconButtonChild("scribeinfo"),
                                    style: iconButtonStyle,
                                    onTap: _ => Widget.OnOpenEditorReference()),
                                // Shade the whole tooltip (bubble + text) to match the body in low light.
                                content: new Padding(
                                    EdgeInsets.All(6),
                                    child: new Text(Lang.Get("scribe:scribe-gui-editor-reference-tooltip"), new TextStyle
                                    {
                                        FontSize = 13,
                                        SoftWrap = true,
                                        Color = colors.OnBackground,
                                    })),
                                baseTheme: Theme.Of(context),
                                shade: Widget.CurrentShade));

        // Settings gear, tablet only (no nav column to reach Settings); styled like the info button. Omitted when
        // OnOpenSettings is null.
        if (Widget.OnOpenSettings is { } openSettings)
        {
            buttons.Add(ScribeGlobalTint.ShadedTooltip(
                child: new Button(
                    child: IconButtonChild("scribegear"),
                    style: iconButtonStyle,
                    onTap: _ => openSettings()),
                content: new Padding(
                    EdgeInsets.All(6),
                    child: new Text(Lang.Get("scribe:scribe-gui-nav-settings"), new TextStyle
                    {
                        FontSize = 13,
                        SoftWrap = true,
                        Color = colors.OnBackground,
                    })),
                baseTheme: Theme.Of(context),
                shade: Widget.CurrentShade));
        }

        return buttons.ToArray();
    }
}

/// <summary>
/// One editor row: a task checkbox (optimistic self-stateful flip) + the editable multi-line field. The dialog
/// owns the focus node so it can move focus across rows; the field writes through to the scratch document via
/// <see cref="ScribeMultilineField.OnChanged"/>.
/// </summary>
internal sealed class ScribeEditRow : StatefulWidget
{
    public ScribeEditRow(
        ScribeEditRowData data,
        FocusNode? focusNode,
        bool autoFocus,
        bool isDropTarget,
        bool isDragSource,
        bool dragActive,
        Action<int, string> onTextChanged,
        Action<int> onCommitAndAdvance,
        Action<int> onCommitAndRetreat,
        Action<int> onInsertTaskBelow,
        Action<int> onRowBlurred,
        Action<int> onMaxLengthReached,
        Action<int> onCaretMoved,
        Action<int> onPointerFocus,
        Action<int> onJumpToFirstRow,
        Action<int> onJumpToLastRow,
        Action<int> onToggleTask,
        Action<int> onDelete,
        Action<int> onTogglePinned,
        Action<int, int> onTrackerQuantityChanged,
        Action<int> onDragStart,
        Action<int> onDragOver,
        Action onDragEnd,
        Action onGripPress,
        Action<int> onGripTap,
        ScribeRowStyle style,
        System.Action<Guid>? onOpenLink = null,
        SKBitmap? assignedStampBitmap = null,
        SKBitmap? externalStampBitmap = null,
        ScribeAmbientLightSampler.Shade currentShade = default,
        Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        Data = data;
        FocusNode = focusNode;
        AutoFocus = autoFocus;
        IsDropTarget = isDropTarget;
        IsDragSource = isDragSource;
        DragActive = dragActive;
        OnTextChanged = onTextChanged;
        OnCommitAndAdvance = onCommitAndAdvance;
        OnCommitAndRetreat = onCommitAndRetreat;
        OnInsertTaskBelow = onInsertTaskBelow;
        OnRowBlurred = onRowBlurred;
        OnMaxLengthReached = onMaxLengthReached;
        OnCaretMoved = onCaretMoved;
        OnPointerFocus = onPointerFocus;
        OnJumpToFirstRow = onJumpToFirstRow;
        OnJumpToLastRow = onJumpToLastRow;
        OnToggleTask = onToggleTask;
        OnDelete = onDelete;
        OnTogglePinned = onTogglePinned;
        OnTrackerQuantityChanged = onTrackerQuantityChanged;
        OnDragStart = onDragStart;
        OnDragOver = onDragOver;
        OnDragEnd = onDragEnd;
        OnGripPress = onGripPress;
        OnGripTap = onGripTap;
        OnOpenLink = onOpenLink;
        Style = style;
        AssignedStampBitmap = assignedStampBitmap;
        ExternalStampBitmap = externalStampBitmap;
        CurrentShade = currentShade;
    }

    public ScribeEditRowData Data { get; }
    public FocusNode? FocusNode { get; }
    public bool AutoFocus { get; }
    /// <summary>True while this row is the current drag drop target; paints a darker wash + border.</summary>
    public bool IsDropTarget { get; }
    /// <summary>True while this row is the one being dragged: shows a ◀ grip and dims. Takes priority over
    /// <see cref="IsDropTarget"/>.</summary>
    public bool IsDragSource { get; }
    /// <summary>True while ANY grip-drag is in progress; non-participating rows hide their grip glyph.</summary>
    public bool DragActive { get; }
    public Action<int, string> OnTextChanged { get; }
    public Action<int> OnCommitAndAdvance { get; }
    public Action<int> OnCommitAndRetreat { get; }
    public Action<int> OnInsertTaskBelow { get; }
    public Action<int> OnRowBlurred { get; }
    public Action<int> OnMaxLengthReached { get; }
    /// <summary>Keyboard caret move (arrows/Home/End) that didn't change text — the editor follows the caret
    /// into view. See <see cref="ScribeMultilineField.OnCaretMoved"/>.</summary>
    public Action<int> OnCaretMoved { get; }
    /// <summary>Row focused by a mouse click — the editor suppresses scroll-into-view (the click point is
    /// already visible). See <see cref="ScribeMultilineField.OnPointerFocus"/>.</summary>
    public Action<int> OnPointerFocus { get; }
    /// <summary>Cmd/Ctrl+Up on this row — jump focus to the first row. See
    /// <see cref="ScribeMultilineField.OnJumpToFirstRow"/>.</summary>
    public Action<int> OnJumpToFirstRow { get; }
    /// <summary>Cmd/Ctrl+Down on this row — jump focus to the last row. See
    /// <see cref="ScribeMultilineField.OnJumpToLastRow"/>.</summary>
    public Action<int> OnJumpToLastRow { get; }
    public Action<int> OnToggleTask { get; }
    public Action<int> OnDelete { get; }
    public Action<int> OnTogglePinned { get; }
    /// <summary>A Tracker row's inline +/- stepper changed its target quantity: (blockIndex, newTarget).</summary>
    public Action<int, int> OnTrackerQuantityChanged { get; }
    public Action<int> OnDragStart { get; }
    public Action<int> OnDragOver { get; }
    public Action OnDragEnd { get; }
    /// <summary>Grip pressed (before any movement). Clears the parent’s tap-suppression so a genuine
    /// tap after a cancelled drag still nests.</summary>
    public Action OnGripPress { get; }
    public Action<int> OnGripTap { get; }
    /// <summary>Non-null only on the tablet: clicking an item row's name opens its Handbook page, keyed by TaskId.</summary>
    public System.Action<Guid>? OnOpenLink { get; }
    public ScribeRowStyle Style { get; }
    /// <summary>The assigned-task stamp raster, threaded from the content widget; null falls back to the SVG glyph.</summary>
    public SKBitmap? AssignedStampBitmap { get; }
    /// <summary>The external-mod hover-info stamp raster, threaded from the content widget; null falls back to the SVG glyph.</summary>
    public SKBitmap? ExternalStampBitmap { get; }
    /// <summary>The live ambient-illumination shade, threaded down so the assignment marker's hover
    /// tooltip can match the body's shading (see <see cref="ScribeEditorContent.CurrentShade"/>).</summary>
    public ScribeAmbientLightSampler.Shade CurrentShade { get; }

    public override State CreateState() => new ScribeEditRowState();
}

internal sealed class ScribeEditRowState : State<ScribeEditRow>
{
    private bool done;
    /// <summary>Pointer position at grip-press, used to start a drag only after movement.</summary>
    private float gripPressX, gripPressY;
    /// <summary>True once this row's grip crossed the drag threshold this press (lost on remount;
    /// <see cref="ScribeEditRow.IsDragSource"/> covers the post-remount case).</summary>
    private bool localGripDragStarted;
    /// <summary>True between this grip's press and release. Hover moves must not start a drag.</summary>
    private bool gripPressed;
    /// <summary>Pixels of movement before a grip press becomes a reorder drag rather than a nest tap.</summary>
    private const float GripDragThreshold = 5f;
    /// <summary>True while the pointer is over this row; delete/pin controls are hidden until then. A row-level
    /// <see cref="MouseRegion"/> doesn't steal click-to-focus (hit-testing is innermost-first) and keeps firing
    /// enter/exit during grip-drag capture. The grip is NOT hover-gated so a drag can't lose capture mid-move.</summary>
    private bool hovered;

    public override void InitState()
    {
        base.InitState();
        done = Widget.Data.Done;
        // Repaint the row Container on field focus change (the cuneiform path draws a focus border/background).
        // The focus node is dialog-owned and shared; ChangeNotifier accepts multiple listeners.
        Widget.FocusNode?.AddListener(OnFieldFocusChanged);
    }

    /// <summary>Moves the focus listener if a rebuild hands this row a different node, and resyncs the optimistic
    /// <see cref="done"/> when an external change flips authoritative completion (rows are reused in place, so
    /// the <see cref="InitState"/> seed would go stale). Gated on the value actually CHANGING so a chrome
    /// reconcile doesn't stomp an in-flight optimistic tick.</summary>
    public override void UpdateWidget(ScribeEditRow oldWidget)
    {
        base.UpdateWidget(oldWidget);
        if (!ReferenceEquals(oldWidget.FocusNode, Widget.FocusNode))
        {
            oldWidget.FocusNode?.RemoveListener(OnFieldFocusChanged);
            Widget.FocusNode?.AddListener(OnFieldFocusChanged);
        }
        if (oldWidget.Data.Done != Widget.Data.Done) done = Widget.Data.Done;
    }

    private void OnFieldFocusChanged() => SetState(() => { });

    public override void Dispose()
    {
        Widget.FocusNode?.RemoveListener(OnFieldFocusChanged);
        base.Dispose();
    }

    /// <summary>The editor content of an item row: icon + name in place of the text field, plus for a Tracker an
    /// inline +/- stepper editing <see cref="ScribeBlock.TargetQuantity"/>. The stepper is uncontrolled and
    /// seeded from the current target; rows reconcile in place and this is the target's only editor-side
    /// mutation, so no ValueKey remount (which would drop focus on every step) is needed. A Link edits nothing
    /// inline.</summary>
    private Widget BuildItemEditorContent(ColorScheme colors, ScribeRowStyle style, int index)
    {
        float iconSize = ScribeRowConstants.ItemIconSize
            * (style.ControlSize / ScribeRowConstants.RowCheckboxSize);
        float lineHeight = ScribeRowControlNudge.TextLineHeight(style.FontSize);
        // A Quest Link's icon renders in the row's leading slot, so this slot has no icon and the name band falls
        // back to ItemNameLineHeight.
        bool isQuestLink = ScribeLinkTarget.IsQuest(Widget.Data.LinkTarget);
        float iconVisual = ScribeRowControlNudge.ItemVisualBandHeight(
            style, Widget.Data.LinkTarget, Widget.Data.IsStaticVsQuestObjective, Widget.Data.DisplayStack);
        float stepperHeight = Widget.Data.IsCarriedCountTracked ? style.ControlSize * 1.15f : 0f;
        float bandHeight = MathF.Max(iconVisual, stepperHeight);
        var rowChildren = new List<Widget>();

        if (Widget.Data.IsCarriedCountTracked)
        {
            // Stepper at the LEFT: the hover-revealed delete/pin float over the RIGHT edge and would cover it.
            // Height is ControlSize×1.15, width twice that; centered in the icon band when the name is one line.
            // clamp keeps the target a whole number ≥ 1 (matching the Core setter).
            float stepperWidth = stepperHeight * 2f;
            rowChildren.Add(ScribeCenterIfShort.InBand(
                new ScribeNumericField(
                    initialValue: Widget.Data.TargetQuantity,
                    step: 1,
                    clamp: v => v < 1 ? 1 : (float)Math.Round(v),
                    onChanged: v => Widget.OnTrackerQuantityChanged(index, (int)Math.Round(v)),
                    style: new BoxStyle { Width = stepperWidth, Height = stepperHeight },
                    textStyle: new TextStyle
                    {
                        Color = colors.OnSurface,
                        FontFamily = ScribeTaskFont.Resolve(style.TaskFontFamily),
                        FontSize = ScribeTaskFont.LayoutSize(style.TaskFontFamily, style.FontSize),
                    },
                    focusBorderColor: style.InputFocusBorderColor),
                bandHeight));
        }

        // Link-accent book glyph: Primary, or the row's override where Primary is illegible (Chalkboard; see
        // ScribeRowStyle.LinkColor). A Quest Link's icon is in the leading slot, so no second icon here.
        Vector4 linkColor = Widget.Data.IsStaticVsQuestObjective
            ? colors.OnSurface
            : isQuestLink
            ? style.QuestLinkColor ?? ScribeTheme.QuestLinkAccent
            : style.LinkColor ?? colors.Primary;
        if (!isQuestLink)
        {
            rowChildren.Add(ScribeCenterIfShort.InBand(
                Widget.Data.IsStaticVsQuestObjective
                    ? ScribeLinkIcon.BuildObjective(Widget.Data.DisplayStack, iconSize, colors.OnSurfaceVariant,
                        lineHeight, heightNeutral: false)
                    : ScribeLinkIcon.Build(Widget.Data.DisplayStack, Widget.Data.LinkTarget, iconSize,
                        linkColor, lineHeight, heightNeutral: false),
                bandHeight));
        }

        // The name is a hyperlink ONLY where the surface opts in (the tablet), using the read row's GestureDetector
        // shape. The sibling ScribeNumericField is a separate hit region, so the number stays editable.
        // Otherwise it is a plain OnSurface label.
        bool canOpen = !Widget.Data.IsStaticVsQuestObjective || Widget.Data.DisplayStack is not null;
        Widget nameLabel = Widget.OnOpenLink is { } openLink && canOpen
            ? new GestureDetector(
                onPress: e => { e.Handled = true; openLink(Widget.Data.TaskId); },
                child: ScribeItemLabel.Build(Widget.Data.Label, linkColor, style))
            : ScribeItemLabel.Build(Widget.Data.Label, colors.OnSurface, style);
        rowChildren.Add(new Expanded(child: ScribeCenterIfShort.Name(nameLabel, style, bandHeight)));

        // No left FieldPadX: the stepper must line up with the Task field box edge. Keep the right inset so
        // wrapped names avoid the hover buttons. A Link row has no stepper, so it gets the 4px left inset the
        // Read/Pinned views use.
        float leftInset = Widget.Data.IsCarriedCountTracked ? 0f : 4f;
        return new Padding(
            EdgeInsets.Only(left: leftInset, right: style.FieldPadX),
            child: new Row(
                spacing: style.CheckboxTextGap,
                crossAxisAlignment: CrossAxisAlignment.Start,
                mainAxisSize: MainAxisSize.Max,
                children: rowChildren));
    }

    public override Widget Build(BuildContext context)
    {
        int index = Widget.Data.Index;
        var style = Widget.Style;
        var colors = Theme.Of(context).ColorScheme;

        var children = new List<Widget>();

        // Grip on the FAR LEFT, always mounted (not hover-gated) so a drag can't lose pointer capture mid-move.
        // Nudged to center on a one-line input; the top margin sits OUTSIDE the GestureDetector so the hit-target
        // covers the glyph. The negative right inset cancels the Row's CheckboxTextGap so the text column
        // reclaims that width.
        //
        // The glyph is state-driven: source row → ◀ (muted), prospective drop row → ▶ (accent), other rows during
        // a drag → a same-size empty box (no reflow). Source wins over drop-target. Ink: the tablet's darker
        // GripGlyphColor, else OnSurfaceVariant.
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
                onPress: e =>
                {
                    gripPressed = true;
                    localGripDragStarted = false;
                    gripPressX = e.X;
                    gripPressY = e.Y;
                    Widget.OnGripPress();
                },
                onMove: e =>
                {
                    // LibGUI fires OnMove on hover as well as drag; PointerEvent.Button defaults to Left
                    // even when the button is up. Only start a drag after an actual press on this grip.
                    if (!gripPressed || localGripDragStarted || Widget.IsDragSource) return;
                    float dx = e.X - gripPressX;
                    float dy = e.Y - gripPressY;
                    if (dx * dx + dy * dy < GripDragThreshold * GripDragThreshold) return;
                    localGripDragStarted = true;
                    Widget.OnDragStart(index);
                },
                onRelease: _ =>
                {
                    bool started = localGripDragStarted || Widget.IsDragSource;
                    gripPressed = false;
                    if (started)
                        Widget.OnDragEnd();
                },
                // LibGUI fires OnPointerClick (OnTap) after up regardless of movement. Parent skipGripTap
                // swallows the tap once a drag started, including a from==to cancel.
                onTap: _ => Widget.OnGripTap(index),
                child: gripGlyph)));

        // Source-row dim: content (checkbox + text) paints at half opacity, per-child so the ◀ grip keeps full ink.
        // Opacity is ALWAYS present (1.0↔0.5) so there is no widget-type swap mid-drag.
        float contentOpacity = Widget.IsDragSource ? 0.5f : 1f;

        // Completable kinds show a completion checkbox.
        if (Widget.Data.Completable)
        {
            children.Add(new Opacity(contentOpacity, child: new Padding(
                EdgeInsets.Only(top: ScribeRowControlNudge.CheckboxAndGripTop(
                    style, Widget.Data.IsItemKind, Widget.Data.LinkTarget,
                    Widget.Data.IsStaticVsQuestObjective, Widget.Data.DisplayStack)),
                // A Quest Link's checkbox is misleading (toggling it doesn't affect the quest), so its slot
                // renders the quest-marker icon instead.
                child: ScribeRowControlNudge.BuildLeadingControl(
                    context, style, Widget.Data.LinkTarget, done,
                    _ =>
                    {
                        SetState(() => done = !done);
                        Widget.OnToggleTask(index);
                    }))));
        }

        // Assignment marker; takes space only for accepted assignments.
        if (Widget.Data.IsAcceptedAssignment)
            children.Add(ScribeAssignedTaskIcon.Build(style, colors.OnSurfaceVariant, Widget.Data.IsItemKind, Widget.AssignedStampBitmap,
                context: context, currentShade: Widget.CurrentShade,
                assignerName: Widget.Data.AssignerName, assignedDate: Widget.Data.AssignedDate, acceptedDate: Widget.Data.AcceptedDate));

        // External-mod hover-info marker; can appear beside the assignment marker.
        if (!string.IsNullOrEmpty(Widget.Data.ExtraInfo))
            children.Add(ScribeExternalInfoIcon.Build(style, colors.OnSurfaceVariant, Widget.Data.IsItemKind, Widget.ExternalStampBitmap,
                context: context, currentShade: Widget.CurrentShade, extraInfo: Widget.Data.ExtraInfo));

        // Item rows have no text field: content is the dialog-resolved icon + name (plus a Tracker stepper),
        // dimmed like a text row during a drag.
        if (Widget.Data.IsItemKind)
        {
            children.Add(new Expanded(child: new Opacity(contentOpacity,
                child: BuildItemEditorContent(colors, style, index))));
        }
        else
        {

        // Ghost hint only on TASK rows; a text section is legitimately empty.
        string placeholder = Widget.Data.IsTask ? Lang.Get("scribe:scribe-gui-newtask-placeholder") : "";

        children.Add(new Expanded(child: new Opacity(contentOpacity, child: new ScribeMultilineField(
            initialText: Widget.Data.Text,
            placeholder: placeholder,
            focusNode: Widget.FocusNode,
            fontSize: style.FontSize,
            fontFamily: ScribeTaskFont.Resolve(style.TaskFontFamily),
            padX: style.FieldPadX,
            padY: style.FieldPadY,
            // null on light surfaces (→ theme Primary); chalk-white on the Chalkboard (InputFocusBorderColor).
            focusBorderColor: style.InputFocusBorderColor,
            autoFocus: Widget.AutoFocus,
            // Tablet-only cuneiform rendering; off for Lectern/Notebook and the disable-cuneiform fallback.
            useCuneiform: style.UseCuneiform,
            cuneiformBundle: style.CuneiformBundle,
            cuneiformJitter: style.UseCuneiform ? style.CuneiformJitter : 0f,
            // Whole-character tilt: the seed omits the stroke ordinal so a character tilts as one rigid unit,
            // independent of the jitter's per-stroke wobble.
            cuneiformRotation: style.UseCuneiform ? style.CuneiformRotation : 0f,
            // Fixed per-row seed from the TaskId so typing doesn't re-wobble existing letters.
            cuneiformJitterSeed: Widget.Data.TaskId.GetHashCode(),
            cuneiformProgression: style.UseCuneiform && style.CuneiformProgression,
            cuneiformGlow: style.UseCuneiform ? style.CuneiformGlow : default,
            cuneiformStrokeWeightScale: style.UseCuneiform ? style.CuneiformStrokeWeightScale : 1f,
            // Each kind has its own live maxlength (task soft cap, larger note cap); the codec clips both on read.
            // Hitting it fires OnMaxLengthReached.
            maxLength: Widget.Data.IsTask ? ScribeDocumentCodec.MaxTaskTextLength : ScribeDocumentCodec.MaxTextLength,
            onChanged: text => Widget.OnTextChanged(index, text),
            onCommitAndAdvance: () => Widget.OnCommitAndAdvance(index),
            onCommitAndRetreat: () => Widget.OnCommitAndRetreat(index),
            onInsertTaskBelow: () => Widget.OnInsertTaskBelow(index),
            onBlur: () => Widget.OnRowBlurred(index),
            onMaxLengthReached: () => Widget.OnMaxLengthReached(index),
            onCaretMoved: () => Widget.OnCaretMoved(index),
            onPointerFocus: () => Widget.OnPointerFocus(index),
            onJumpToFirstRow: () => Widget.OnJumpToFirstRow(index),
            onJumpToLastRow: () => Widget.OnJumpToLastRow(index)))));
        }

        // Row body [grip][checkbox][text]. Delete/pin float over the row rather than reserving columns; a Depth-1
        // subtask gets a left inset.
        float subtaskIndent = Widget.Data.Depth > 0 ? style.SubtaskIndent : 0f;
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

        // Resting pinned tint behind the row. The Container is ALWAYS present (transparent fill when unpinned) so
        // toggling is a property update, not a widget-type swap. Drag feedback lives on the grip glyph and the
        // source-row dim, and focus chrome is drawn by the focused input itself (see ScribeMultilineField's
        // cuneiform render widget), so neither collides with the pinned tint.
        Vector4 rowFill = Widget.Data.Pinned
            && (Widget.Data.Completable || Widget.Data.IsStaticVsQuestObjective)
                ? ScribeRowConstants.PinnedTint(colors)
                : Vector4.Zero;

        rowBody = new Container(
            style: new BoxStyle
            {
                Color = rowFill,
                BorderColor = Vector4.Zero,
                BorderThickness = 0f,
                CornerRadius = Vector4.Zero,
            },
            child: rowBody);

        // Delete + pin float on the RIGHT as hover-only buttons in a Stack over the non-positioned rowBody, so they
        // reserve no column. Right-anchored, pin left of delete.
        //
        // STRUCTURAL STABILITY: the row is ALWAYS a Stack whose index-0 child is ALWAYS this Container(rowBody).
        // The reconciler matches by type + key + position, so swapping the child between rowBody and a Stack on
        // hover would unmount the subtree, destroying the field's State and its in-progress text (remounted from
        // the stale Data.Text). Only the trailing Positioned buttons mount/unmount.
        var stackChildren = new List<Widget> { rowBody };
        if (hovered)
        {
            float btn = style.ControlSize;
            float gap = 4f;
            // Drawn button width (ScribeRowButton shrinks its chrome by BoxShrink); spaces the pin against delete's
            // real edge.
            float boxW = btn - ScribeRowButton.BoxShrink;
            // Right inset: resting gap +1px. Top is the computed centering offset (FloatingButtonTop) for a
            // one-line input.
            float btnRight = gap + 1f;
            float btnTop = ScribeRowControlNudge.FloatingButtonTop(style, Widget.Data.IsItemKind);
            // delete: right-most; pin: to its left (every kind, including notes).
            stackChildren.Add(new Positioned(
                right: btnRight, top: btnTop,
                child: new ScribeRowButton(
                    iconName: "scribeclose",
                    iconColor: colors.Error,
                    size: btn,
                    onTap: () => Widget.OnDelete(index))));
            // Task, Tracker, Link, Craft, and Text notes are all pinnable.
            stackChildren.Add(new Positioned(
                right: btnRight + boxW + gap, top: btnTop,
                child: new ScribeRowButton(
                    iconName: "scribepin",
                    // Pinned reads "active" (accent); unpinned is muted.
                    iconColor: Widget.Data.Pinned ? colors.Primary : colors.OnSurfaceVariant,
                    size: btn,
                    onTap: () => Widget.OnTogglePinned(index),
                    iconScale: 1.15f))); // pin glyph +15%
        }

        Widget row = new Stack(stackChildren);

        // Row-level hover tracking: enter/exit reveal the hover controls and also forward the drop-target signal
        // during a drag.
        return new MouseRegion(
            onEnter: _ =>
            {
                if (!hovered) SetState(() => hovered = true);
                Widget.OnDragOver(index);
            },
            onExit: _ => { if (hovered) SetState(() => hovered = false); },
            child: row);
    }
}

// ---- Pin Tab content ----

/// <summary>A value snapshot of one Pin Tab row: the pin's stable identity, its displayed done-state, and
/// the text to seed the field with (the live edit buffer if one is in flight, else the server snapshot).
/// Carries no live pin reference — a resync rebuilds instead.</summary>
