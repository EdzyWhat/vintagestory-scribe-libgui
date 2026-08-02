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
using Gui.Widgets.Gestures;      // ScrollController
using Gui.Widgets.Layout;        // Column, Row, Expanded, Padding, SizedBox, Center, Align, Alignment, CrossAxisAlignment, MainAxisAlignment
using Gui.Widgets.Overlay;       // Tooltip
using Gui.Widgets.Painting;      // BoxStyle
using Gui.Widgets.Scroll;        // ListView, SingleChildScrollView, Scrollable, Scrollbar
using Gui.Core.Layout;           // MainAxisSize
using OpenTK.Mathematics;        // Vector2
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;   // Lang, GlobalConstants
using Vintagestory.API.MathTools;  // BlockPos

namespace Scribe;

internal readonly record struct ScribeEditRowData(int Index, bool IsTask, bool Done, bool Pinned, Guid TaskId, string Text);

/// <summary>
/// A static, non-interactive snapshot of a deleted editor row, shown while it collapses out of the list
/// (scribe-list-collapse). The row's scratch block and focus node are already gone, so this renders a
/// FROZEN copy — the same [grip-spacer][checkbox][text] column an editor row uses (so it aligns and
/// collapses seamlessly), but with no editable field, no gestures, and no delete/pin/drag controls. It is
/// never focused and never mutates anything; the <see cref="ScribeCollapsible"/> wrapping it animates its
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
                ScribeRowControlNudge.GripInsets(style),
                child: new Opacity(
                    opacity: 0f,
                    child: new ScribeVsIconGlyph("scribegrip", style.ControlSize, colors.OnSurfaceVariant))),
        };

        if (data.IsTask)
        {
            // A frozen (disabled) checkbox reflecting the row's last done-state — no onChanged, so it can't
            // be toggled while it collapses.
            children.Add(new Padding(
                EdgeInsets.Only(top: ScribeRowControlNudge.CheckboxAndGripTop(style)),
                child: new Checkbox(value: data.Done, onChanged: null, size: style.CheckboxSize)));
        }

        // Inset the text by the editor field's internal padding so the frozen row's text sits exactly where
        // the live field's text did (matches ScribeReadRow), avoiding a horizontal jump as it collapses.
        children.Add(new Expanded(child: new Padding(
            EdgeInsets.Symmetric(vertical: style.FieldPadY, horizontal: style.FieldPadX),
            child: new Text(data.Text, textStyle))));

        Widget rowBody = new Padding(
            EdgeInsets.Symmetric(vertical: style.RowVerticalPadding, horizontal: style.RowHorizontalPadding),
            child: new Row(
                spacing: style.CheckboxTextGap,
                crossAxisAlignment: CrossAxisAlignment.Start,
                mainAxisSize: MainAxisSize.Max,
                children: children));

        if (data.IsTask && data.Pinned)
        {
            rowBody = new Container(
                style: new BoxStyle { Color = ScribeRowConstants.PinnedTint(colors) },
                child: rowBody);
        }

        return rowBody;
    }
}

/// <summary>A deleted editor row that is collapsing out of the list (scribe-list-collapse): its last-known
/// data snapshot and the DISPLAY index it should collapse in place at.</summary>
internal readonly record struct ScribeDepartingEditorRow(ScribeEditRowData Row, int Index);

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
        Action<int> onToggleTask,
        Action<int> onDeleteBlock,
        Action<int> onTogglePinned,
        Action<int, int> onReorderBlock,
        Action onAddTask,
        Action onSwitchToRead,
        Action onOpenEditorReference,
        Action? onOpenSettings,
        EdgeInsets footerButtonPadding,
        ScribeRowStyle style,
        ScrollController scrollController,
        IReadOnlyList<ScribeDepartingEditorRow> departingRows,
        ScribeCollapseRegistry collapseRegistry,
        Action<Guid> onDepartingCollapsed,
        string hintLangKey = "scribe:scribe-gui-edit-hint",
        bool addTaskEnabled = true,
        bool showSwitchToRead = true)
    {
        Blocks = blocks;
        FocusNodes = focusNodes;
        AutoFocusIndex = autoFocusIndex;
        OnTextChanged = onTextChanged;
        OnCommitAndAdvance = onCommitAndAdvance;
        OnCommitAndRetreat = onCommitAndRetreat;
        OnInsertTaskBelow = onInsertTaskBelow;
        OnRowBlurred = onRowBlurred;
        OnToggleTask = onToggleTask;
        OnDeleteBlock = onDeleteBlock;
        OnTogglePinned = onTogglePinned;
        OnReorderBlock = onReorderBlock;
        OnAddTask = onAddTask;
        OnSwitchToRead = onSwitchToRead;
        OnOpenEditorReference = onOpenEditorReference;
        OnOpenSettings = onOpenSettings;
        FooterButtonPadding = footerButtonPadding;
        Style = style;
        ScrollController = scrollController;
        DepartingRows = departingRows;
        CollapseRegistry = collapseRegistry;
        OnDepartingCollapsed = onDepartingCollapsed;
        HintLangKey = hintLangKey;
        AddTaskEnabled = addTaskEnabled;
        ShowSwitchToRead = showSwitchToRead;
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
    public Action<int> OnToggleTask { get; }
    public Action<int> OnDeleteBlock { get; }
    public Action<int> OnTogglePinned { get; }
    /// <summary>Reorder a block from one index to another (drag drop). See
    /// <see cref="ScribeEditorContentState"/> for the drag mechanics.</summary>
    public Action<int, int> OnReorderBlock { get; }
    public Action OnAddTask { get; }
    /// <summary>Whether the footer "Add task" button is enabled. Default true (uncapped tiers — Lectern,
    /// Notebook — always). The tablet tier passes false once its document holds the max task blocks
    /// (scribe-document-policy), which dims the button and makes its tap a no-op so the 10-task cap is a
    /// visible affordance, not just a silent backstop.</summary>
    public bool AddTaskEnabled { get; }
    public Action OnSwitchToRead { get; }
    /// <summary>Whether the footer shows the "Done editing" (switch-to-read) button. Default true for the
    /// tabbed dialogs (Lectern/Notebook), which have a Read view to switch to. The always-edit tablet
    /// (add-tablet-dialog) passes false: it has no Read view, so leaving editor mode would null the scratch
    /// and there would be nowhere to land — the button is simply omitted there.</summary>
    public bool ShowSwitchToRead { get; }
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
    /// <summary>Deleted rows still collapsing out of the list (scribe-list-collapse), spliced back in at
    /// their display index as static, non-interactive ghosts by <see cref="ScribeEditorContentState.Build"/>.</summary>
    public IReadOnlyList<ScribeDepartingEditorRow> DepartingRows { get; }
    /// <summary>Host-owned collapse controllers for the departing rows (keyed by TaskId), so a collapse
    /// resumes across the dialog's ForceRebuild remounts.</summary>
    public ScribeCollapseRegistry CollapseRegistry { get; }
    /// <summary>Fired (with the row's TaskId) when a departing row's collapse completes, so the dialog can
    /// remove its ghost and re-clamp the scroll extent.</summary>
    public Action<Guid> OnDepartingCollapsed { get; }
    public string HintLangKey { get; }

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

    /// <summary>Grip pressed: begin a drag from this row. The event dispatcher auto-captures the grip's
    /// element on press, so the subsequent moves/release keep arriving here even as the cursor crosses
    /// sibling rows (the same mechanism Scrollbar's thumb relies on).</summary>
    private void OnRowDragStart(int index)
    {
        SetState(() =>
        {
            dragFromIndex = index;
            dragOverIndex = index;
        });
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
        if (dragFromIndex is { } from && dragOverIndex is { } to)
        {
            dragFromIndex = null;
            dragOverIndex = null;
            Widget.OnReorderBlock(from, to); // no-op inside if from == to
        }
        else
        {
            SetState(() => { dragFromIndex = null; dragOverIndex = null; });
        }
    }

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        TextStyle buttonTextStyle = new() { FontSize = 14, Color = colors.OnPrimary, FontFamily = ScribeTaskFont.ButtonFamily };

        Widget scrollBody;
        if (Widget.Blocks.Count == 0)
        {
            // Font family + base size inherited from the tab's DefaultTextStyle ancestor; only color and
            // the centered-wrap overrides are non-default and stay explicit.
            scrollBody = new Center(child: new Text(
                Lang.Get(Widget.HintLangKey),
                new TextStyle { Color = colors.OnSurfaceVariant, SoftWrap = true, Align = TextAlignment.Center }));
        }
        else
        {
            var rows = Widget.Blocks
                .Select(b => (Widget)new ScribeEditRow(
                    data: b,
                    focusNode: b.Index < Widget.FocusNodes.Count ? Widget.FocusNodes[b.Index] : null,
                    autoFocus: Widget.AutoFocusIndex == b.Index,
                    isDropTarget: dragFromIndex is not null && dragOverIndex == b.Index,
                    // The origin row of the in-progress drag (null-safe: false when no drag is active). The
                    // row's Build gives this white-wash priority over the drop-target black wash when the
                    // cursor hovers back over the source.
                    isDragSource: dragFromIndex == b.Index,
                    onTextChanged: Widget.OnTextChanged,
                    onCommitAndAdvance: Widget.OnCommitAndAdvance,
                    onCommitAndRetreat: Widget.OnCommitAndRetreat,
                    onInsertTaskBelow: Widget.OnInsertTaskBelow,
                    onRowBlurred: Widget.OnRowBlurred,
                    onToggleTask: Widget.OnToggleTask,
                    onDelete: Widget.OnDeleteBlock,
                    onTogglePinned: Widget.OnTogglePinned,
                    onDragStart: OnRowDragStart,
                    onDragOver: OnRowDragOver,
                    onDragEnd: OnRowDragEnd,
                    style: Widget.Style,
                    key: new ValueKey<int>(b.Index)))
                .ToList();

            // Splice each deleted-but-collapsing row back in at the display index it held, as a static,
            // non-interactive ghost that collapses its height to zero then removes itself
            // (scribe-list-collapse). Its scratch block + focus node are gone, so it renders as a frozen
            // read-style row (no field, no drag/delete controls) wrapped in ScribeCollapsible. Keyed by
            // TaskId (OUTSIDE the collapsible) so its identity is stable across rebuilds and never collides
            // with a live index-keyed row. Insert ascending, clamped to the current list length.
            foreach (var d in Widget.DepartingRows.OrderBy(d => d.Index))
            {
                int at = Math.Clamp(d.Index, 0, rows.Count);
                Guid taskId = d.Row.TaskId;
                rows.Insert(at, new ScribeCollapsible(
                    id: taskId.ToString("N"),
                    collapsing: true,
                    registry: Widget.CollapseRegistry,
                    onCollapsed: () => Widget.OnDepartingCollapsed(taskId),
                    child: new ScribeFrozenEditorRow(d.Row, Widget.Style),
                    key: new ValueKey<Guid>(taskId)));
            }

            // Wrapped in a Scrollbar so a tall editor list shows a draggable track (task 8.15). AutoHide
            // off (see read view): permanently visible, matching the native GUI, and avoids the
            // ForceRebuild-driven fade flicker (task 5.7).
            scrollBody = new Scrollbar(
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
            { AutoHide = false };
        }

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
                    new Divider(),
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
                bundle: bundle);
        }
        return new Text(label, labelStyle);
    }

    /// <summary>The footer button row: "Add task", optionally "Done editing", and the trailing ⓘ info
    /// button. The always-edit tablet omits "Done editing" (<see cref="ScribeEditorContent.ShowSwitchToRead"/>
    /// false) since it has no Read view to switch to; the tabbed dialogs keep it.</summary>
    private Widget[] BuildFooterButtons(TextStyle buttonTextStyle, ColorScheme colors, BuildContext context)
    {
        var buttons = new List<Widget>
        {
            // "Add task": dimmed + inert once the tier cap is reached (tablet at 10 tasks);
            // uncapped tiers always pass AddTaskEnabled=true, so this renders exactly as before.
            new Expanded(child: new Button(
                child: BuildButtonLabel(
                    Lang.Get("scribe:scribe-gui-addtask"),
                    Widget.AddTaskEnabled
                        ? buttonTextStyle
                        : buttonTextStyle with { Color = colors.OnPrimary with { W = 0.4f } }),
                onTap: Widget.AddTaskEnabled ? _ => Widget.OnAddTask() : null)),
        };

        if (Widget.ShowSwitchToRead)
        {
            buttons.Add(new Expanded(child: new Button(
                child: BuildButtonLabel(Lang.Get("scribe:scribe-gui-switch-to-read"), buttonTextStyle),
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
                            new Tooltip(
                                child: new Button(
                                    // 17f (nudged down from 18) so the glyph's rendered content matches the
                                    // label text's height rather than overshooting it.
                                    child: new ScribeVsIconGlyph("scribeinfo", 17f, colors.OnPrimary),
                                    // Copy the theme button style but replace the default label padding
                                    // (Symmetric(10, 20)) with a tight, equal All(7): equal on all sides keeps
                                    // the square glyph in a square box (not the label-shaped landscape one), and
                                    // the reduced amount brings total height (~38px) in line with the Add/Done
                                    // buttons — the glyph content renders taller than label text, so it needs
                                    // less padding to match, not the same.
                                    style: Theme.Of(context).ButtonStyle with { Padding = EdgeInsets.All(7) },
                                    onTap: _ => Widget.OnOpenEditorReference()),
                                content: new Padding(
                                    EdgeInsets.All(6),
                                    child: new Text(Lang.Get("scribe:scribe-gui-editor-reference-tooltip"), new TextStyle
                                    {
                                        FontSize = 13,
                                        SoftWrap = true,
                                        Color = colors.OnBackground,
                                    })),
                                useGlobalOverlay: true));

        // Settings gear — tablet only (add-tablet-cuneiform-chrome). The always-edit tablet has no nav
        // column to reach Settings through, so a gear sits just right of the ⓘ info button, styled
        // identically (same non-Expanded Primary Button, tight All(7) padding, 17f OnPrimary glyph). Omitted
        // when OnOpenSettings is null (the tabbed dialogs, which reach Settings via their nav column).
        if (Widget.OnOpenSettings is { } openSettings)
        {
            buttons.Add(new Tooltip(
                child: new Button(
                    child: new ScribeVsIconGlyph("scribegear", 17f, colors.OnPrimary),
                    style: Theme.Of(context).ButtonStyle with { Padding = EdgeInsets.All(7) },
                    onTap: _ => openSettings()),
                content: new Padding(
                    EdgeInsets.All(6),
                    child: new Text(Lang.Get("scribe:scribe-gui-nav-settings"), new TextStyle
                    {
                        FontSize = 13,
                        SoftWrap = true,
                        Color = colors.OnBackground,
                    })),
                useGlobalOverlay: true));
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
        Action<int, string> onTextChanged,
        Action<int> onCommitAndAdvance,
        Action<int> onCommitAndRetreat,
        Action<int> onInsertTaskBelow,
        Action<int> onRowBlurred,
        Action<int> onToggleTask,
        Action<int> onDelete,
        Action<int> onTogglePinned,
        Action<int> onDragStart,
        Action<int> onDragOver,
        Action onDragEnd,
        ScribeRowStyle style,
        Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        Data = data;
        FocusNode = focusNode;
        AutoFocus = autoFocus;
        IsDropTarget = isDropTarget;
        IsDragSource = isDragSource;
        OnTextChanged = onTextChanged;
        OnCommitAndAdvance = onCommitAndAdvance;
        OnCommitAndRetreat = onCommitAndRetreat;
        OnInsertTaskBelow = onInsertTaskBelow;
        OnRowBlurred = onRowBlurred;
        OnToggleTask = onToggleTask;
        OnDelete = onDelete;
        OnTogglePinned = onTogglePinned;
        OnDragStart = onDragStart;
        OnDragOver = onDragOver;
        OnDragEnd = onDragEnd;
        Style = style;
    }

    public ScribeEditRowData Data { get; }
    public FocusNode? FocusNode { get; }
    public bool AutoFocus { get; }
    /// <summary>True while a drag is in progress and this row is the current drop target — the row
    /// paints a theme-derived darker wash + border so the player sees where the dragged row would land.</summary>
    public bool IsDropTarget { get; }
    /// <summary>True while a drag is in progress and this row is the one being dragged (its origin) — the
    /// row paints a theme-derived brighter wash + border so the player sees where the dragged row came
    /// from. Takes priority over <see cref="IsDropTarget"/> when the cursor is hovering back over the
    /// source row.</summary>
    public bool IsDragSource { get; }
    public Action<int, string> OnTextChanged { get; }
    public Action<int> OnCommitAndAdvance { get; }
    public Action<int> OnCommitAndRetreat { get; }
    public Action<int> OnInsertTaskBelow { get; }
    public Action<int> OnRowBlurred { get; }
    public Action<int> OnToggleTask { get; }
    public Action<int> OnDelete { get; }
    public Action<int> OnTogglePinned { get; }
    public Action<int> OnDragStart { get; }
    public Action<int> OnDragOver { get; }
    public Action OnDragEnd { get; }
    public ScribeRowStyle Style { get; }

    public override State CreateState() => new ScribeEditRowState();
}

internal sealed class ScribeEditRowState : State<ScribeEditRow>
{
    private bool done;
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
    /// given row index keeps its node across rebuilds, so this is belt-and-suspenders).</summary>
    public override void UpdateWidget(ScribeEditRow oldWidget)
    {
        base.UpdateWidget(oldWidget);
        if (!ReferenceEquals(oldWidget.FocusNode, Widget.FocusNode))
        {
            oldWidget.FocusNode?.RemoveListener(OnFieldFocusChanged);
            Widget.FocusNode?.AddListener(OnFieldFocusChanged);
        }
    }

    private void OnFieldFocusChanged() => SetState(() => { });

    public override void Dispose()
    {
        Widget.FocusNode?.RemoveListener(OnFieldFocusChanged);
        base.Dispose();
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
        children.Add(new Padding(
            ScribeRowControlNudge.GripInsets(style),
            child: new GestureDetector(
                onPress: _ => Widget.OnDragStart(index),
                onRelease: _ => Widget.OnDragEnd(),
                child: new ScribeVsIconGlyph("scribegrip", style.ControlSize, colors.OnSurfaceVariant))));

        if (Widget.Data.IsTask)
        {
            children.Add(new Padding(
                EdgeInsets.Only(top: ScribeRowControlNudge.CheckboxAndGripTop(style)),
                child: new Checkbox(
                    value: done,
                    onChanged: _ =>
                    {
                        SetState(() => done = !done);
                        Widget.OnToggleTask(index);
                    },
                    size: style.CheckboxSize)));
        }

        // Ghost hint only on TASK rows (add-empty-task-lifecycle D6b) — a text section is legitimately
        // empty and needs no "New task…" prompt. Painted dimmed while the field is empty; not committed.
        string placeholder = Widget.Data.IsTask ? Lang.Get("scribe:scribe-gui-newtask-placeholder") : "";

        children.Add(new Expanded(child: new ScribeMultilineField(
            initialText: Widget.Data.Text,
            placeholder: placeholder,
            focusNode: Widget.FocusNode,
            fontSize: style.FontSize,
            fontFamily: ScribeTaskFont.Resolve(style.TaskFontFamily),
            padX: style.FieldPadX,
            padY: style.FieldPadY,
            autoFocus: Widget.AutoFocus,
            // Tablet-only: render/edit this row as live cuneiform strokes (add-tablet-cuneiform-chrome).
            // Both flags are off for the Lectern/Notebook rows and for the disable-cuneiform fallback, so
            // those surfaces stay on the normal editable renderer.
            useCuneiform: style.UseCuneiform,
            cuneiformBundle: style.CuneiformBundle,
            // Task rows are held to the soft task cap as a maxlength affordance; freeform Text sections
            // stay uncapped in-editor (bounded only by the codec's larger hard limit). The codec clips
            // Task text on read regardless, so this is the UX half of the same limit (RELEASE.md A1).
            maxLength: Widget.Data.IsTask ? ScribeDocumentCodec.MaxTaskTextLength : (int?)null,
            onChanged: text => Widget.OnTextChanged(index, text),
            onCommitAndAdvance: () => Widget.OnCommitAndAdvance(index),
            onCommitAndRetreat: () => Widget.OnCommitAndRetreat(index),
            onInsertTaskBelow: () => Widget.OnInsertTaskBelow(index),
            onBlur: () => Widget.OnRowBlurred(index))));

        // Row body: [grip][checkbox][text]. Delete/pin no longer reserve columns here — they float on
        // top of the row (see below), so the text can use the full width.
        Widget rowBody = new Padding(
            EdgeInsets.Symmetric(vertical: style.RowVerticalPadding, horizontal: style.RowHorizontalPadding),
            child: new Row(
                spacing: style.CheckboxTextGap,
                crossAxisAlignment: CrossAxisAlignment.Start,
                mainAxisSize: MainAxisSize.Max,
                children: children));

        // Drag-reorder wash / resting pinned tint, drawn behind the row content. The Container is
        // ALWAYS present (fill may be transparent Vector4.Zero) — see the structural-stability note below
        // — so toggling the fill is a cheap property update, not a widget-type swap.
        //
        // The two drag states use DELIBERATELY DISTINCT, non-pin colors so drag signalling reads clearly
        // and doesn't collide with the pin resting tint (which used to share the drop-target's ochre):
        //   • SOURCE (the row you grabbed)  → theme Primary brightened +20, half-saturated — "came from".
        //   • DROP TARGET (row under cursor)→ theme Primary darkened  -20, half-saturated — "lands here".
        // Both are theme-derived (via ShiftBrightness) so they read as "the theme, brighter/darker" under
        // any LibGUI theme rather than stark white/black. Fill sits at 0.4 alpha; a 1px border of the SAME
        // shifted color at 0.5 alpha crisps the row edge. Source wins when the cursor hovers back over the
        // origin row (releasing there is a no-op reorder, so signalling "this is home" is clearer than
        // re-showing the drop tint). Neither drag state active → the resting pin tint (task + pinned) or
        // nothing.
        Vector4? dragShift =
            Widget.IsDragSource ? ScribeRowConstants.ShiftBrightness(colors.Primary, +20f, saturationScale: 0.5f)
            : Widget.IsDropTarget ? ScribeRowConstants.ShiftBrightness(colors.Primary, -20f, saturationScale: 0.5f)
            : (Vector4?)null;

        // Focus-driven row chrome, TABLET (cuneiform) PATH ONLY (add-tablet-cuneiform-chrome D3/task 5.1):
        // the cuneiform field paints no box of its own, so the always-present row Container is the sole
        // appearance driver — borderless/transparent at rest, bordered + a soft surface fill on focus. The
        // widget type never swaps (only these BoxStyle properties change), so the field's live caret/text
        // survives the repaint. The normal (Lectern/Notebook) path is untouched: UseCuneiform is false, so
        // the field keeps drawing its own focus border and this stays transparent.
        bool focusedCuneiform = style.UseCuneiform && (Widget.FocusNode?.HasFocus ?? false);

        Vector4 rowFill =
            dragShift is Vector4 d ? d with { W = 0.4f }
            : focusedCuneiform ? colors.SurfaceHigh
            : (Widget.Data.IsTask && Widget.Data.Pinned ? ScribeRowConstants.PinnedTint(colors) : Vector4.Zero);

        Vector4 rowBorder =
            dragShift is Vector4 b ? b with { W = 0.5f }
            : focusedCuneiform ? colors.Primary
            : Vector4.Zero;

        rowBody = new Container(
            style: new BoxStyle
            {
                Color = rowFill,
                BorderColor = rowBorder,
                BorderThickness = dragShift is not null || focusedCuneiform ? 1f : 0f,
                CornerRadius = focusedCuneiform ? Vector4.One * 4f : Vector4.Zero,
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
            // delete: right-most; pin: to its left (task rows only).
            stackChildren.Add(new Positioned(
                right: btnRight, top: btnTop,
                child: new ScribeRowButton(
                    iconName: "scribeclose",
                    iconColor: colors.Error,
                    size: btn,
                    onTap: () => Widget.OnDelete(index))));
            if (Widget.Data.IsTask)
            {
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
