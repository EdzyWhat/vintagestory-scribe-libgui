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
using Vintagestory.API.Client;
using Vintagestory.API.Common;   // ItemStack (Tracker/Link display item)
using Vintagestory.API.Config;   // Lang, GlobalConstants
using Vintagestory.API.MathTools;  // BlockPos

namespace Scribe;

/// <summary>A value snapshot of one Pin Tab row. <see cref="Kind"/> distinguishes Task / Tracker / Link
/// (a pinned Text section can't exist — only completable blocks pin). A Tracker/Link's icon + name are
/// resolved by the dialog (where <c>capi</c> lives) and passed in as <see cref="DisplayStack"/> /
/// <see cref="DisplayName"/>, so the pure LibGUI row widget stays API-free — mirroring
/// <see cref="ScribeReadRowData"/> (add-tracker-link-tasks 7.8).</summary>
internal readonly record struct ScribePinRowData(
    Guid DocId, Guid TaskId, bool Done, string Text,
    ScribeBlockKind Kind = ScribeBlockKind.Task,
    ItemStack? DisplayStack = null, string? DisplayName = null,
    int TargetQuantity = 1, int CurrentQuantity = 0, string? LinkTarget = null)
{
    public bool IsTracker => Kind == ScribeBlockKind.Tracker;
    public bool IsLink => Kind == ScribeBlockKind.Link;
    /// <summary>A Tracker/Link renders an item icon + name instead of an editable text field; a plain Task
    /// keeps the directly-editable field.</summary>
    public bool IsItemKind => IsTracker || IsLink;
    /// <summary>The row's display label: a Tracker/Link shows its resolved item name (its own Text is
    /// empty), a Task shows its authored text. Also used by the collapsing ghost so a removed Tracker/Link
    /// doesn't collapse as a blank row.</summary>
    public string Label => IsItemKind ? (DisplayName ?? Text) : Text;
}

/// <summary>
/// The Pin Tab's content tree: the player's pins as an editable, reorderable list, plus the
/// completion-policy picker below. Reuses the editor view's row shape (grip + checkbox +
/// <see cref="ScribeMultilineField"/> + hover delete/unpin), but every row is editable by default and
/// sourced from the player's pin set rather than a document. Uses a NON-virtualized
/// <see cref="SingleChildScrollView"/> + <see cref="Column"/> of ALL rows (like the editor) so every
/// field's focus node stays mounted and the dialog can coordinate cross-row focus/commit. No max-row cap.
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
        ScribeAmbientLightSampler.Shade currentShade)
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
    }

    public IReadOnlyList<ScribePinRowData> Rows { get; }
    public IReadOnlyDictionary<Guid, FocusNode> FocusNodes { get; }
    public Guid? AutoFocusTaskId { get; }
    public Action<Guid, string> OnTextChanged { get; }
    public Action<Guid> OnCommitText { get; }
    public Action<Guid, Guid> OnToggleComplete { get; }
    public Action<Guid, Guid> OnDelete { get; }
    public Action<Guid, Guid> OnUnpin { get; }
    /// <summary>Open a pinned Tracker/Link's target Handbook page (its name is a hyperlink), addressed by
    /// TaskId. Never touches completion — distinct from the row's checkbox (add-tracker-link-tasks 7.8).</summary>
    public Action<Guid> OnOpenLink { get; }
    public Action<int, int> OnReorder { get; }
    public ScribeCompletionPolicy CompletionPolicy { get; }
    public Action<ScribeCompletionPolicy> OnCompletionPolicyChanged { get; }
    /// <summary>Horizontal breathing room applied around the completion-policy picker header, matching the
    /// title row's inset (<c>left: 10 + 0.04·W, right: 0.04·W</c>) so the picker lines up with the title
    /// band above it. Passed in from the dialog, which owns the <c>ScribeLayout</c> width.</summary>
    public EdgeInsets PolicyPickerPadding { get; }
    public ScribeRowStyle Style { get; }
    /// <summary>Dialog-owned scroll controller shared by all views; NOT disposed here.</summary>
    public ScrollController ScrollController { get; }
    /// <summary>Host-owned collapse controllers for the Pin Tab's departing rows (extract-animated-task-list),
    /// passed through to the <see cref="ScribeAnimatedList"/> so a removed pin's row collapses out instead of
    /// snapping. The dialog owns its lifetime (survives the resync reconcile) and reads its <c>AnyAnimating</c>
    /// to drive scroll-pin/hover in <c>OnRenderGUI</c>; NOT disposed here.</summary>
    public ScribeAnimationRegistry CollapseRegistry { get; }
    /// <summary>Fired (deferred) when a departing pin row's collapse completes and the list has shrunk, so the
    /// dialog can re-clamp the shared scroll extent — see <see cref="ScribeAnimatedList.OnDepartureSettled"/>.</summary>
    public Action OnDepartureSettled { get; }
    /// <summary>Live illumination shade (respect-local-illumination), threaded in so the policy-caption hover
    /// tooltip — which renders in the global Overlay layer, outside the dialog body's own ScribeGlobalTint
    /// wrap — can be shaded to match the body in low light (refine-scribe-hover-tooltips D2).</summary>
    public ScribeAmbientLightSampler.Shade CurrentShade { get; }

    public override State CreateState() => new ScribePinnedContentState();
}

internal sealed class ScribePinnedContentState : State<ScribePinnedContent>
{
    // Drag-reorder state (this State owns the row list, so a drag updates via SetState here — NOT the
    // dialog's ForceRebuild, which would unmount the grip mid-drag and drop the pointer capture). Mirrors
    // ScribeEditorContentState.
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
        // ALWAYS repaint on release (see the editor's OnRowDragEnd): clear the drag under SetState so the
        // ghost row's dimmed content and the ◀/▶ grip arrows revert even on a no-op release. OnReorder's
        // from==to path sends no packet and triggers no rebuild, so without this the row stayed stuck dimmed
        // with a ◀ handle (replace-drag-wash-with-grip-arrows follow-up).
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

        // Route the rows through ScribeAnimatedList (extract-animated-task-list): the container diffs the
        // TaskId-keyed set frame-to-frame, so a pin removed by complete/unpin/delete — which lands here as a
        // now-absent row on the OnMyPinsChanged reconcile — collapses out (rows below sliding up) instead of
        // snapping. The container abstracts MOTION only: it decides row ORDER (live rows + collapsing ghosts
        // spliced at their old slots) and hands that ordered list to OUR layoutBuilder below, which keeps the
        // Pin Tab's own Scrollbar > SingleChildScrollView > Column shape. Immediate policy (no undo window —
        // the Pin Tab's Completion Policy is visible/editable and it has discrete unpin/delete controls, so
        // its removals are affirmative choices).
        //
        // Each live row is the same TaskId-keyed ScribePinRow as before (its ScribeMultilineField State — hence
        // caret + unsaved buffer — reconciles across a resync). Each departing row supplies an explicit frozen
        // ghost: a live pin row is unsafe to freeze in place (its checkbox/field/gestures would stay live mid-
        // collapse and its focus node is gone once the pin leaves the set), so we snapshot it as a static
        // ScribeFrozenEditorRow — the same [grip-spacer][checkbox][text] shape the editor's ghost uses, so the
        // Pin Tab and editor collapse identically. Pinned:false → no resting tint (a Pin Tab row has none).
        var items = Widget.Rows
            .Select((r, i) => new ScribeAnimatedListItem(
                Id: r.TaskId,
                Child: new ScribePinRow(
                    data: r,
                    index: i,
                    focusNode: Widget.FocusNodes.TryGetValue(r.TaskId, out var fn) ? fn : null,
                    autoFocus: Widget.AutoFocusTaskId == r.TaskId,
                    isDropTarget: dragFromIndex is not null && dragOverIndex == i,
                    isDragSource: dragFromIndex == i,
                    // Any drag in progress → non-source/non-target rows hide their grip glyph.
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
                    // Key by TaskId (not index) so a row's field State + element identity track the pin
                    // across a reorder/resync rebuild rather than by list position.
                    key: new ValueKey<Guid>(r.TaskId)),
                Ghost: new ScribeFrozenEditorRow(
                    // The collapse ghost snapshots the row's real Kind + resolved item fields (7.8) so a
                    // removed Tracker/Link collapses showing its icon + name (+ counter) rather than a blank
                    // task row. Every pinnable kind is Completable, so the ghost keeps its checkbox mirroring
                    // the live row as it collapses.
                    new ScribeEditRowData(Index: i, Kind: r.Kind, Done: r.Done, Pinned: false, TaskId: r.TaskId,
                        Text: r.Text, DisplayStack: r.DisplayStack, DisplayName: r.DisplayName,
                        TargetQuantity: r.TargetQuantity, CurrentQuantity: r.CurrentQuantity, LinkTarget: r.LinkTarget),
                    Widget.Style)))
            .ToList();

        Widget scrollBody = new ScribeAnimatedList(
            items: items,
            registry: Widget.CollapseRegistry,
            policy: ScribeListRemovalPolicy.Immediate,
            onDepartureSettled: Widget.OnDepartureSettled,
            // The layout wrapper is ours (D6 seam): the container passes the ordered widget list (live rows +
            // any collapsing ghosts) and we wrap it exactly as before. When that list is empty — no live rows
            // AND no ghost still collapsing — show the empty-state prompt; this keeps the placeholder from
            // popping in before the LAST removed row has finished collapsing.
            layoutBuilder: rows => rows.Count == 0
                ? new Padding(
                    EdgeInsets.All(12),
                    child: new Text(
                        Lang.Get("scribe:scribe-gui-pintab-empty"),
                        new TextStyle { FontSize = 14, Color = colors.OnSurfaceVariant, SoftWrap = true }))
                : new Scrollbar(
                    controller: Widget.ScrollController,
                    child: new SingleChildScrollView(
                        controller: Widget.ScrollController,
                        child: new Column(
                            spacing: 0,
                            crossAxisAlignment: CrossAxisAlignment.Stretch,
                            mainAxisSize: MainAxisSize.Min,
                            children: rows)))
                { AutoHide = false });

        // Completion-policy picker: the same control the Settings window offers, editing the one shared
        // per-player preference (scribe-pin-editor — "one value, two hosts"). Positioned as the view's
        // HEADER, above the list (scribe-lectern-view-consistency §3).
        //
        // The caption scales with the window text size (derived from the shared row Style, which is
        // BaseWindowFontSize * WindowFontScale) and carries the SAME hover helptext the Settings screen
        // shows for this setting, so the two hosts of this one preference read identically. Both the
        // caption/help and the dropdown's own text follow the player's chosen Task Text Font.
        float scale = Widget.Style.FontSize / ScribeRowConstants.BaseWindowFontSize;
        string taskFont = ScribeTaskFont.Resolve(Widget.Style.TaskFontFamily);
        // Caption font family inherited from the tab's DefaultTextStyle ancestor; the smaller 13*scale
        // size is a deliberate delta and stays explicit.
        Widget policyCaption = new Text(Lang.Get("scribe:settings-completionpolicy"),
            new TextStyle { FontSize = 13 * scale, Color = colors.OnSurfaceVariant });
        policyCaption = new Tooltip(
            child: policyCaption,
            // Shade the tooltip to match the body in low light (refine-scribe-hover-tooltips D2), same reduced
            // hover strength as the shared WithTooltip helper.
            content: ScribeGlobalTint.ForHover(new Padding(
                EdgeInsets.All(6),
                // useGlobalOverlay: this tooltip renders OUTSIDE the tab subtree (task 3.1), so the
                // DefaultTextStyle ancestor does NOT reach it — it must keep an explicit task font.
                child: new Text(
                    Lang.Get("scribe:settings-completionpolicy-help"),
                    new TextStyle { FontSize = 13 * scale, Color = colors.OnSurface, SoftWrap = true, FontFamily = taskFont })),
                Widget.CurrentShade),
            useGlobalOverlay: true);

        // Start from the theme's dropdown style and swap in the task font on its shared TextStyle (used
        // for both the trigger button label and the menu items). Kept explicit (not inherited): the
        // dropdown's popup menu renders in a global overlay, outside the tab subtree the DefaultTextStyle
        // ancestor covers (task 3.1) — and Dropdown takes a DropdownStyle, not a plain Text anyway.
        var dropdownStyle = Theme.Of(context).DropdownStyle;
        dropdownStyle = dropdownStyle with { TextStyle = dropdownStyle.TextStyle with { FontFamily = taskFont } };

        var policyPicker = new Column(
            spacing: 4,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                policyCaption,
                new Dropdown<ScribeCompletionPolicy>(
                    value: Widget.CompletionPolicy,
                    // Explicit display order (v1-playtest-fixes 5.2 / 9.2): Keep (stay), Keep (sink),
                    // Unpin (stay), Unpin (sink), Delete — kept in sync with the Settings picker.
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

        // Root the tab subtree in the player's Task Text Font + window-scaled base size, so the empty
        // state and the policy caption inherit them (adopt-libgui-31-improvements). Survivors that render
        // outside this subtree keep explicit fonts: the policy tooltip + dropdown menu (global overlay,
        // task 3.1) and the pin rows' ScribeMultilineField (custom RenderBox that doesn't inherit).
        return ScribeTextDefaults.Wrap(Widget.Style.TaskFontFamily, Widget.Style.FontSize, new Padding(
            EdgeInsets.All(10),
            child: new Column(
                spacing: 8,
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: new Widget[]
                {
                    // Header: policy picker, then a divider straight-edge above the scroll region
                    // (scribe-lectern-view-consistency §1 + §3). Expanded keeps the list filling the rest.
                    // The picker is inset horizontally to match the title row's padding (added on request);
                    // the divider + list keep the outer EdgeInsets.All(10) so only the picker shifts in.
                    new Padding(Widget.PolicyPickerPadding, child: policyPicker),
                    // Dropped on the cuneiform tablet path (add-tablet-clay-type-themes 8.1) — the hard
                    // rule reads wrong against the clay backdrop; the readable path keeps it.
                    Widget.Style.UseCuneiform ? new SizedBox() : new Divider(),
                    new Expanded(child: scrollBody),
                })));
    }
}

/// <summary>
/// One Pin Tab row: a drag grip + a completion checkbox + the pin's directly-editable text field, with
/// hover-conditional unpin and delete buttons floating on the right — the editor row's shape
/// (<see cref="ScribeEditRow"/>) fed from a pin rather than a document block. Editable by default; every
/// action is addressed by the pin's stable <c>(DocId, TaskId)</c>. Keyed by TaskId by its parent.
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
    }

    public ScribePinRowData Data { get; }
    public int Index { get; }
    public FocusNode? FocusNode { get; }
    public bool AutoFocus { get; }
    /// <summary>True while a drag is in progress and this row is the current drop target — the row's grip
    /// shows a right-pointing (▶) accent glyph marking where the dragged row would land
    /// (replace-drag-wash-with-grip-arrows).</summary>
    public bool IsDropTarget { get; }
    /// <summary>True while a drag is in progress and this row is the one being dragged (its origin) — the
    /// row shows a left-pointing (◀) grip glyph and dims to read as lifted. Takes priority over
    /// <see cref="IsDropTarget"/> when the cursor hovers back over the source row.</summary>
    public bool IsDragSource { get; }
    /// <summary>True while ANY grip-drag is in progress (this row may be neither source nor target). A
    /// non-participating row hides its grip glyph so the list declutters down to just the ◀ source and ▶
    /// drop-target rows (replace-drag-wash-with-grip-arrows).</summary>
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

    public override State CreateState() => new ScribePinRowState();
}

internal sealed class ScribePinRowState : State<ScribePinRow>
{
    private bool done;
    /// <summary>True while the pointer is over this row: the delete/unpin controls are hidden until then
    /// (the editor row's hover-gating). The grip is NOT hover-gated (it stays mounted so a drag it started
    /// can't lose pointer capture mid-move).</summary>
    private bool hovered;

    public override void InitState()
    {
        base.InitState();
        done = Widget.Data.Done;
    }

    /// <summary>Resync the optimistic <see cref="done"/> when an external change flips this row's authoritative
    /// completion. Mirrors <c>ScribeReadRowState.UpdateWidget</c> (reconcile-animating-surfaces §5): since
    /// <c>6eb59a7</c> the Pin Tab reconciles in place instead of <c>ForceRebuild</c>, so a server re-push
    /// (e.g. a HUD completion under a Keep/Sink policy, where the pin stays in the set) REUSES this
    /// TaskId-keyed row rather than remounting it — without this override, <see cref="InitState"/>'s seed goes
    /// stale and the checkbox never reflects the external toggle. Gate on the authoritative value actually
    /// CHANGING so a pure chrome reconcile (pin re-tint, reorder) doesn't stomp an in-flight optimistic tick
    /// the player just made — the same discipline the Read view uses.</summary>
    public override void UpdateWidget(ScribePinRow oldWidget)
    {
        base.UpdateWidget(oldWidget);
        if (oldWidget.Data.Done != Widget.Data.Done) done = Widget.Data.Done;
    }

    /// <summary>The content of a Tracker/Link pin row: the referenced item's icon + name, plus a have/need
    /// counter on the LEFT for a Tracker (add-tracker-link-tasks 7.8). The name is a hyperlink — tapping it
    /// opens the item's Handbook page via <see cref="ScribePinRow.OnOpenLink"/> and never touches the row's
    /// checkbox. Mirrors <c>ScribeReadRowState.BuildItemContent</c> exactly so the Pin Tab, read, and editor
    /// views render Tracker/Link rows identically (counter-left; future Crafting tasks inherit it).</summary>
    private Widget BuildItemContent(ColorScheme colors, ScribeRowStyle style)
    {
        var data = Widget.Data;
        float iconSize = style.ControlSize * 1.4f;
        float lineHeight = ScribeRowControlNudge.TextLineHeight(style.FontSize);
        // Guide-page book glyph Primary (7.11d), item icon grown + row-height-neutral (7.11e/7.11f).
        Widget icon = ScribeLinkIcon.Build(data.DisplayStack, data.LinkTarget, iconSize, colors.Primary, lineHeight);

        Widget nameLink = new Expanded(child: new GestureDetector(
            onPress: e => { e.Handled = true; Widget.OnOpenLink(data.TaskId); },
            child: new Text(data.Label, new TextStyle { Color = colors.Primary, SoftWrap = true })));

        var rowChildren = new List<Widget>();
        if (data.IsLink)
        {
            rowChildren.Add(icon);
            rowChildren.Add(nameLink);
        }
        else // Tracker: a "have / need" counter on the LEFT, then the item icon + name.
        {
            bool satisfied = data.CurrentQuantity >= data.TargetQuantity;
            // Inverted emphasis + satisfied strikethrough, shared with the read/HUD counters (7.11g/7.11h).
            rowChildren.Add(ScribeTrackerCounterText.Build(
                data.CurrentQuantity, data.TargetQuantity, satisfied,
                strongColor: colors.Primary, mutedColor: colors.OnSurfaceVariant, lineHeight: lineHeight));
            rowChildren.Add(icon);
            rowChildren.Add(nameLink);
        }

        // Inset by the editor field's internal padding, matching the Task row, so icon rows line up with text
        // rows. Center the icon against the (taller-than-a-line) content.
        return new Padding(
            EdgeInsets.Symmetric(vertical: style.FieldPadY, horizontal: style.FieldPadX),
            child: new Row(
                spacing: style.CheckboxTextGap,
                crossAxisAlignment: CrossAxisAlignment.Center,
                mainAxisSize: MainAxisSize.Max,
                children: rowChildren));
    }

    public override Widget Build(BuildContext context)
    {
        var data = Widget.Data;
        var style = Widget.Style;
        var colors = Theme.Of(context).ColorScheme;

        var children = new List<Widget>();

        // Grip on the FAR LEFT (always present, matching the editor). onPress/hover(row)/release drive the
        // reorder; nudged down to center on a one-line input, with the trailing gap zeroed (§10.4).
        //
        // The grip glyph is state-driven during a drag (replace-drag-wash-with-grip-arrows), identical to
        // the editor view: source (grabbed) row → ◀ (muted OnSurfaceVariant); the prospective drop row → ▶
        // (accent Primary); any OTHER row while a drag is in progress → a same-size empty box (glyph hidden)
        // so only the ◀/▶ pair shows and the column width is unchanged. Source wins over drop-target when
        // the cursor hovers back over the origin row. This replaces the old row-background washes, which
        // collided with the strengthened pinned tint (though a Pin Tab row has no resting tint of its own,
        // the two views share this machinery and must behave identically).
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
            ScribeRowControlNudge.GripInsets(style),
            child: new GestureDetector(
                onPress: _ => Widget.OnDragStart(Widget.Index),
                onRelease: _ => Widget.OnDragEnd(),
                child: gripGlyph)));

        // Source-row "lifted / in-hand" dim (matching the editor): while THIS row is dragged, its CONTENT
        // (checkbox + text) paints at ~half opacity, applied per-child so the grip column keeps full opacity
        // and the ◀ source arrow retains its ink. Always-present (value flips 1.0↔0.5) so no widget swap.
        float contentOpacity = Widget.IsDragSource ? 0.5f : 1f;

        // Completion checkbox — completes with NO undo delay (the send fires immediately; see the dialog's
        // OnPinCompleteTask). Flips optimistically in its own State; the server re-push reconciles it.
        children.Add(new Opacity(contentOpacity, child: new Padding(
            EdgeInsets.Only(top: ScribeRowControlNudge.CheckboxAndGripTop(style)),
            child: new Checkbox(
                value: done,
                onChanged: _ =>
                {
                    SetState(() => done = !done);
                    Widget.OnToggleComplete(data.DocId, data.TaskId);
                },
                size: style.CheckboxSize))));

        // A Tracker/Link pin renders a non-editable item icon + name (+ a have/need counter for a Tracker),
        // NOT the editable text field — its own Text is empty, its content is the referenced item, exactly
        // like the read/editor views (add-tracker-link-tasks 7.8). Otherwise the directly-editable text field
        // (editable by default — no separate edit mode). Held to the same task-text cap as the editor. Writes
        // through on every keystroke (OnTextChanged buffers it); commits on Enter/blur (OnCommitText). Enter
        // commits in place (no insert-below on the Pin Tab).
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
                onChanged: text => Widget.OnTextChanged(data.TaskId, text),
                onCommitAndAdvance: () => Widget.OnCommitText(data.TaskId),
                onCommitAndRetreat: () => Widget.OnCommitText(data.TaskId),
                onInsertTaskBelow: () => Widget.OnCommitText(data.TaskId),
                onBlur: () => Widget.OnCommitText(data.TaskId)))));
        }

        Widget rowBody = new Padding(
            EdgeInsets.Symmetric(vertical: style.RowVerticalPadding, horizontal: style.RowHorizontalPadding),
            child: new Row(
                spacing: style.CheckboxTextGap,
                crossAxisAlignment: CrossAxisAlignment.Start,
                mainAxisSize: MainAxisSize.Max,
                children: children));

        // Drag feedback is on the grip glyph (◀/▶ above) + a source-row opacity dim (below), NOT a row
        // background wash (replace-drag-wash-with-grip-arrows) — the old brightened/darkened-Primary washes
        // collided with the strengthened pinned tint in the editor, and the two views share this machinery.
        // A Pin Tab row has no resting pinned tint of its own (every row is pinned), so its idle Container
        // is fully transparent. The Container stays ALWAYS present (transparent fill / zero border) so the
        // widget type never swaps mid-drag, keeping the field's State mounted (the STRUCTURAL STABILITY rule).
        rowBody = new Container(
            style: new BoxStyle
            {
                Color = Vector4.Zero,
                BorderColor = Vector4.Zero,
                BorderThickness = 0f,
            },
            child: rowBody);

        // Delete + unpin float on the RIGHT as real buttons, shown only on hover. delete = right-most (the
        // task itself), unpin to its left (remove only the pin). Same Stack overlay the editor uses.
        var stackChildren = new List<Widget> { rowBody };
        if (hovered)
        {
            float btn = style.ControlSize;
            float gap = 4f;
            float boxW = btn - ScribeRowButton.BoxShrink;
            float btnRight = gap + 1f;
            float btnTop = ScribeRowControlNudge.FloatingButtonTop(style);
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
                    iconColor: colors.Primary, // a Pin Tab row is always pinned → active accent
                    size: btn,
                    onTap: () => Widget.OnUnpin(data.DocId, data.TaskId),
                    iconScale: 1.1f))); // pin glyph +10% (§10.2)
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

/// <summary>Top-margin nudges that visually center a row's shorter controls on a SINGLE-LINE text
/// input, without moving them when the text wraps to multiple lines.
///
/// <para>The row lays its children out with <see cref="CrossAxisAlignment.Start"/> (top-aligned) so a
/// multi-line input keeps the controls pinned to its first line. That means the controls do NOT auto-
/// center — a one-line input is a couple of pixels taller than the grip/checkbox glyphs and the
/// floating pin/delete buttons, so each reads a hair high. We nudge each control DOWN by a top margin
/// to sit centered on a one-line row. Because the nudge is smaller than the (input − control) slack, it
/// never grows the row height.</para>
///
/// <para>Now that the window font size is user-adjustable (add-settings-tab D4), these are COMPUTED from
/// the measured single-line input height and the control's size at the current scale — centering is
/// <c>(inputHeight − controlHeight) / 2</c>, which is not linear in the scale, so a single multiplier of
/// the old font-15 constants wouldn't stay centered. The single-line input height mirrors
/// <c>ScribeMultilineFieldRender</c>'s own formula (measured line height of "Ag" in the same
/// "sans-serif" family + the field's vertical padding), so the computed offset tracks the real field at
/// any scale.</para></summary>
