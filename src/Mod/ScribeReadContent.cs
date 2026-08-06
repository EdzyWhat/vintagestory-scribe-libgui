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
        string hintLangKey = "scribe:scribe-gui-edit-hint",
        bool readOnly = false,
        bool completionAndPinLive = false,
        Action<Guid>? onTextEditRefused = null)
    {
        Blocks = blocks;
        OnToggleTask = onToggleTask;
        OnTogglePinned = onTogglePinned;
        OnSwitchToEditor = onSwitchToEditor;
        FooterButtonPadding = footerButtonPadding;
        Style = style;
        ScrollController = scrollController;
        HintLangKey = hintLangKey;
        ReadOnly = readOnly;
        CompletionAndPinLive = completionAndPinLive;
        OnTextEditRefused = onTextEditRefused;
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
    /// <summary>When true this is a permanently-read-only surface (a hard or fired tablet — tablet-firing):
    /// the "switch to editor" footer button is omitted. It also gates row text-editing affordances (there are
    /// none in the read view, so this is mostly about the footer). The tabbed Lectern/Notebook read view
    /// passes false, so it keeps its "Edit" footer button exactly as before.
    ///
    /// <para>NOTE (zero-point-three-fixes §7.3): read-only no longer implies inert checkbox/pin. Whether a
    /// read-only row's checkbox and pin stay interactive is governed by <see cref="CompletionAndPinLive"/>,
    /// decoupled so a hard/fired TABLET keeps completion + pin live (so a fired tablet's pin is never stranded
    /// on the HUD) while its text stays locked.</para></summary>
    public bool ReadOnly { get; }
    /// <summary>When true, each task row's completion checkbox and hover pin stay INTERACTIVE even on a
    /// read-only surface (a hard/fired tablet — zero-point-three-fixes §7.3). The tabbed Lectern/Notebook read
    /// view leaves this false: it is already <see cref="ReadOnly"/> = false and drives its checkbox/pin off
    /// that, so its behavior is unchanged. Only the tablet's read view sets this true alongside
    /// <see cref="ReadOnly"/> = true, to keep toggles live while blocking text.</summary>
    public bool CompletionAndPinLive { get; }
    /// <summary>Invoked when the player taps a locked row's TEXT on a read-only tablet (zero-point-three-fixes
    /// §7.4), carrying the row's TaskId, so the dialog can surface the material-specific "soften it / cannot
    /// be changed" in-game message. Null on the tabbed read view (and on a wet tablet), where the text isn't a
    /// blocked-edit affordance.</summary>
    public Action<Guid>? OnTextEditRefused { get; }

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
            // Font family + base size inherited from the tab's DefaultTextStyle ancestor; only the
            // color and the centered-wrap overrides are non-default and stay explicit.
            rowList = new Center(child: new Text(
                Lang.Get(Widget.HintLangKey),
                new TextStyle { Color = colors.OnSurfaceVariant, SoftWrap = true, Align = TextAlignment.Center }));
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
                        .Select(b => (Widget)new ScribeReadRow(b, Widget.OnToggleTask, Widget.OnTogglePinned, style, Widget.ReadOnly, Widget.CompletionAndPinLive, Widget.OnTextEditRefused, new ValueKey<int>(b.Index)))
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

        // The "switch to editor" footer button — the read view's only edit affordance. A permanently
        // read-only surface (hard/fired tablet — tablet-firing) omits it entirely so there is no path back
        // into the editor; the tabbed Lectern/Notebook keep it. Built as a list so the whole footer slot
        // (Padding + Button) drops out cleanly rather than rendering an empty gap.
        var children = new List<Widget>
        {
            // A straight edge directly above the scroll region, matching the editor and pinned
            // views (scribe-lectern-view-consistency §1). Reuses the theme-border Divider the
            // settings form uses; inherits the Column's spacing gap below it. Dropped on the
            // cuneiform tablet path (add-tablet-clay-type-themes 8.1) — the hard rule reads wrong
            // against the clay backdrop; the readable Lectern/Notebook view keeps it.
            Widget.Style.UseCuneiform ? new SizedBox() : new Divider(),
            new Expanded(child: rowList),
        };
        if (!Widget.ReadOnly)
        {
            children.Add(new Padding(Widget.FooterButtonPadding, child: new Button(
                child: new Text(Lang.Get("scribe:scribe-gui-switch-to-editor"), switchTextStyle),
                onTap: _ => Widget.OnSwitchToEditor())));
        }

        // Root the whole tab subtree in the player's Task Text Font + window-scaled base size, so the
        // row text and empty hint inherit them (adopt-libgui-31-improvements). The row widgets live in
        // the ListView below, which is a descendant of this ancestor. The switch/Edit button keeps its
        // own explicit Caudex button font (a deliberate non-task face), so it is unaffected.
        return ScribeTextDefaults.Wrap(Widget.Style.TaskFontFamily, Widget.Style.FontSize, new Padding(
            EdgeInsets.All(10),
            child: new Column(
                spacing: 8,
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: children)));
    }
}

/// <summary>
/// One read-view row: a task checkbox (reflecting/toggling Done) or a note, plus wrapped text.
/// Self-stateful and keyed (design D4). Only the checkbox is interactive.
/// </summary>
internal sealed class ScribeReadRow : StatefulWidget
{
    public ScribeReadRow(ScribeReadRowData data, Action<Guid> onToggleTask, Action<Guid> onTogglePinned, ScribeRowStyle style, bool readOnly = false, bool completionAndPinLive = false, Action<Guid>? onTextEditRefused = null, Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        Data = data;
        OnToggleTask = onToggleTask;
        OnTogglePinned = onTogglePinned;
        Style = style;
        ReadOnly = readOnly;
        CompletionAndPinLive = completionAndPinLive;
        OnTextEditRefused = onTextEditRefused;
    }

    public ScribeReadRowData Data { get; }
    public Action<Guid> OnToggleTask { get; }
    public Action<Guid> OnTogglePinned { get; }
    public ScribeRowStyle Style { get; }
    /// <summary>Permanently-read-only surface (hard/fired tablet — tablet-firing): the "switch to editor"
    /// footer is dropped and text stays locked. Whether the checkbox/pin stay live is governed separately by
    /// <see cref="CompletionAndPinLive"/> (zero-point-three-fixes §7.3), not by this flag alone.</summary>
    public bool ReadOnly { get; }
    /// <summary>When true, keep the checkbox and hover pin INTERACTIVE even though <see cref="ReadOnly"/> is
    /// true — the hard/fired tablet case (§7.3), so completion and unpin stay reachable. False on the tabbed
    /// read view, which drives its live controls off <see cref="ReadOnly"/> = false instead (unchanged).</summary>
    public bool CompletionAndPinLive { get; }
    /// <summary>Tapping the row's TEXT on a read-only tablet surfaces the material-specific locked message
    /// through this callback (§7.4). Null where a text tap is not a blocked-edit affordance (tabbed read view,
    /// wet tablet).</summary>
    public Action<Guid>? OnTextEditRefused { get; }
    /// <summary>Whether this row's checkbox and pin should behave as interactive: true on any editable read
    /// view (<see cref="ReadOnly"/> = false) OR on a read-only surface that keeps completion/pin live
    /// (<see cref="CompletionAndPinLive"/>). Consolidates the two so the checkbox/pin render off one predicate.</summary>
    public bool TogglesLive => !ReadOnly || CompletionAndPinLive;

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
        // Font family + base size inherited from the tab's DefaultTextStyle ancestor; only color and
        // wrap are non-default here.
        TextStyle textStyle = new() { Color = colors.OnSurface, SoftWrap = true };

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
                    // The checkbox stays interactive whenever toggles are live: on any editable read view AND
                    // on a hard/fired tablet, which keeps completion live so a pinned task can still be
                    // completed/unpinned (zero-point-three-fixes §7.3). A null onChanged (only when toggles
                    // are NOT live — not currently reached, but the safe inert fallback) reflects Done and
                    // ignores taps.
                    onChanged: !Widget.TogglesLive ? null : _ =>
                    {
                        SetState(() => done = !done);
                        Widget.OnToggleTask(Widget.Data.TaskId);
                    },
                    size: style.CheckboxSize)));
        }

        // The row text. On the cuneiform tablet path (add-tablet-firing-mechanic) render it as display-only
        // cuneiform strokes so a dried/fired tablet reads in the SAME glyphs the wet tablet types in —
        // reusing the editable field's wrapping render object with focus/caret OFF (no new rendering code),
        // seeded off the same TaskId so a row wobbles identically whether wet-editable or read-only. Off the
        // cuneiform path (Lectern/Notebook, or cuneiform disabled) it stays the normal wrapped Text, inset by
        // the editor field's internal padding so a single-line read row matches the editor field height and
        // its text's left edge aligns across a view switch.
        Widget textChild = style.UseCuneiform
            ? new ScribeCuneiformFieldRenderWidget(
                text: Widget.Data.Text,
                caret: 0,
                selectionAnchor: 0,
                hasFocus: false,
                fontSizeEm: style.FontSize,
                inkColor: colors.OnSurface,
                caretColor: colors.Primary,
                selectionColor: Vector4.Zero,
                bundle: style.CuneiformBundle,
                padX: style.FieldPadX,
                padY: style.FieldPadY,
                // Resting (unfocused) box is transparent, exactly like a resting editable cuneiform row.
                boxColor: Vector4.Zero,
                borderColor: Vector4.Zero,
                borderThickness: 1f,
                cornerRadii: Vector4.One * 4f,
                caretVisible: false,
                jitterStrength: style.CuneiformJitter,
                jitterSeed: Widget.Data.TaskId.GetHashCode(),
                rotationDegrees: style.CuneiformRotation,
                glow: style.CuneiformGlow)
            : new Padding(
                EdgeInsets.Symmetric(vertical: style.FieldPadY, horizontal: style.FieldPadX),
                child: new Text(Widget.Data.Text, textStyle));

        // On a read-only tablet, tapping the row TEXT is the "I want to edit this" gesture — there is no text
        // field to click into, so this is where the material-specific locked message is surfaced
        // (zero-point-three-fixes §7.4). Tasks only (a text-tap on a note has no completion/pin alternative to
        // disambiguate from, and the note is equally locked); the checkbox and pin sit in their own hit
        // regions, so this doesn't intercept them. Null callback (tabbed read view, wet tablet) leaves the
        // text inert as before.
        if (Widget.OnTextEditRefused is { } onTextEditRefused && Widget.Data.IsTask)
        {
            textChild = new GestureDetector(
                onPress: e => { e.Handled = true; onTextEditRefused(Widget.Data.TaskId); },
                child: textChild);
        }

        children.Add(new Expanded(child: textChild));

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
        // The hover pin follows the same live-toggle rule as the checkbox: offered on any editable read view
        // AND on a hard/fired tablet (zero-point-three-fixes §7.3 — pin/unpin stays reachable so a fired
        // tablet's pin is never stranded on the HUD). Only a non-live surface would hide it.
        var stackChildren = new List<Widget> { rowBody };
        if (hovered && Widget.Data.IsTask && Widget.TogglesLive)
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
