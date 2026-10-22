using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle, FontWeight
using Gui.Widgets.Basic;         // Text, WindowFrame, VsIcon, Container, Button
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, Theme, ValueKey, Key
using Gui.Widgets.Input;         // Checkbox, FocusNode, GestureDetector, MouseRegion, Dropdown, DropdownItem
using Gui.Widgets.Gestures;      // ScrollController
using Gui.Widgets.Layout;        // Column, Row, Expanded, Padding, SizedBox, Center, Align, Alignment, CrossAxisAlignment, MainAxisAlignment
using Gui.Widgets.Painting;      // BoxStyle
using Gui.Widgets.Scroll;        // ListView, SingleChildScrollView, Scrollable, Scrollbar
using Gui.Core.Layout;           // MainAxisSize
using Scribe.Core;
using SkiaSharp;                 // SKBitmap (assigned-task stamp raster)
using Vintagestory.API.Common;   // ItemStack (Tracker/Link display item)
using Vintagestory.API.Config;   // Lang, GlobalConstants

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
