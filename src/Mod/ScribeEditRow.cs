using Gui.Rendering;
using Gui.Rendering.Text;
using Gui.Widgets.Basic;
using Gui.Widgets.Framework;
using Gui.Widgets.Input;
using Gui.Widgets.Layout;
using Gui.Widgets.Painting;
using Gui.Core.Layout;
using OpenTK.Mathematics;
using Scribe.Core;
using SkiaSharp;
using Vintagestory.API.Config;

namespace Scribe;

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
