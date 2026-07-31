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

internal readonly record struct ScribePinRowData(Guid DocId, Guid TaskId, bool Done, string Text);

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
        Action<int, int> onReorder,
        ScribeCompletionPolicy completionPolicy,
        Action<ScribeCompletionPolicy> onCompletionPolicyChanged,
        EdgeInsets policyPickerPadding,
        ScribeRowStyle style,
        ScrollController scrollController)
    {
        Rows = rows;
        FocusNodes = focusNodes;
        AutoFocusTaskId = autoFocusTaskId;
        OnTextChanged = onTextChanged;
        OnCommitText = onCommitText;
        OnToggleComplete = onToggleComplete;
        OnDelete = onDelete;
        OnUnpin = onUnpin;
        OnReorder = onReorder;
        CompletionPolicy = completionPolicy;
        OnCompletionPolicyChanged = onCompletionPolicyChanged;
        PolicyPickerPadding = policyPickerPadding;
        Style = style;
        ScrollController = scrollController;
    }

    public IReadOnlyList<ScribePinRowData> Rows { get; }
    public IReadOnlyDictionary<Guid, FocusNode> FocusNodes { get; }
    public Guid? AutoFocusTaskId { get; }
    public Action<Guid, string> OnTextChanged { get; }
    public Action<Guid> OnCommitText { get; }
    public Action<Guid, Guid> OnToggleComplete { get; }
    public Action<Guid, Guid> OnDelete { get; }
    public Action<Guid, Guid> OnUnpin { get; }
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
        if (dragFromIndex is { } from && dragOverIndex is { } to)
        {
            dragFromIndex = null;
            dragOverIndex = null;
            Widget.OnReorder(from, to); // no-op inside if from == to
        }
        else
        {
            SetState(() => { dragFromIndex = null; dragOverIndex = null; });
        }
    }

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        TextStyle labelStyle = new() { FontSize = 14, Color = colors.OnSurface };

        Widget scrollBody;
        if (Widget.Rows.Count == 0)
        {
            scrollBody = new Padding(
                EdgeInsets.All(12),
                child: new Text(
                    Lang.Get("scribe:scribe-gui-pintab-empty"),
                    new TextStyle { FontSize = 14, Color = colors.OnSurfaceVariant, SoftWrap = true }));
        }
        else
        {
            var rows = Widget.Rows
                .Select((r, i) => (Widget)new ScribePinRow(
                    data: r,
                    index: i,
                    focusNode: Widget.FocusNodes.TryGetValue(r.TaskId, out var fn) ? fn : null,
                    autoFocus: Widget.AutoFocusTaskId == r.TaskId,
                    isDropTarget: dragFromIndex is not null && dragOverIndex == i,
                    isDragSource: dragFromIndex == i,
                    onTextChanged: Widget.OnTextChanged,
                    onCommitText: Widget.OnCommitText,
                    onToggleComplete: Widget.OnToggleComplete,
                    onDelete: Widget.OnDelete,
                    onUnpin: Widget.OnUnpin,
                    onDragStart: OnRowDragStart,
                    onDragOver: OnRowDragOver,
                    onDragEnd: OnRowDragEnd,
                    style: Widget.Style,
                    // Key by TaskId (not index) so a row's field State + element identity track the pin
                    // across a reorder/resync rebuild rather than by list position.
                    key: new ValueKey<Guid>(r.TaskId)))
                .ToList();

            scrollBody = new Scrollbar(
                controller: Widget.ScrollController,
                child: new SingleChildScrollView(
                    controller: Widget.ScrollController,
                    child: new Column(
                        spacing: 0,
                        crossAxisAlignment: CrossAxisAlignment.Stretch,
                        mainAxisSize: MainAxisSize.Min,
                        children: rows)))
            { AutoHide = false };
        }

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
        Widget policyCaption = new Text(Lang.Get("scribe:settings-completionpolicy"),
            new TextStyle { FontSize = 13 * scale, Color = colors.OnSurfaceVariant, FontFamily = taskFont });
        policyCaption = new Tooltip(
            child: policyCaption,
            content: new Padding(
                EdgeInsets.All(6),
                child: new Text(
                    Lang.Get("scribe:settings-completionpolicy-help"),
                    new TextStyle { FontSize = 13 * scale, Color = colors.OnSurface, SoftWrap = true, FontFamily = taskFont })),
            useGlobalOverlay: true);

        // Start from the theme's dropdown style and swap in the task font on its shared TextStyle (used
        // for both the trigger button label and the menu items).
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

        return new Padding(
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
                    new Divider(),
                    new Expanded(child: scrollBody),
                }));
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
        Action<Guid, string> onTextChanged,
        Action<Guid> onCommitText,
        Action<Guid, Guid> onToggleComplete,
        Action<Guid, Guid> onDelete,
        Action<Guid, Guid> onUnpin,
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
        OnTextChanged = onTextChanged;
        OnCommitText = onCommitText;
        OnToggleComplete = onToggleComplete;
        OnDelete = onDelete;
        OnUnpin = onUnpin;
        OnDragStart = onDragStart;
        OnDragOver = onDragOver;
        OnDragEnd = onDragEnd;
        Style = style;
    }

    public ScribePinRowData Data { get; }
    public int Index { get; }
    public FocusNode? FocusNode { get; }
    public bool AutoFocus { get; }
    /// <summary>True while a drag is in progress and this row is the current drop target — the row
    /// paints a theme-derived darker wash + border so the player sees where the dragged row would land.</summary>
    public bool IsDropTarget { get; }
    /// <summary>True while a drag is in progress and this row is the one being dragged (its origin) — the
    /// row paints a theme-derived brighter wash + border so the player sees where the dragged row came
    /// from. Takes priority over <see cref="IsDropTarget"/> when the cursor hovers back over the source
    /// row.</summary>
    public bool IsDragSource { get; }
    public Action<Guid, string> OnTextChanged { get; }
    public Action<Guid> OnCommitText { get; }
    public Action<Guid, Guid> OnToggleComplete { get; }
    public Action<Guid, Guid> OnDelete { get; }
    public Action<Guid, Guid> OnUnpin { get; }
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

    public override Widget Build(BuildContext context)
    {
        var data = Widget.Data;
        var style = Widget.Style;
        var colors = Theme.Of(context).ColorScheme;

        var children = new List<Widget>();

        // Grip on the FAR LEFT (always present, matching the editor). onPress/hover(row)/release drive the
        // reorder; nudged down to center on a one-line input, with the trailing gap zeroed (§10.4).
        children.Add(new Padding(
            ScribeRowControlNudge.GripInsets(style),
            child: new GestureDetector(
                onPress: _ => Widget.OnDragStart(Widget.Index),
                onRelease: _ => Widget.OnDragEnd(),
                child: new ScribeVsIconGlyph("scribegrip", style.ControlSize, colors.OnSurfaceVariant))));

        // Completion checkbox — completes with NO undo delay (the send fires immediately; see the dialog's
        // OnPinCompleteTask). Flips optimistically in its own State; the server re-push reconciles it.
        children.Add(new Padding(
            EdgeInsets.Only(top: ScribeRowControlNudge.CheckboxAndGripTop(style)),
            child: new Checkbox(
                value: done,
                onChanged: _ =>
                {
                    SetState(() => done = !done);
                    Widget.OnToggleComplete(data.DocId, data.TaskId);
                },
                size: style.CheckboxSize)));

        // Directly-editable text field (editable by default — no separate edit mode). Held to the same
        // task-text cap as the editor. Writes through on every keystroke (OnTextChanged buffers it);
        // commits on Enter/blur (OnCommitText). Enter commits in place (no insert-below on the Pin Tab).
        children.Add(new Expanded(child: new ScribeMultilineField(
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
            onBlur: () => Widget.OnCommitText(data.TaskId))));

        Widget rowBody = new Padding(
            EdgeInsets.Symmetric(vertical: style.RowVerticalPadding, horizontal: style.RowHorizontalPadding),
            child: new Row(
                spacing: style.CheckboxTextGap,
                crossAxisAlignment: CrossAxisAlignment.Start,
                mainAxisSize: MainAxisSize.Max,
                children: children));

        // Drag highlight, matching the Edit view (a pin row is always "pinned", so there's no resting
        // pinned tint to fall back to — only the drag states or transparent):
        //   • SOURCE (the row you grabbed)  → theme Primary brightened +20, half-saturated — "came from".
        //   • DROP TARGET (row under cursor)→ theme Primary darkened  -20, half-saturated — "lands here".
        // Fill at 0.4 alpha; a 1px border of the SAME shifted color at 0.5 alpha crisps the edge. Source
        // wins when the cursor hovers back over the origin row. The Container is ALWAYS present (transparent
        // fill / zero border when idle) so toggling the highlight is a property update, not a widget-type
        // swap — keeping the field's State mounted (the STRUCTURAL STABILITY rule).
        Vector4? dragShift =
            Widget.IsDragSource ? ScribeRowConstants.ShiftBrightness(colors.Primary, +20f, saturationScale: 0.5f)
            : Widget.IsDropTarget ? ScribeRowConstants.ShiftBrightness(colors.Primary, -20f, saturationScale: 0.5f)
            : (Vector4?)null;

        rowBody = new Container(
            style: new BoxStyle
            {
                Color = dragShift is Vector4 d ? d with { W = 0.4f } : Vector4.Zero,
                BorderColor = dragShift is Vector4 b ? b with { W = 0.5f } : Vector4.Zero,
                BorderThickness = dragShift is not null ? 1f : 0f,
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
