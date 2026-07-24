using System.Collections.Generic;
using System.Linq;
using Gui;                       // GuiDialogBlockEntityBase, WindowConfig
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle, FontWeight
using Gui.Widgets.Basic;         // Text, WindowFrame
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, Theme, ValueKey, Key
using Gui.Widgets.Input;         // Checkbox
using Gui.Widgets.Layout;        // Column, Row, Expanded, Padding, SizedBox, CrossAxisAlignment, MainAxisAlignment
using Gui.Widgets.Scroll;        // ListView
using Gui.Core.Layout;           // MainAxisSize
using OpenTK.Mathematics;        // Vector2
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;   // Lang, GlobalConstants
using Vintagestory.API.MathTools;  // BlockPos

namespace Scribe;

/// <summary>
/// The lectern's READ view, rebuilt on LibGUI (modid <c>gui</c>) as the first migration step off
/// the native <see cref="GuiDialogScribeLectern"/>'s absolute-bounds <c>GuiComposer</c> path
/// (adopt-libgui-foundation). This dialog is READ-ONLY: it renders the live document and lets a
/// viewer tick tasks off (lock-free), and its "switch to editor" control hands editing back to the
/// still-native editor view (design D2) so editing never breaks during the migration. The editor
/// view migrates in the follow-up change.
///
/// Opened the same way the native dialog is -- through the real interaction + packet flow (the
/// <c>scribe</c> channel), from <see cref="BlockEntityScribeLectern.HandleServerReply"/> -- not a
/// debug command and not a direct <see cref="ScribeDocument"/> reference. The document is captured
/// as an immutable snapshot per build (the block entity owns the authoritative copy); a server
/// re-sync calls <see cref="RefreshReadView"/> to rebuild.
/// </summary>
public sealed class GuiDialogScribeLecternLibGui : GuiDialogBlockEntityBase
{
    private readonly BlockEntityScribeLectern lectern;

    /// <summary>Fired when the read view's "switch to editor" control is activated. Wired by the
    /// block entity to the existing native editor open path (design D2) rather than owned here, so
    /// this dialog stays free of the lock/packet-request logic that lives on the block entity.</summary>
    private readonly System.Action onSwitchToEditor;

    public GuiDialogScribeLecternLibGui(BlockPos pos, BlockEntityScribeLectern lectern, ICoreClientAPI capi, System.Action onSwitchToEditor)
        : base(pos, capi)
    {
        this.lectern = lectern;
        this.onSwitchToEditor = onSwitchToEditor;
    }

    protected override WindowConfig CreateWindowConfig() => new()
    {
        Size = new Vector2(420, 520),
        Draggable = true,
        Resizable = true,
    };

    /// <summary>
    /// The native dialog overrode <c>IsInRangeOfBlock</c> to fix a Creative-mode walk-away bug: the
    /// engine inflates <c>PickingRange</c> to ~100 blocks in Creative, so the base's pick-range
    /// auto-close never fired. LibGUI's <see cref="GuiDialogBlockEntityBase"/> uses a different
    /// override point -- <c>OnFinalizeFrame</c> calls <c>IsOutOfRange(playerPos, pos,
    /// InteractionRange)</c>, and <c>InteractionRange</c> defaults to the same mode-inflated
    /// <c>PickingRange</c>. Pin it to the fixed survival interaction distance so walk-away
    /// auto-close fires in every game mode (adopt-libgui-foundation task 5.1). Small margin added,
    /// mirroring the native override.
    /// </summary>
    protected override double InteractionRange => GlobalConstants.DefaultPickingRange + 0.5;

    /// <summary>
    /// Called by <see cref="BlockEntityScribeLectern.FromTreeAttributes"/> whenever the
    /// authoritative document changes (e.g. another viewer toggled a task, or an editor autosaved).
    /// Tears down and rebuilds the whole widget tree from the now-current document: LibGUI's
    /// <see cref="ListView"/> caches its child rows by index and does NOT rebuild them on a plain
    /// parent rebuild unless the item count changes (VSAPI-NOTES LibGUI; the rows are otherwise
    /// self-stateful), so a full <see cref="GuiBase.ForceRebuild"/> is the reliable way to reflect
    /// an external state change in an already-open read view.
    /// </summary>
    public void RefreshReadView()
    {
        if (IsOpened())
        {
            ForceRebuild();
        }
    }

    protected override Widget Build() =>
        new WindowFrame(
            title: Lang.Get("scribe:scribe-gui-title"),
            onClose: () => TryClose(),
            fillHeight: true,
            child: new ScribeLecternReadContent(
                // Snapshot the block list for this build into value copies (index/text/done/isTask),
                // never a live block reference, so a later mutation of the authoritative document
                // can't alias into a built row -- a re-sync rebuilds instead.
                blocks: lectern.Document.Blocks
                    .Select((b, i) => new ScribeReadRowData(i, b.IsTask, b.Done, b.Text))
                    .ToList(),
                onToggleTask: OnReadViewToggleTask,
                onSwitchToEditor: () => onSwitchToEditor()));

    /// <summary>Read-view task checkbox click: fire-and-forget a lock-free toggle to the server.
    /// The read view holds no editor lock, so this uses the dedicated
    /// <see cref="ScribeToggleTaskMessage"/> rather than the lock-gated edit path. Mirrors the
    /// native dialog's <c>OnReadViewToggleTask</c>: the server applies it and re-syncs, and
    /// <see cref="BlockEntityScribeLectern.FromTreeAttributes"/> -> <see cref="RefreshReadView"/>
    /// rebuilds with the new state (the row also flips its own checkbox optimistically meanwhile).</summary>
    private void OnReadViewToggleTask(int index)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeToggleTaskMessage
        {
            PosX = lectern.Pos.X,
            PosY = lectern.Pos.Y,
            PosZ = lectern.Pos.Z,
            BlockIndex = index,
        });
    }
}

/// <summary>
/// A read-only row model: a value snapshot of one <see cref="ScribeBlock"/> plus its index. Passed
/// to <see cref="ScribeReadRow"/> so a row never holds a live block reference (see the snapshot note
/// in <see cref="GuiDialogScribeLecternLibGui.Build"/>).
/// </summary>
internal readonly record struct ScribeReadRowData(int Index, bool IsTask, bool Done, string Text);

/// <summary>
/// The read view's content tree: the document rendered as a scrollable <see cref="ListView"/> of
/// rows, with a "switch to editor" control below. A <see cref="StatefulWidget"/> so it can hold the
/// (stable) row list built once from the snapshot; the interactive per-row state lives in the row
/// widgets themselves (design D4), not here.
/// </summary>
internal sealed class ScribeLecternReadContent : StatefulWidget
{
    public ScribeLecternReadContent(
        IReadOnlyList<ScribeReadRowData> blocks,
        System.Action<int> onToggleTask,
        System.Action onSwitchToEditor)
    {
        Blocks = blocks;
        OnToggleTask = onToggleTask;
        OnSwitchToEditor = onSwitchToEditor;
    }

    public IReadOnlyList<ScribeReadRowData> Blocks { get; }
    public System.Action<int> OnToggleTask { get; }
    public System.Action OnSwitchToEditor { get; }

    public override State CreateState() => new ScribeLecternReadContentState();
}

internal sealed class ScribeLecternReadContentState : State<ScribeLecternReadContent>
{
    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;

        TextStyle switchTextStyle = new() { FontSize = 14, Color = colors.OnPrimary };

        // The scrollable row list. Each row is a self-stateful widget keyed by its block index so
        // the ListView tracks it across document changes and reorders (design D4). Uses the
        // variable-height ListView path so a wrapped multi-line note row measures to its real
        // height (VSAPI-NOTES: variableHeight is real despite the wiki).
        Widget rowList;
        if (Widget.Blocks.Count == 0)
        {
            rowList = new Padding(
                EdgeInsets.All(12),
                child: new Text(
                    Lang.Get("scribe:scribe-gui-edit-hint"),
                    new TextStyle { FontSize = 14, Color = colors.OnSurfaceVariant, SoftWrap = true }));
        }
        else
        {
            rowList = new ListView(
                children: Widget.Blocks
                    .Select(b => (Widget)new ScribeReadRow(b, Widget.OnToggleTask, new ValueKey<int>(b.Index)))
                    .ToList(),
                estimatedItemHeight: 34f,
                variableHeight: true);
        }

        return new Padding(
            EdgeInsets.All(10),
            child: new Column(
                spacing: 8,
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: new Widget[]
                {
                    // The row list takes all the height left between the title bar and the footer
                    // button. Expanded is valid here because the Column (via WindowFrame's
                    // fillHeight) receives a bounded height -- the ListView then does its own
                    // scrolling/clipping inside that box.
                    new Expanded(child: rowList),

                    // "Switch to editor" hands editing to the still-native editor view (design D2).
                    new Button(
                        child: new Text(Lang.Get("scribe:scribe-gui-switch-to-editor"), switchTextStyle),
                        onTap: _ => Widget.OnSwitchToEditor()),
                }));
    }
}

/// <summary>
/// One read-view row: a task checkbox (reflecting/toggling Done) or a note, plus wrapped text.
/// Self-stateful and keyed by <see cref="ScribeReadRowData.Index"/> (design D4): LibGUI's
/// <see cref="ListView"/> caches children by index and won't rebuild them on a parent rebuild, so
/// the row owns its own displayed Done state and flips it optimistically on click rather than
/// waiting for the parent to rebuild it. The authoritative re-sync (via
/// <see cref="GuiDialogScribeLecternLibGui.RefreshReadView"/>) is the source of truth and will
/// rebuild the whole tree; the optimistic flip just avoids a visible lag. Only the checkbox is
/// interactive -- the rest of the row is inert (no edit field, drag, or per-row icons in read view).
/// </summary>
internal sealed class ScribeReadRow : StatefulWidget
{
    public ScribeReadRow(ScribeReadRowData data, System.Action<int> onToggleTask, Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        Data = data;
        OnToggleTask = onToggleTask;
    }

    public ScribeReadRowData Data { get; }
    public System.Action<int> OnToggleTask { get; }

    public override State CreateState() => new ScribeReadRowState();
}

internal sealed class ScribeReadRowState : State<ScribeReadRow>
{
    private bool done;

    public override void InitState()
    {
        base.InitState();
        done = Widget.Data.Done;
    }

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        TextStyle textStyle = new() { FontSize = 14, Color = colors.OnSurface, SoftWrap = true };

        var children = new List<Widget>();

        if (Widget.Data.IsTask)
        {
            children.Add(new Checkbox(
                value: done,
                onChanged: _ =>
                {
                    // Optimistic local flip (own state -- not a parent rebuild, per design D4) so
                    // the check responds immediately; the server toggle + re-sync is authoritative.
                    SetState(() => done = !done);
                    Widget.OnToggleTask(Widget.Data.Index);
                },
                size: 22));
        }

        // Expanded so the text takes the row width left of the checkbox and wraps within it, rather
        // than overflowing. A note (no checkbox) gets the full width.
        children.Add(new Expanded(child: new Text(Widget.Data.Text, textStyle)));

        return new Padding(
            EdgeInsets.Symmetric(vertical: 4, horizontal: 2),
            child: new Row(
                spacing: 6,
                crossAxisAlignment: CrossAxisAlignment.Center,
                mainAxisSize: MainAxisSize.Max,
                children: children));
    }
}
