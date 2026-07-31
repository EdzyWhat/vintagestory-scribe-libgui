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

internal readonly record struct ScribeReadRowData(int Index, bool IsTask, bool Done, bool Pinned, Guid TaskId, string Text);

/// <summary>
/// The read view's content tree: the document rendered as a scrollable <see cref="ListView"/> of
/// rows, with a "switch to editor" control below. The interactive per-row state lives in the row
/// widgets themselves (design D4), not here.
/// </summary>
internal sealed class ScribeReadContent : StatefulWidget
{
    public ScribeReadContent(
        IReadOnlyList<ScribeReadRowData> blocks,
        Action<Guid> onToggleTask,
        Action<Guid> onTogglePinned,
        Action onSwitchToEditor,
        EdgeInsets footerButtonPadding,
        ScribeRowStyle style,
        ScrollController scrollController,
        string hintLangKey = "scribe:scribe-gui-edit-hint")
    {
        Blocks = blocks;
        OnToggleTask = onToggleTask;
        OnTogglePinned = onTogglePinned;
        OnSwitchToEditor = onSwitchToEditor;
        FooterButtonPadding = footerButtonPadding;
        Style = style;
        ScrollController = scrollController;
        HintLangKey = hintLangKey;
    }

    public IReadOnlyList<ScribeReadRowData> Blocks { get; }
    /// <summary>Complete a task by its stable id (the read view completes by identity, not index).</summary>
    public Action<Guid> OnToggleTask { get; }
    /// <summary>Pin/unpin a task by its stable id (scribe-lectern-view-consistency §2).</summary>
    public Action<Guid> OnTogglePinned { get; }
    public Action OnSwitchToEditor { get; }
    /// <summary>Horizontal breathing room applied around the footer button (Edit), 0.04·W each side, so it
    /// doesn't run to the content edges. Passed from the dialog, which owns the <c>ScribeLayout</c> width.</summary>
    public EdgeInsets FooterButtonPadding { get; }
    public ScribeRowStyle Style { get; }
    /// <summary>Dialog-owned scroll controller shared by both views (see the dialog field); NOT disposed
    /// here — the dialog owns its lifetime so the scroll offset survives the view-switch rebuild.</summary>
    public ScrollController ScrollController { get; }
    public string HintLangKey { get; }

    public override State CreateState() => new ScribeReadContentState();
}

internal sealed class ScribeReadContentState : State<ScribeReadContent>
{
    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;

        TextStyle switchTextStyle = new() { FontSize = 14, Color = colors.OnPrimary, FontFamily = ScribeTaskFont.ButtonFamily };

        // The scrollable row list. Each row is a self-stateful widget keyed by its block index so the
        // ListView tracks it across document changes (design D4). variableHeight so a wrapped
        // multi-line note row measures to its real height. Wrapped in a Scrollbar so a list taller
        // than the viewport shows a draggable track (wheel scroll worked before; the visible bar
        // did not exist — task 8.15).
        Widget rowList;
        if (Widget.Blocks.Count == 0)
        {
            var hintStyle = Widget.Style;
            rowList = new Center(child: new Text(
                Lang.Get(Widget.HintLangKey),
                new TextStyle { FontSize = hintStyle.FontSize, Color = colors.OnSurfaceVariant, SoftWrap = true,
                    FontFamily = ScribeTaskFont.Resolve(hintStyle.TaskFontFamily), Align = TextAlignment.Center }));
        }
        else
        {
            var style = Widget.Style;
            // AutoHide off: keep the bar permanently visible (matches the pre-LibGUI native GUI). This
            // also sidesteps the flicker where a ForceRebuild (delete/pin/reorder) nudged the controller
            // and re-triggered the auto-hide fade-in/out (task 5.7). AutoHide is an init-only property,
            // not a ctor param, so it's set in an object initializer.
            rowList = new Scrollbar(
                controller: Widget.ScrollController,
                child: new ListView(
                    children: Widget.Blocks
                        .Select(b => (Widget)new ScribeReadRow(b, Widget.OnToggleTask, Widget.OnTogglePinned, style, new ValueKey<int>(b.Index)))
                        .ToList(),
                    // Scroll estimate for rows not yet mounted (variableHeight measures the real height
                    // of mounted rows). This MUST equal a true single-line row height, because the
                    // ListView derives its total content height — and thus the shared controller's
                    // max-scroll — from this estimate for every un-rendered row. A too-small estimate
                    // (the old `FontSize * 1.2f` stand-in undercounts the real metric line height) made
                    // the read view bottom out at a smaller offset than the editor's exact-summed
                    // SingleChildScrollView, so after jumping/dragging to the bottom the read content sat
                    // ~2px lower than the editor (fixes 18cd5c60). Use the SAME measured line height the
                    // editor field uses (same "sans-serif" family — see ScribeMultilineFieldRender) plus
                    // the identical field + row vertical padding.
                    estimatedItemHeight: TextLayoutHelper.MeasureText("Ag", "sans-serif", style.FontSize, FontWeight.Normal).Y
                        + style.FieldPadY * 2 + style.RowVerticalPadding * 2,
                    variableHeight: true,
                    controller: Widget.ScrollController))
            { AutoHide = false };
        }

        return new Padding(
            EdgeInsets.All(10),
            child: new Column(
                spacing: 8,
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: new Widget[]
                {
                    // A straight edge directly above the scroll region, matching the editor and pinned
                    // views (scribe-lectern-view-consistency §1). Reuses the theme-border Divider the
                    // settings form uses; inherits the Column's spacing gap below it.
                    new Divider(),
                    new Expanded(child: rowList),
                    new Padding(Widget.FooterButtonPadding, child: new Button(
                        child: new Text(Lang.Get("scribe:scribe-gui-switch-to-editor"), switchTextStyle),
                        onTap: _ => Widget.OnSwitchToEditor())),
                }));
    }
}

/// <summary>
/// One read-view row: a task checkbox (reflecting/toggling Done) or a note, plus wrapped text.
/// Self-stateful and keyed (design D4). Only the checkbox is interactive.
/// </summary>
internal sealed class ScribeReadRow : StatefulWidget
{
    public ScribeReadRow(ScribeReadRowData data, Action<Guid> onToggleTask, Action<Guid> onTogglePinned, ScribeRowStyle style, Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        Data = data;
        OnToggleTask = onToggleTask;
        OnTogglePinned = onTogglePinned;
        Style = style;
    }

    public ScribeReadRowData Data { get; }
    public Action<Guid> OnToggleTask { get; }
    public Action<Guid> OnTogglePinned { get; }
    public ScribeRowStyle Style { get; }

    public override State CreateState() => new ScribeReadRowState();
}

internal sealed class ScribeReadRowState : State<ScribeReadRow>
{
    private bool done;
    /// <summary>True while the pointer is over this row: the pin control is hidden until then, mirroring
    /// the editor row's hover-conditional icons (lectern-gui-shell "Row icons are hover-conditional").</summary>
    private bool hovered;

    public override void InitState()
    {
        base.InitState();
        done = Widget.Data.Done;
    }

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        var style = Widget.Style;
        TextStyle textStyle = new() { FontSize = style.FontSize, Color = colors.OnSurface, SoftWrap = true, FontFamily = ScribeTaskFont.Resolve(style.TaskFontFamily) };

        var children = new List<Widget>();

        // Reserve the same far-left grip column the editor row draws (f07783f7), so read and edit rows
        // are column-identical and align seamlessly across a view switch. It's the actual grip glyph
        // (keeps the reserved width in lockstep with the editor's grip if ControlSize changes) but drawn
        // at zero opacity and with NO gesture wrapper -- purely a spacer, uninteractable and invisible.
        // The read view exposes no reorder (dragging is a lock-gated authoring action, design D4). Uses the
        // SAME GripInsets as the editor grip (top nudge + the -CheckboxTextGap trailing cancel, §10.4) so
        // the reserved column — and thus the text's left edge — stays aligned row-for-row across a switch.
        children.Add(new Padding(
            ScribeRowControlNudge.GripInsets(style),
            child: new Opacity(
                opacity: 0f,
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
                        Widget.OnToggleTask(Widget.Data.TaskId);
                    },
                    size: style.CheckboxSize)));
        }

        // Inset the read text by the editor field's internal padding so a single-line read row is the
        // same height as the editor field (vertical) and its text's left edge aligns (horizontal) across
        // a view switch. No border here -- only the editor field draws one (inside its own padding).
        children.Add(new Expanded(child: new Padding(
            EdgeInsets.Symmetric(vertical: style.FieldPadY, horizontal: style.FieldPadX),
            child: new Text(Widget.Data.Text, textStyle))));

        Widget rowBody = new Padding(
            EdgeInsets.Symmetric(vertical: style.RowVerticalPadding, horizontal: style.RowHorizontalPadding),
            child: new Row(
                spacing: style.CheckboxTextGap,
                crossAxisAlignment: CrossAxisAlignment.Start,
                mainAxisSize: MainAxisSize.Max,
                children: children));

        // Resting pinned indicator (lectern-gui-shell "Pinned tasks show a resting indicator"): a
        // pinned task carries the same subtle tint the editor row uses, drawn under the row content, so
        // it reads as pinned without hovering. Unpinned tasks and text sections get no tint. The
        // Container is ALWAYS present (transparent fill when unpinned) so the read row's index-0 child
        // stays structurally identical across hover — the same reconciler-stability rule the editor row
        // documents, so revealing the hover pin never remounts the row subtree.
        rowBody = new Container(
            style: new BoxStyle
            {
                Color = Widget.Data.IsTask && Widget.Data.Pinned ? ScribeRowConstants.PinnedTint(colors) : Vector4.Zero,
            },
            child: rowBody);

        // Pin toggle floats on the right, task-only, shown on hover — mirroring the editor row's pin
        // button (scribe-lectern-view-consistency §2). The read view still exposes no edit/delete/drag;
        // only pin + the checkbox are interactive here.
        var stackChildren = new List<Widget> { rowBody };
        if (hovered && Widget.Data.IsTask)
        {
            stackChildren.Add(new Positioned(
                right: 5f, top: ScribeRowControlNudge.FloatingButtonTop(style),
                child: new ScribeRowButton(
                    iconName: "scribepin",
                    iconColor: Widget.Data.Pinned ? colors.Primary : colors.OnSurfaceVariant,
                    size: style.ControlSize,
                    onTap: () => Widget.OnTogglePinned(Widget.Data.TaskId),
                    iconScale: 1.15f))); // pin glyph +15%, matching the editor (§10.2)
        }

        return new MouseRegion(
            onEnter: _ => { if (!hovered) SetState(() => hovered = true); },
            onExit: _ => { if (hovered) SetState(() => hovered = false); },
            child: new Stack(stackChildren));
    }
}

// ============================================================================
// Editor view content
// ============================================================================

/// <summary>A value snapshot of one editable block plus its index. The live text lives in the
/// dialog's scratch document (the field writes through on every keystroke); this is only the seed
/// for building the row.</summary>
