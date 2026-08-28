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
using Vintagestory.API.Client;
using Vintagestory.API.Common;   // ItemStack (Tracker/Link display item)
using Vintagestory.API.Config;   // Lang, GlobalConstants
using Vintagestory.API.MathTools;  // BlockPos

namespace Scribe;

/// <summary>A value snapshot of one editable block plus its index. The live text lives in the dialog's
/// scratch document (the field writes through on every keystroke); this is only the seed for building the
/// row. <see cref="Kind"/> distinguishes Task / Text / Tracker / Link (add-tracker-link-tasks Group 5): a
/// Tracker row swaps its text field for an item icon + name + <see cref="TargetQuantity"/> stepper, and a
/// Link row shows the icon + name of its <see cref="LinkTarget"/>. The icon/name are resolved by the dialog
/// and passed in as <see cref="DisplayStack"/>/<see cref="DisplayName"/> so this row widget stays API-free.</summary>
internal readonly record struct ScribeEditRowData(
    int Index, ScribeBlockKind Kind, bool Done, bool Pinned, Guid TaskId, string Text,
    ItemStack? DisplayStack = null, string? DisplayName = null,
    int TargetQuantity = 1, int CurrentQuantity = 0, string? LinkTarget = null, int Depth = 0)
{
    public bool IsTask => Kind == ScribeBlockKind.Task;
    public bool IsTracker => Kind == ScribeBlockKind.Tracker;
    public bool IsLink => Kind == ScribeBlockKind.Link;
    public bool IsCraft => Kind == ScribeBlockKind.Craft;
    /// <summary>Kinds that render as an item icon + name (their own Text is empty): Tracker, Link, and the
    /// Craft parent (which shows its recipe output — add-crafting-tasks 9.1).</summary>
    public bool IsItemKind => IsTracker || IsLink || IsCraft;
    /// <summary>Kinds whose row carries a live have/need counter + inline target stepper: Tracker and the
    /// Craft parent (both count the viewer's carried inventory — add-crafting-tasks 9.2). Mirrors
    /// <see cref="ScribeBlock.IsCarriedCountTracked"/>.</summary>
    public bool IsCarriedCountTracked => IsTracker || IsCraft;
    /// <summary>Task, Tracker, Link, and Craft all carry a Done flag, so they get a completion checkbox;
    /// a freeform Text section doesn't. Pin is offered on every kind (including notes).</summary>
    public bool Completable => Kind != ScribeBlockKind.Text;
    /// <summary>The row's display label: a Craft parent frames its output name ("Craft Iron Ingot"), a
    /// Tracker/Link shows its resolved item name (its own Text is empty), a Task/Text shows its authored
    /// text.</summary>
    public string Label => IsCraft ? Lang.Get("scribe:scribe-gui-craft-row-label", DisplayName ?? Text)
        : IsItemKind ? (DisplayName ?? Text) : Text;
}

/// <summary>
/// A static, non-interactive snapshot of a deleted editor row, shown while it collapses out of the list
/// (scribe-list-collapse). The row's scratch block and focus node are already gone, so this renders a
/// FROZEN copy — the same [grip-spacer][checkbox][text] column an editor row uses (so it aligns and
/// collapses seamlessly), but with no editable field, no gestures, and no delete/pin/drag controls. It is
/// never focused and never mutates anything; the <see cref="ScribeRowSizeAnimation"/> wrapping it animates its
/// height to zero and then removes it.
/// </summary>
internal sealed class ScribeFrozenEditorRow : StatelessWidget
{
    private readonly ScribeEditRowData data;
    private readonly ScribeRowStyle style;

    public ScribeFrozenEditorRow(ScribeEditRowData data, ScribeRowStyle style)
    {
        this.data = data;
        this.style = style;
    }

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        // Font family + base size inherited from the tab's DefaultTextStyle ancestor (this ghost is a
        // snapshot of a task-font row, so inheriting the task font keeps it consistent as it collapses).
        TextStyle textStyle = new() { Color = colors.OnSurface, SoftWrap = true };

        var children = new List<Widget>
        {
            // Grip-column spacer (invisible, uninteractable), matching the editor row's far-left grip (same
            // GripInsets, §10.4) so the ghost's columns line up with its neighbors as it collapses.
            new Padding(
                ScribeRowControlNudge.GripInsets(style, data.IsItemKind),
                child: new Opacity(
                    opacity: 0f,
                    child: new ScribeVsIconGlyph("scribegrip", style.ControlSize, colors.OnSurfaceVariant))),
        };

        if (data.Completable)
        {
            // A frozen (disabled) checkbox reflecting the row's last done-state — no onChanged, so it can't
            // be toggled while it collapses. Task, Tracker, and Link all carry a Done flag (Completable).
            // Tick color routes through the row style's CheckTickColor seam (§11) so it matches the live rows.
            children.Add(new Padding(
                EdgeInsets.Only(top: ScribeRowControlNudge.CheckboxAndGripTop(style, data.IsItemKind)),
                child: ScribeRowControlNudge.BuildTaskCheckbox(context, style, data.Done, onChanged: null)));
        }

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
/// The editor view's content tree. Unlike the read view it uses a NON-virtualized
/// <see cref="SingleChildScrollView"/> + <see cref="Column"/> of ALL rows (design D2): LibGUI's
/// <see cref="ListView"/> unmounts off-screen rows, which would destroy an off-screen row's focus
/// node and drop focus/caret when a focused row grows past the viewport. Keeping every row mounted
/// lets the dialog coordinate cross-row focus (Enter/Shift+Tab) and keep a growing focused row in
/// view. A footer carries "Add task" and "Done editing".
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
        System.Action<Guid>? onOpenLink = null)
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
    }

    public IReadOnlyList<ScribeEditRowData> Blocks { get; }
    public IReadOnlyList<FocusNode> FocusNodes { get; }
    public int? AutoFocusIndex { get; }
    public Action<int, string> OnTextChanged { get; }
    public Action<int> OnCommitAndAdvance { get; }
    public Action<int> OnCommitAndRetreat { get; }
    public Action<int> OnInsertTaskBelow { get; }
    /// <summary>A row's field genuinely lost focus (add-empty-task-lifecycle): the dialog removes the row
    /// if it is an empty task. See <see cref="ScribeDialogBase.OnRowBlurred"/>.</summary>
    public Action<int> OnRowBlurred { get; }
    /// <summary>A row hit its per-kind character cap while typing/pasting (add-note-kind-picker §8): the
    /// dialog surfaces a transient "limited to N characters" notice. See
    /// <see cref="ScribeDialogBase.OnRowMaxLengthReached"/>.</summary>
    public Action<int> OnMaxLengthReached { get; }
    /// <summary>A row's caret moved by keyboard nav (no text change) — the dialog follows it into view
    /// (scroll-follow-caret-in-editor issue #1). See <see cref="ScribeDialogBase.NotifyCaretMoved"/>.</summary>
    public Action<int> OnCaretMoved { get; }
    /// <summary>A row was focused by a mouse click — the dialog suppresses scroll-into-view for it
    /// (scroll-follow-caret-in-editor issue #3). See <see cref="ScribeDialogBase.NotifyPointerFocus"/>.</summary>
    public Action<int> OnPointerFocus { get; }
    /// <summary>Cmd/Ctrl+Up in a row — the dialog jumps focus to the first row, caret at its start
    /// (scroll-follow-caret-in-editor Cmd/Ctrl row-nav). See <see cref="ScribeDialogBase.EditorJumpToFirstRow"/>.</summary>
    public Action<int> OnJumpToFirstRow { get; }
    /// <summary>Cmd/Ctrl+Down in a row — the dialog jumps focus to the last row, caret at its end. See
    /// <see cref="ScribeDialogBase.EditorJumpToLastRow"/>.</summary>
    public Action<int> OnJumpToLastRow { get; }
    public Action<int> OnToggleTask { get; }
    public Action<int> OnDeleteBlock { get; }
    public Action<int> OnTogglePinned { get; }
    /// <summary>A single tap (not a press-hold-drag) on a row's grip toggles its subtask depth 0↔1
    /// (task-subtasks 5.3). The grip's <see cref="GestureDetector"/> fires this only on a genuine click —
    /// the dispatcher suppresses the tap during a drag — so reordering is unaffected. See
    /// <see cref="ScribeDialogBase.OnGripTap"/>.</summary>
    public Action<int> OnGripTap { get; }
    /// <summary>Reorder a block from one index to another (drag drop). See
    /// <see cref="ScribeEditorContentState"/> for the drag mechanics.</summary>
    public Action<int, int> OnReorderBlock { get; }
    /// <summary>A Tracker row's inline +/- stepper changed its target quantity: (blockIndex, newTarget)
    /// (add-tracker-link-tasks 5.2). The dialog writes it into the scratch document; the normal editor flush
    /// persists it (the codec already serializes TargetQuantity, so no dedicated packet). See
    /// <see cref="ScribeDialogBase.SetEditorTrackerTargetQuantity"/>.</summary>
    public Action<int, int> OnTrackerQuantityChanged { get; }
    /// <summary>Add a block of the chosen kind (add-note-kind-picker D2). The footer's segmented picker calls
    /// this with its current primary kind (defaults to Task) on a primary click, or with the picked kind from
    /// the inline list. See <see cref="ScribeDialogBase.OnClickAdd"/>.</summary>
    public Action<ScribeAddKind> OnAdd { get; }
    /// <summary>Whether the footer's add control may add another block. Default true (uncapped tiers —
    /// Lectern, Notebook — always). Finite tiers (tablet, chalkboard) pass false once the document holds
    /// 10 entries of any kind (scribe-document-policy), which DIMS the primary button and every drop-up
    /// tile. The buttons stay clickable so the tap reaches <see cref="ScribeDialogBase.OnClickAdd"/> and
    /// surfaces the cap notice (refine-chalkboard §12.9).</summary>
    public bool AddTaskEnabled { get; }
    public Action OnSwitchToRead { get; }
    /// <summary>Whether the footer shows the "Done editing" (switch-to-read) button. Default true for the
    /// tabbed dialogs (Lectern/Notebook), which have a Read view to switch to. The always-edit tablet
    /// (add-tablet-dialog) passes false: it has no Read view, so leaving editor mode would null the scratch
    /// and there would be nowhere to land — the button is simply omitted there.</summary>
    public bool ShowSwitchToRead { get; }
    /// <summary>The dialog's current illumination shade (add-note-kind-picker + respect-local-illumination):
    /// threaded so the footer's add-kind picker can tint its FLOATING drop-up menu to match the window. The
    /// menu paints in the Overlay layer, which sits OUTSIDE the dialog body's <see cref="ScribeGlobalTint"/>
    /// wrap, so it would otherwise render at full brightness while the rest of the dialog is shaded (the bug
    /// the user reported). The picker re-wraps its menu content in its own <see cref="ScribeGlobalTint"/>
    /// using this value. Refreshed on every <c>RebuildBody()</c>, so a newly-opened menu uses the current
    /// shade.</summary>
    public ScribeAmbientLightSampler.Shade CurrentShade { get; }
    /// <summary>Open the "Scribe Editor Features" handbook page (v1-release-checklist 9.5). Wired from the
    /// dialog (which holds the client API); this widget stays free of the VS API. See
    /// <see cref="ScribeDialogBase.OpenEditorReferenceHandbook"/>.</summary>
    public Action OnOpenEditorReference { get; }
    /// <summary>Open Scribe Settings from a footer gear button, mirroring <see cref="OnOpenEditorReference"/>.
    /// Non-null only for the tablet (add-tablet-cuneiform-chrome): the always-edit tablet has no Settings tab
    /// in a nav column, so its footer carries a gear beside the ⓘ info button. The tabbed dialogs pass null
    /// (they reach Settings through their nav column) and the gear is omitted.</summary>
    public Action? OnOpenSettings { get; }
    /// <summary>Horizontal breathing room applied around the footer button row (Add task / Done editing),
    /// 0.04·W each side, so the buttons don't run to the content edges. Passed from the dialog, which owns
    /// the <c>ScribeLayout</c> width.</summary>
    public EdgeInsets FooterButtonPadding { get; }
    public ScribeRowStyle Style { get; }
    /// <summary>Dialog-owned scroll controller shared by both views (see the dialog field); NOT disposed
    /// here — the dialog owns its lifetime so the scroll offset survives the view-switch rebuild. The
    /// same controller <see cref="Scrollable.EnsureVisible"/> drives when a focused row grows/moves.</summary>
    public ScrollController ScrollController { get; }
    /// <summary>Host-owned collapse controllers for the editor's row list (keyed by TaskId), so a collapse
    /// resumes across the dialog's rebuild remounts. Passed into <see cref="ScribeAnimatedList"/>, which
    /// diffs the row set and animates departures against it (extract-animated-task-list §6.1 / D0). Lives on
    /// the dialog so <c>OnRenderGUI</c> can read <c>AnyAnimating</c> to drive the scroll-pin + hover latch.</summary>
    public ScribeAnimationRegistry CollapseRegistry { get; }
    /// <summary>Fired (deferred, safe) when a departing row's collapse completes and the list has shrunk, so
    /// the dialog can re-clamp the scroll extent — the same settle hook the Read view and Pin Tab use
    /// (<see cref="ScribeAnimatedList.OnDepartureSettled"/>). The container retires the ghost itself; the
    /// dialog no longer owns departing-row bookkeeping.</summary>
    public Action OnDepartureSettled { get; }
    public string HintLangKey { get; }
    /// <summary>Optional click-to-open-Handbook dispatch for an item row's (Link/Tracker/Craft) name label,
    /// keyed by TaskId (enable-tablet-row-links). Non-null ONLY on a surface that opts its editor rows into
    /// link activation (the tablet, which has no read view — see <see cref="ScribeDialogBase.EditorRowsOpenLinks"/>).
    /// Null on every other surface, so their editor names stay plain editable regions and render byte-identical.</summary>
    public System.Action<Guid>? OnOpenLink { get; }

    public override State CreateState() => new ScribeEditorContentState();
}

internal sealed class ScribeEditorContentState : State<ScribeEditorContent>
{
    // ---- Drag-reorder state (this State owns the row list, so a drag updates via SetState here —
    // NOT the dialog's ForceRebuild, which would unmount the grip mid-drag and drop the pointer
    // capture the reorder depends on). dragFromIndex is the row a grip-drag started on; dragOverIndex
    // is the row the cursor is currently over (the prospective drop). Both null when no drag active.
    private int? dragFromIndex;
    private int? dragOverIndex;
    /// <summary>True after a grip drag started this gesture. Survives the SetState remount that
    /// <see cref="OnRowDragStart"/> triggers, so a from==to release still suppresses <see cref="OnGripTap"/>.</summary>
    private bool skipGripTap;

    // The add-kind picker (selected kind + open/closed) is a self-contained widget in the footer
    // (ScribeAddKindPicker), which owns that state so its floating drop-up menu can manage its own overlay
    // entries; see BuildFooterButtons.

    /// <summary>Grip pressed: reset the tap-suppression flag for this gesture. Drag itself starts only
    /// after the pointer moves past a threshold (task-subtasks D11).</summary>
    private void OnRowGripPress() => skipGripTap = false;

    /// <summary>Grip moved past the drag threshold: begin a drag from this row. The event dispatcher
    /// auto-captures the grip's element on press, so the subsequent moves/release keep arriving here even
    /// as the cursor crosses sibling rows (the same mechanism Scrollbar's thumb relies on).</summary>
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

    /// <summary>A row reports the cursor entered it during a drag. The dispatcher fires enter/exit on
    /// the drag-hovered row even while another element (the grip) holds capture, so this is a robust
    /// drop-target signal that needs no manual hit-test geometry.</summary>
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
        // ALWAYS repaint on release: clear the drag under SetState so the ghost row's dimmed content and the
        // ◀/▶ grip arrows revert to rest, even on a no-op release. OnReorderBlock's from==to branch does NOT
        // ForceRebuild (there's no doc edit to apply), so without this SetState a grab-and-release-in-place
        // left the row stuck dimmed with a ◀ handle until the next unrelated rebuild
        // (replace-drag-wash-with-grip-arrows follow-up).
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

        // Route the editor's rows through ScribeAnimatedList (D0 / extract-animated-task-list §6.1), exactly
        // as the Read view and Pin Tab already do. The container diffs the TaskId-keyed set frame-to-frame:
        // a row the dialog deleted from scratch lands here as a now-absent id and the container collapses it
        // out (rows below sliding up) from a frozen ghost, then self-cleans — replacing the dialog's former
        // hand-wired DepartingRows / OnEditorRowCollapsed / needsEditorCollapseCleanup machinery. The
        // container abstracts MOTION only: it decides row ORDER (live rows + collapsing ghosts at their old
        // slots) and hands that list to OUR layoutBuilder, which keeps the editor's own
        // Scrollbar > SingleChildScrollView > Column shape. Immediate policy (the editor delete is an
        // affirmative tap — no undo window, matching read/pin).
        //
        // Each item supplies an explicit frozen ghost (ScribeFrozenEditorRow): a live editable row is unsafe
        // to freeze in place (its field/checkbox/drag gestures would stay live mid-collapse and its focus
        // node is gone once the scratch block leaves), so the container animates the snapshot instead of the
        // live child. Drag-reorder state stays in THIS State (dragFromIndex/dragOverIndex) and is baked into
        // each live row here, so the container stays content-agnostic (its D6).
        var items = Widget.Blocks
            .Select(b => new ScribeAnimatedListItem(
                Id: b.TaskId,
                Child: new ScribeEditRow(
                    data: b,
                    focusNode: b.Index < Widget.FocusNodes.Count ? Widget.FocusNodes[b.Index] : null,
                    autoFocus: Widget.AutoFocusIndex == b.Index,
                    isDropTarget: dragFromIndex is not null && dragOverIndex == b.Index,
                    // The origin row of the in-progress drag (null-safe: false when no drag is active). Its
                    // Build shows the ◀ grip glyph + dims the row, and takes priority over the ▶ drop-target
                    // glyph when the cursor hovers back over the source.
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
                    // Non-null only on the tablet: its item-row names open the Handbook (enable-tablet-row-links).
                    onOpenLink: Widget.OnOpenLink,
                    style: Widget.Style,
                    // Stable per-row identity (reconcile-animating-surfaces §3.2): keyed by the block's
                    // TaskId, NOT its list index. Under the in-place reconcile a RebuildBody() drives
                    // (RebuildBody → BodyState.Build → this Build re-runs → MultiChildElement.Update walks
                    // the rows POSITIONALLY), a Guid key lets an unshifted slot's row be REUSED — its
                    // ScribeMultilineField State, hence caret + unsaved buffer, survives the repaint. An
                    // int index keyed every slot to its position, so any structural change looked like a
                    // brand-new widget at that slot and remounted the field (dropping the caret). LibGUI's
                    // reconciler is positional (no keyed reordering), so this preserves identity only where
                    // the slot is unchanged — the container's departing ghost holds the deleted slot, keeping
                    // rows below in place; a delete/insert ABOVE the focused row still shifts + remounts it
                    // (the accepted positional caveat — text survives via the scratch write-through).
                    key: new ValueKey<Guid>(b.TaskId)),
                Ghost: new ScribeFrozenEditorRow(b, Widget.Style)))
            .ToList();

        // Wrapped in a Scrollbar so a tall editor list shows a draggable track (task 8.15). AutoHide off
        // (see read view): permanently visible, matching the native GUI, and avoids the rebuild-driven fade
        // flicker (task 5.7). The empty-state hint check moves INTO the layoutBuilder (mirroring read/pin):
        // checking rows.Count == 0 here (not Widget.Blocks.Count outside) keeps the hint from popping in
        // before the LAST removed row has finished collapsing — so deleting the final row now animates its
        // collapse before the hint appears, consistent with every other surface.
        Widget scrollBody = new ScribeAnimatedList(
            items: items,
            registry: Widget.CollapseRegistry,
            policy: ScribeListRemovalPolicy.Immediate,
            onDepartureSettled: Widget.OnDepartureSettled,
            // Every appearance (add / insert-below / quick-add, and any peer insert) slides in through the
            // container via one uniform motion (animate-row-insertion): the content translates into place +
            // fades, holding the row at FULL height in its slot the whole time — so even the auto-focused new
            // row keeps its caret visible and its clicks exact from the first frame. The entry motion lives
            // ENTIRELY in the container; the editor only supplies the row set.
            layoutBuilder: rows => rows.Count == 0
                // Font family + base size inherited from the tab's DefaultTextStyle ancestor; only color and
                // the centered-wrap overrides are non-default and stay explicit.
                ? new Center(child: new Text(
                    Lang.Get(Widget.HintLangKey),
                    new TextStyle { Color = colors.OnSurfaceVariant, SoftWrap = true, Align = TextAlignment.Center }))
                : new Scrollbar(
                    controller: Widget.ScrollController,
                    child: new SingleChildScrollView(
                        controller: Widget.ScrollController,
                        child: new Column(
                            // spacing 0: all inter-row separation lives in each row's own vertical padding, so
                            // the editor Column matches the read ListView (which adds no inter-row gap) and rows
                            // stay pixel-aligned across a view switch.
                            spacing: 0,
                            crossAxisAlignment: CrossAxisAlignment.Stretch,
                            mainAxisSize: MainAxisSize.Min,
                            children: rows)))
                { AutoHide = false });

        // Root the tab subtree in the player's Task Text Font + window-scaled base size, so the empty
        // hint and the frozen collapsing-ghost rows inherit them (adopt-libgui-31-improvements). The live
        // editable rows use ScribeMultilineField, a custom RenderBox that does NOT read DefaultTextStyle,
        // so it keeps its own explicit fontFamily/fontSize (a deliberate survivor). The footer buttons
        // keep their explicit Caudex button font.
        return ScribeTextDefaults.Wrap(Widget.Style.TaskFontFamily, Widget.Style.FontSize, new Padding(
            EdgeInsets.All(10),
            child: new Column(
                spacing: 8,
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: new Widget[]
                {
                    // Straight edge above the scroll region (scribe-lectern-view-consistency §1).
                    // Dropped on the cuneiform tablet path (add-tablet-clay-type-themes 8.1) — the hard
                    // rule reads wrong against the clay backdrop; the readable path keeps it.
                    Widget.Style.UseCuneiform ? new SizedBox() : new Divider(),
                    // The scroll body keeps its exact height regardless of the add-kind picker: the picker's
                    // kind menu is a FLOATING drop-up that grows OVER this scroll body (see
                    // ScribeAddKindPicker), so nothing here reflows when it opens.
                    new Expanded(child: scrollBody),
                    new Padding(Widget.FooterButtonPadding, child: new Row(
                        spacing: 8,
                        // Center so the (non-Expanded) info Button sits vertically centered against the two
                        // labelled buttons. NOT Stretch: stretch gives the icon button an unbounded axis that a
                        // greedy child (Center/Align) would balloon to fill the whole dialog — an 18px glyph
                        // already renders at ~the 14px label's line height, so natural heights match anyway.
                        crossAxisAlignment: CrossAxisAlignment.Center,
                        mainAxisSize: MainAxisSize.Max,
                        children: BuildFooterButtons(buttonTextStyle, colors, context))),
                })));
    }

    /// <summary>A footer button's label: cuneiform strokes under the single tablet cuneiform branch
    /// (add-tablet-cuneiform-chrome task 5.4), else the normal <see cref="Text"/>. Both share the branch the
    /// rows and title already use (<see cref="ScribeRowStyle.UseCuneiform"/> + bundle threaded from the
    /// tablet), so disabling cuneiform reverts every surface — labels included — in one place. The em size
    /// tracks the label's font size so a cuneiform label reads at the same scale as the readable one; ink is
    /// the button-label color the readable label uses (<c>OnPrimary</c>, dimmed at the tier cap). A null
    /// bundle (asset not loaded) falls back to the readable label so a button is never blank.
    ///
    /// <para>The <see cref="CuneiformText"/> is returned DIRECTLY, with NO <see cref="Center"/>/<see cref="Align"/>
    /// wrapper — exactly like the normal <see cref="Text"/> label and the sibling ⓘ/gear glyphs below. A Center
    /// is a <c>RenderPositionedBox</c> that takes the parent's full <c>MaxHeight</c> when finite; inside the
    /// "Add task" <see cref="Expanded"/> (whose max height is the entire scroll column) that balloons the whole
    /// button to fill the central region — the 2026-08-02 playtest regression this fixes. Both label widgets
    /// self-size to their content, so the button hugs the label at either scale.</para></summary>
    private Widget BuildButtonLabel(string label, TextStyle labelStyle)
    {
        if (Widget.Style.UseCuneiform && Widget.Style.CuneiformBundle is { } bundle)
        {
            return new CuneiformText(
                text: label,
                fontSizeEm: labelStyle.FontSize,
                inkColor: labelStyle.Color,
                bundle: bundle,
                // No glow on button labels (add-tablet-clay-type-themes 8.2): the halo muddies the label
                // against the solid Primary-filled button rather than lifting it. The rows/title keep the
                // per-material glow; only the footer labels render crisp.
                glow: default);
        }
        return new Text(label, labelStyle);
    }

    /// <summary>The footer button row: "Add task", optionally "Done editing", and the trailing ⓘ info
    /// button. The always-edit tablet omits "Done editing" (<see cref="ScribeEditorContent.ShowSwitchToRead"/>
    /// false) since it has no Read view to switch to; the tabbed dialogs keep it.</summary>
    private Widget[] BuildFooterButtons(TextStyle buttonTextStyle, ColorScheme colors, BuildContext context)
    {
        // On the cuneiform path the label is a ratio-boosted CuneiformText (D7), which renders taller than
        // the equivalent TTF label; trim the labelled buttons' vertical padding ~8px (Symmetric vertical
        // 10 → 6) so the taller glyph doesn't inflate the footer button height past its readable-text form.
        // Cuneiform-only: the normal path keeps the default theme padding so the Lectern/Notebook footers
        // are byte-identical (task 8.4).
        const float cuneiformLabelPadY = 6f;
        ButtonStyle? labelButtonStyle = Widget.Style.UseCuneiform
            ? Theme.Of(context).ButtonStyle with { Padding = EdgeInsets.Symmetric(cuneiformLabelPadY, 20) }
            : null;

        // Height parity for the trailing icon (ⓘ / gear) buttons (2026-08-03 feedback: "same height as the
        // taller 'add task' button"). On the cuneiform path the Add-task label is a CuneiformText whose
        // CONTENT height is exactly FontSize × LineHeightRatio (see CuneiformTextRender.PerformLayout), and
        // its button adds cuneiformLabelPadY top+bottom. To match, we force each icon button's content box to
        // that same content height via a FULLY-BOUNDED SizedBox (both axes tight → the inner Center cannot
        // grow on EITHER axis — the balloon regression only happens with an unbounded axis) and give it the
        // same vertical padding, so total heights are equal by construction regardless of the glyph's natural
        // size. On the normal (readable) path we keep the original bare 17f glyph + All(7), so the
        // Lectern/Notebook footers stay byte-identical (task 8.4).
        const float iconGlyphSize = 17f;
        float? iconContentHeight = Widget.Style.UseCuneiform
            ? buttonTextStyle.FontSize * CuneiformMetrics.LineHeightRatio
            : null;
        Widget IconButtonChild(string iconName)
        {
            var glyph = new ScribeVsIconGlyph(iconName, iconGlyphSize, colors.OnPrimary);
            return iconContentHeight is { } h
                // Square, height-locked box centering the glyph so the button matches the label height and
                // stays a square (width == height) rather than the label-shaped landscape box.
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
            // The add control (add-note-kind-picker D1): a segmented button — a primary "Add <kind>" button
            // (defaults to Task, so one click still adds a task) plus a caret that opens a floating drop-up
            // of the kinds (Task / Note). Self-contained (ScribeAddKindPicker owns its selected-kind + open
            // state and its overlay), so it builds its own cuneiform-aware labels + segment styles from
            // Widget.Style. AddTaskEnabled flows through so the primary button + every drop-up tile dim at
            // a finite tier's 10-entry cap but stay clickable (the tap raises the cap notice).
            new Expanded(child: new ScribeAddKindPicker(
                onAdd: Widget.OnAdd,
                addTaskEnabled: Widget.AddTaskEnabled,
                style: Widget.Style,
                // The drop-up menu paints in the Overlay, outside the dialog's ScribeGlobalTint wrap, so the
                // picker re-tints it to match the window (add-note-kind-picker tint fix).
                currentShade: Widget.CurrentShade)),
        };

        if (Widget.ShowSwitchToRead)
        {
            buttons.Add(new Expanded(child: new Button(
                child: BuildButtonLabel(Lang.Get("scribe:scribe-gui-switch-to-read"), buttonTextStyle),
                style: labelButtonStyle,
                onTap: _ => Widget.OnSwitchToRead())));
        }

        buttons.Add(
                            // Trailing info button — a peer of the Add/Done buttons: same LibGUI Button (Primary
                            // variant, so it inherits the identical background, border, padding, and hover/press
                            // feedback), just NON-Expanded so it hugs the right edge at its natural width while
                            // the two labelled buttons split the flexible space. Its child is the scaled-up ⓘ
                            // glyph, set directly (like the siblings' Text — NO Center wrapper, which would
                            // greedily expand the non-Expanded button to fill the dialog) in the button font
                            // color (OnPrimary). Clicking opens the "Scribe Editor Features" handbook page; the
                            // hover tooltip labels it (v1-release-checklist 9.5 — discoverability of Tab/Shift+Tab
                            // row nav). Inline tooltip (not the private WithTooltip helper in ScribeDialogBase),
                            // matching ScribePinnedContent's inline tooltip using the tab theme's OnBackground.
                            ScribeGlobalTint.ShadedTooltip(
                                child: new Button(
                                    // 17f glyph, centered in a height-locked square box on the cuneiform path
                                    // (see IconButtonChild / iconButtonStyle above) so this matches the taller
                                    // Add-task cuneiform label; on the normal path it's the bare glyph + All(7),
                                    // byte-identical to before.
                                    child: IconButtonChild("scribeinfo"),
                                    style: iconButtonStyle,
                                    onTap: _ => Widget.OnOpenEditorReference()),
                                // Shade the whole tooltip — bubble + text — to match the body in low light
                                // (refine-scribe-hover-tooltips D2 + bug-1); same reduced hover strength as the
                                // shared WithTooltip helper.
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

        // Settings gear — tablet only (add-tablet-cuneiform-chrome). The always-edit tablet has no nav
        // column to reach Settings through, so a gear sits just right of the ⓘ info button, styled
        // identically (same non-Expanded Primary Button, tight All(7) padding, 17f OnPrimary glyph). Omitted
        // when OnOpenSettings is null (the tabbed dialogs, which reach Settings via their nav column).
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
/// One editor row: a task checkbox (optimistic self-stateful flip) + the editable multi-line field.
/// Keyed by block index (design D2/D4). The field's focus node is owned by the dialog (passed in) so
/// the dialog can move focus across rows; the field writes its text through to the dialog's scratch
/// document via <see cref="ScribeMultilineField.OnChanged"/>.
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
    }

    public ScribeEditRowData Data { get; }
    public FocusNode? FocusNode { get; }
    public bool AutoFocus { get; }
    /// <summary>True while a drag is in progress and this row is the current drop target — the row
    /// paints a theme-derived darker wash + border so the player sees where the dragged row would land.</summary>
    public bool IsDropTarget { get; }
    /// <summary>True while a drag is in progress and this row is the one being dragged (its origin) — the
    /// row shows a left-pointing (◀) grip glyph and dims to read as lifted. Takes priority over
    /// <see cref="IsDropTarget"/> when the cursor is hovering back over the source row.</summary>
    public bool IsDragSource { get; }
    /// <summary>True while ANY grip-drag is in progress (this row may be neither source nor target).
    /// A non-participating row hides its grip glyph so the list declutters down to just the ◀ source
    /// and ▶ drop-target rows (replace-drag-wash-with-grip-arrows).</summary>
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
    /// <summary>A Tracker row's inline +/- stepper changed its target quantity: (blockIndex, newTarget)
    /// (add-tracker-link-tasks 5.2).</summary>
    public Action<int, int> OnTrackerQuantityChanged { get; }
    public Action<int> OnDragStart { get; }
    public Action<int> OnDragOver { get; }
    public Action OnDragEnd { get; }
    /// <summary>Grip pressed (before any movement). Clears the parent’s tap-suppression so a genuine
    /// tap after a cancelled drag still nests.</summary>
    public Action OnGripPress { get; }
    public Action<int> OnGripTap { get; }
    /// <summary>Non-null only on the tablet (enable-tablet-row-links): clicking a Link/Tracker/Craft row's
    /// name label opens the item's Handbook page, keyed by TaskId. Null elsewhere, so the label stays a plain
    /// (non-clickable) region and non-tablet editor rows render exactly as before.</summary>
    public System.Action<Guid>? OnOpenLink { get; }
    public ScribeRowStyle Style { get; }

    public override State CreateState() => new ScribeEditRowState();
}

internal sealed class ScribeEditRowState : State<ScribeEditRow>
{
    private bool done;
    /// <summary>Pointer position at grip-press, used to start a drag only after movement (D11).</summary>
    private float gripPressX, gripPressY;
    /// <summary>True once this row's grip crossed the drag threshold this press (lost on remount;
    /// <see cref="ScribeEditRow.IsDragSource"/> covers the post-remount case).</summary>
    private bool localGripDragStarted;
    /// <summary>True between this grip's press and release. Hover moves must not start a drag.</summary>
    private bool gripPressed;
    /// <summary>Pixels of movement before a grip press becomes a reorder drag rather than a nest tap.</summary>
    private const float GripDragThreshold = 5f;
    /// <summary>True while the pointer is over this row: the delete and (task-only) pin controls are
    /// hidden until then (lectern-gui-shell "Row icons are hover-conditional"). Tracked with a
    /// row-level <see cref="MouseRegion"/>; hit-testing is innermost-first and enter/exit propagate up
    /// the hierarchy, so this region does NOT steal click-to-focus from the inner field, and during a
    /// grip-drag capture the dispatcher keeps firing enter/exit on the row under the cursor — the same
    /// signal that drives <see cref="ScribeEditorContentState.OnRowDragOver"/>. The grip itself
    /// is NOT hover-gated (it stays mounted so a drag it started can't lose pointer capture mid-move).</summary>
    private bool hovered;

    public override void InitState()
    {
        base.InitState();
        done = Widget.Data.Done;
        // Repaint the row's Container when its field gains/loses focus, so the cuneiform (tablet) path can
        // show a border + background on focus (add-tablet-cuneiform-chrome task 5.1). The field owns its
        // own repaint on focus, but that only rebuilds the field subtree — the enclosing row Container
        // needs its own listener to react. The focus node is dialog-owned (shared with the field); a
        // ChangeNotifier accepts multiple listeners, so this coexists with the dialog's OnRowFocusChanged.
        Widget.FocusNode?.AddListener(OnFieldFocusChanged);
    }

    /// <summary>Move the focus listener if a rebuild handed this row a different focus node instance (a
    /// given row index keeps its node across rebuilds, so this is belt-and-suspenders), and resync the
    /// optimistic <see cref="done"/> when an external change flips this row's authoritative completion.
    /// The resync mirrors <c>ScribeReadRowState</c>/<c>ScribePinRowState</c>: the editor reconciles rows in
    /// place (keyed by TaskId) rather than <c>ForceRebuild</c>, so when an external completion is synced
    /// into scratch (<see cref="ScribeDialogBase.RefreshReadView"/>, editor-mode branch) this row is REUSED
    /// and <see cref="InitState"/>'s seed would otherwise stay stale, leaving the checkbox out of date. Gate
    /// on the authoritative value actually CHANGING so a pure chrome reconcile doesn't stomp an in-flight
    /// optimistic tick the player just made locally (sync-editor-view-on-external-completion).</summary>
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

    /// <summary>The editor content of a Tracker/Link row: the referenced item's icon + name in place of the
    /// text field (the block's own Text is empty by design), and — for a Tracker — an inline +/- stepper that
    /// edits its <see cref="ScribeBlock.TargetQuantity"/> (add-tracker-link-tasks 5.2). The stepper is an
    /// uncontrolled <see cref="ScribeNumericField"/> seeded from the current target: the editor reconciles rows
    /// in place (UpdateWidget, not remount) and the target's ONLY editor-side mutation IS this stepper, so it
    /// stays consistent without a ValueKey remount (which would drop focus on every step). The count engine
    /// changes a Tracker's CurrentQuantity in the READ view, never its target here. A Link edits nothing inline
    /// — it's just the icon + name; its target page is opened from the read view (5.3). The icon/name are the
    /// dialog-resolved snapshot on the row data, so this stays capi-free.</summary>
    private Widget BuildItemEditorContent(ColorScheme colors, ScribeRowStyle style, int index)
    {
        float iconSize = ScribeRowConstants.ItemIconSize
            * (style.ControlSize / ScribeRowConstants.RowCheckboxSize);
        float iconVisual = ScribeLinkIcon.VisualSize(iconSize, Widget.Data.LinkTarget);
        float stepperHeight = Widget.Data.IsCarriedCountTracked ? style.ControlSize * 1.15f : 0f;
        float bandHeight = MathF.Max(iconVisual, stepperHeight);
        var rowChildren = new List<Widget>();

        if (Widget.Data.IsCarriedCountTracked)
        {
            // Inline target-quantity stepper, placed at the LEFT of the row (feedback 6.3): the row's
            // hover-revealed delete/pin buttons float over the RIGHT edge, so a right-hand stepper sat
            // UNDER them and was unreachable (which also made the pin untappable — feedback 6.9).
            // Height is ControlSize×1.15 (a hair taller than the checkbox); width is that height×2.
            // No translate on the stepper — Tracker/Craft keep the field in its layout box. Centered in
            // the icon band when the name is one line; stays at the top when the name wraps.
            // clamp keeps the target a whole number ≥ 1 (matching the Core setter); onChanged writes the
            // rounded int through to scratch via the dialog.
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

        // Guide-page book glyph tinted with the link accent (feedback 7.11d) — Primary on light surfaces, or
        // the row's override where Primary is illegible on a dark surface (Chalkboard slate; see
        // ScribeRowStyle.LinkColor). Row-height-neutral (7.11e/7.11f); the item icon ignores the color. The
        // Tracker's stepper still drives this editor row's height by design.
        float lineHeight = ScribeRowControlNudge.TextLineHeight(style.FontSize);
        rowChildren.Add(ScribeCenterIfShort.InBand(
            ScribeLinkIcon.Build(Widget.Data.DisplayStack, Widget.Data.LinkTarget, iconSize, style.LinkColor ?? colors.Primary, lineHeight, heightNeutral: false),
            bandHeight));

        // The item name is a hyperlink ONLY where the surface opts its editor rows into link activation (the
        // tablet — enable-tablet-row-links, which has no read view). Wrap it in the same GestureDetector shape the
        // read row uses (ScribeReadContent.cs:362) and render it in the link accent so it reads as tappable, matching
        // the read view. The sibling ScribeNumericField (the Tracker/Craft +/- stepper added above) is a separate
        // widget, so the number stays an INDEPENDENT hit region that keeps editing the target quantity — clicking the
        // number reaches the field, clicking the name reaches this gesture. When OnOpenLink is null (every non-tablet
        // editor), the label is a plain OnSurface region and this row renders byte-identical to before. Only item-kind
        // rows (Link/Tracker/Craft) reach this method, so a plain Task/Note's editable text is never wrapped.
        Widget nameLabel = Widget.OnOpenLink is { } openLink
            ? new GestureDetector(
                onPress: e => { e.Handled = true; openLink(Widget.Data.TaskId); },
                child: ScribeItemLabel.Build(Widget.Data.Label, style.LinkColor ?? colors.Primary, style))
            : ScribeItemLabel.Build(Widget.Data.Label, colors.OnSurface, style);
        rowChildren.Add(new Expanded(child: ScribeCenterIfShort.Name(nameLabel, style, bandHeight)));

        // No left FieldPadX: a Task row's field BOX starts immediately after the checkbox gap, and the
        // Tracker stepper must line up with that edge (playtest: FieldPadX indented the numeric box).
        // Keep the right inset so wrapped names don't run into the hover pin/delete.
        return new Padding(
            EdgeInsets.Only(right: style.FieldPadX),
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

        // Grip on the FAR LEFT of the row (2026-07-24 feedback). Always present (not hover-gated): it
        // stays mounted so a drag it started can't lose the dispatcher's pointer capture mid-move.
        // onPress/onMove-hover(row)/onRelease drive the reorder. Nudged down to center on a one-line
        // input (see ScribeRowControlNudge); the top margin sits OUTSIDE the GestureDetector so the
        // drag hit-target still covers the visible glyph. The negative right inset cancels the Row's
        // CheckboxTextGap that would otherwise sit between the grip and the checkbox, so the grip's
        // trailing margin is 0 and the text column reclaims that width (§10.4). See ScribeRowGripInsets.
        //
        // The grip glyph is state-driven during a drag (replace-drag-wash-with-grip-arrows): the drag
        // feedback lives on this glyph now, not on the row background — the darkened-Primary drop-target
        // wash collided visually with the strengthened Secondary pinned tint. Source (grabbed) row → ◀
        // (muted OnSurfaceVariant); the prospective drop row under the cursor → ▶ (accent Primary); any
        // OTHER row while a drag is in progress → a same-size empty box (glyph hidden) so the list
        // declutters to just the ◀/▶ pair while the grip column width is unchanged (no mid-drag reflow).
        // Source wins over drop-target when the cursor hovers back over the origin row.
        // Ink for the grip + its ◀/▶ arrows: the tablet supplies a darker material ink (matching the
        // title-bar grip/pencil) via style.GripGlyphColor so the handle reads as engraved on the clay;
        // other surfaces fall back to the theme's mid-gray OnSurfaceVariant.
        Vector4 gripColor = style.GripGlyphColor ?? colors.OnSurfaceVariant;
        Widget gripGlyph =
            Widget.IsDragSource ? new ScribeVsIconGlyph("scribetriangleleft", style.ControlSize, gripColor)
            : Widget.IsDropTarget ? new ScribeVsIconGlyph("scribetriangleright", style.ControlSize, colors.Primary)
            : Widget.DragActive ? new SizedBox(width: style.ControlSize, height: style.ControlSize)
            : new ScribeVsIconGlyph("scribegrip", style.ControlSize, gripColor);

        children.Add(new Padding(
            ScribeRowControlNudge.GripInsets(style, Widget.Data.IsItemKind),
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

        // Source-row "lifted / in-hand" dim (replace-drag-wash-with-grip-arrows): while THIS row is the one
        // being dragged, its CONTENT (checkbox + text) paints at ~half opacity so the row reads as picked up.
        // The dim is applied per-child, NOT to the whole row — the grip column stays full opacity so the ◀
        // source arrow keeps its ink (the arrow IS the "you grabbed this" signal; dimming it would defeat it).
        // Opacity is paint-only and ALWAYS present (value flips 1.0↔0.5) so no widget-type swap mid-drag.
        float contentOpacity = Widget.IsDragSource ? 0.5f : 1f;

        // Task, Tracker, and Link all carry a Done flag (Completable), so all three show a completion
        // checkbox; only a freeform Text section doesn't (add-tracker-link-tasks Group 5).
        if (Widget.Data.Completable)
        {
            children.Add(new Opacity(contentOpacity, child: new Padding(
                EdgeInsets.Only(top: ScribeRowControlNudge.CheckboxAndGripTop(style, Widget.Data.IsItemKind)),
                child: ScribeRowControlNudge.BuildTaskCheckbox(
                    context, style, done,
                    _ =>
                    {
                        SetState(() => done = !done);
                        Widget.OnToggleTask(index);
                    }))));
        }

        // A Tracker/Link row has no editable text field — its content is the referenced item's icon + name,
        // and a Tracker additionally carries an inline +/- stepper for its target quantity
        // (add-tracker-link-tasks 5.2/5.3). The block's own Text is empty by design; the item is resolved by
        // the dialog and passed in on the row data, so this stays capi-free. Dimmed with the same
        // contentOpacity as a text row so a drag reads identically.
        if (Widget.Data.IsItemKind)
        {
            children.Add(new Expanded(child: new Opacity(contentOpacity,
                child: BuildItemEditorContent(colors, style, index))));
        }
        else
        {

        // Ghost hint only on TASK rows (add-empty-task-lifecycle D6b) — a text section is legitimately
        // empty and needs no "New task…" prompt. Painted dimmed while the field is empty; not committed.
        string placeholder = Widget.Data.IsTask ? Lang.Get("scribe:scribe-gui-newtask-placeholder") : "";

        children.Add(new Expanded(child: new Opacity(contentOpacity, child: new ScribeMultilineField(
            initialText: Widget.Data.Text,
            placeholder: placeholder,
            focusNode: Widget.FocusNode,
            fontSize: style.FontSize,
            fontFamily: ScribeTaskFont.Resolve(style.TaskFontFamily),
            padX: style.FieldPadX,
            padY: style.FieldPadY,
            // Focus-border color for this row's field: null on light surfaces (→ theme Primary), a chalk-white
            // on the Chalkboard so the focused task field doesn't outline in the forest-green Primary the
            // author disliked (ScribeRowStyle.InputFocusBorderColor, seeded from the dialog seam).
            focusBorderColor: style.InputFocusBorderColor,
            autoFocus: Widget.AutoFocus,
            // Tablet-only: render/edit this row as live cuneiform strokes (add-tablet-cuneiform-chrome).
            // Both flags are off for the Lectern/Notebook rows and for the disable-cuneiform fallback, so
            // those surfaces stay on the normal editable renderer.
            useCuneiform: style.UseCuneiform,
            cuneiformBundle: style.CuneiformBundle,
            cuneiformJitter: style.UseCuneiform ? style.CuneiformJitter : 0f,
            // Whole-character tilt (tune-tablet-jitter-add-rotation), tablet path only. Shares the per-row
            // TaskId seed with the jitter, but its SeedFor omits the stroke ordinal so every stroke of a
            // character tilts as one rigid unit (independent stream from the jitter's per-stroke wobble).
            cuneiformRotation: style.UseCuneiform ? style.CuneiformRotation : 0f,
            // Fixed per-row seed from the stable TaskId, so a row's letters wobble consistently and typing a
            // new character does not reseed (re-wobble) the letters already pressed into the row.
            cuneiformJitterSeed: Widget.Data.TaskId.GetHashCode(),
            cuneiformProgression: style.UseCuneiform && style.CuneiformProgression,
            cuneiformGlow: style.UseCuneiform ? style.CuneiformGlow : default,
            cuneiformStrokeWeightScale: style.UseCuneiform ? style.CuneiformStrokeWeightScale : 1f,
            // Each kind is held to its own maxlength as a live affordance: Task rows to the soft task cap,
            // Note rows to the larger freeform cap. The codec clips both on read regardless, so this is the
            // UX half of the same limit (RELEASE.md A1). Hitting the cap fires OnMaxLengthReached below so the
            // dialog can explain it.
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

        // Row body: [grip][checkbox][text]. Delete/pin no longer reserve columns here — they float on
        // top of the row (see below), so the text can use the full width. A Depth-1 subtask adds a left
        // inset so it reads as nested under its parent (task-subtasks 5.1); depth-0 rows are unchanged.
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

        // Resting pinned tint, drawn behind the row content. The Container is ALWAYS present (fill may be
        // transparent Vector4.Zero) — see the structural-stability note below — so toggling the fill is a
        // cheap property update, not a widget-type swap.
        //
        // Drag feedback is NO LONGER a row-background wash (replace-drag-wash-with-grip-arrows): the old
        // source/drop-target washes (brightened/darkened Primary) collided with the strengthened Secondary
        // pinned tint, so drag signalling moved entirely onto the grip glyph (◀/▶ above) plus a source-row
        // opacity dim (below). The Container now carries ONLY the resting pinned tint, leaving nothing on
        // the row background that could collide with the pinned wash during a drag.
        //
        // Focus chrome is NOT drawn here either — on the cuneiform (tablet) path the focused input draws its
        // own SurfaceHigh+Primary box scoped to the input element (see ScribeMultilineField's cuneiform
        // render widget), matching the normal Lectern/Notebook path. That keeps a focused input on a PINNED
        // row distinct from the pinned wash — a small bordered input inside the row tint, two shapes, not one
        // ambiguous whole-row fill (scope-focus-affordance-to-input, playtest fail f640f9ab). The Container
        // is still ALWAYS present with transparent defaults so the widget type never swaps and the field's
        // live caret/text survive the repaint.
        Vector4 rowFill =
            Widget.Data.Completable && Widget.Data.Pinned ? ScribeRowConstants.PinnedTint(colors) : Vector4.Zero;

        rowBody = new Container(
            style: new BoxStyle
            {
                Color = rowFill,
                BorderColor = Vector4.Zero,
                BorderThickness = 0f,
                CornerRadius = Vector4.Zero,
            },
            child: rowBody);

        // Delete + pin float on the RIGHT of the row as real buttons (2026-07-24 feedback), shown only
        // on hover. A Stack sizes to the non-positioned rowBody and lays the Positioned buttons on top,
        // so they overlay the text's right edge without reserving a column or reflowing it. Pin is
        // task-only. Right-anchored, pin left of delete.
        //
        // STRUCTURAL STABILITY (fixes the hover-reverts-in-progress-text bug): the row is ALWAYS a
        // Stack whose index-0 child is ALWAYS this Container(rowBody), regardless of hover/pin/drop
        // state. LibGUI reconciles by (type + key + position): if hover instead swapped the MouseRegion's
        // child between a bare `rowBody` and a `Stack` (different types), the reconciler would unmount the
        // whole subtree — including the row's ScribeMultilineField, destroying its State and the live
        // caret/text-in-progress, then remount a fresh field re-seeded from the STALE Data.Text snapshot
        // (the field writes through on each keystroke but never triggers an editor rebuild). That made
        // moving the mouse over a row appear to revert unsaved edits. Keeping index 0 identical lets the
        // field UPDATE in place; only the trailing Positioned buttons mount/unmount on hover.
        var stackChildren = new List<Widget> { rowBody };
        if (hovered)
        {
            float btn = style.ControlSize;
            float gap = 4f;
            // Actual drawn box width of a button (ScribeRowButton shrinks its chrome by BoxShrink); used
            // to space the pin against the delete's real edge, not its nominal column.
            float boxW = btn - ScribeRowButton.BoxShrink;
            // Right inset: resting gap +1px (2026-07-24 feedback). Top is the COMPUTED centering offset
            // (see ScribeRowControlNudge.FloatingButtonTop), which now vertically centers the button box
            // on the one-line input at any font scale rather than the old font-15 constant.
            float btnRight = gap + 1f;
            float btnTop = ScribeRowControlNudge.FloatingButtonTop(style);
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
                    iconScale: 1.15f))); // pin glyph +15% (§10.2)
        }

        Widget row = new Stack(stackChildren);

        // Row-level hover tracking (see `hovered`). onEnter/onExit reveal the hover-conditional
        // controls; the same enter events during a drag are forwarded as the drop-target signal
        // (the parent ignores OnDragOver unless a drag is active), so one region serves both.
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

// ============================================================================
// Pin Tab content (scribe-pin-editor)
// ============================================================================

/// <summary>A value snapshot of one Pin Tab row: the pin's stable identity, its displayed done-state, and
/// the text to seed the field with (the live edit buffer if one is in flight, else the server snapshot).
/// Carries no live pin reference — a resync rebuilds instead.</summary>
