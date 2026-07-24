using System;
using System.Collections.Generic;
using System.Linq;
using Gui;                       // GuiDialogBlockEntityBase, WindowConfig
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle, FontWeight
using Gui.Widgets.Basic;         // Text, WindowFrame
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, Theme, ValueKey, Key
using Gui.Widgets.Input;         // Checkbox, FocusNode
using Gui.Widgets.Layout;        // Column, Row, Expanded, Padding, SizedBox, CrossAxisAlignment, MainAxisAlignment
using Gui.Widgets.Scroll;        // ListView, SingleChildScrollView, Scrollable
using Gui.Core.Layout;           // MainAxisSize
using OpenTK.Mathematics;        // Vector2
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;   // Lang, GlobalConstants
using Vintagestory.API.MathTools;  // BlockPos

namespace Scribe;

/// <summary>
/// The lectern dialog, rebuilt on LibGUI (modid <c>gui</c>). As of migrate-editor-view-libgui this
/// single dialog owns BOTH views: the READ view (lock-free, renders the live document and lets a
/// viewer tick tasks off) and the EDITOR view (lock-gated, in-place multi-line editing). Switching
/// between them is an internal view swap (<see cref="isEditorMode"/> + <see cref="GuiBase.ForceRebuild"/>),
/// so there is no native dialog in the loop and "done editing" returns straight to the LibGUI read
/// view (fixing the change-1 seam's backlogged return-path defect).
///
/// Opened through the real interaction + packet flow (the <c>scribe</c> channel), from
/// <see cref="BlockEntityScribeLectern.HandleServerReply"/> — not a debug command and not a direct
/// <see cref="ScribeDocument"/> reference. The read view snapshots the block entity's authoritative
/// document per build; the editor view edits a private scratch copy that is autosaved to the server
/// through the existing lock-gated <see cref="ScribeEditDocumentMessage"/> path.
/// </summary>
public sealed class GuiDialogScribeLecternLibGui : GuiDialogBlockEntityBase
{
    private readonly BlockEntityScribeLectern lectern;

    // ---- View state ----
    private bool isEditorMode;

    // ---- Editor state (null / inert while in read mode) ----
    /// <summary>Editor-view-only scratch copy; never aliased to <see cref="BlockEntityScribeLectern.Document"/>.</summary>
    private ScribeDocument? scratch;
    private bool isDirty;
    private long? autosaveTickListenerId;
    /// <summary>The block index of the editor row currently focused, tracked from the rows' focus
    /// nodes so the commit-on-close/switch path can normalize the right row.</summary>
    private int? focusedEditIndex;
    /// <summary>One focus node per editor row, owned by the dialog (not the row widgets) so focus can
    /// be moved across rows programmatically (Enter/Shift+Tab) — LibGUI has no focus-traversal API,
    /// so the parent coordinates focus manually. Kept in sync with <c>scratch.Blocks.Count</c>.</summary>
    private readonly List<FocusNode> editorFocusNodes = new();
    /// <summary>Row index to auto-focus on the next editor rebuild (a newly added task), or null.</summary>
    private int? autoFocusRowOnRebuild;
    /// <summary>Set when a focus move or a row growth needs the focused row scrolled into view; acted
    /// on in <see cref="OnRenderGUI"/> AFTER layout has run (EnsureVisible reads live geometry).</summary>
    private bool pendingEnsureVisible;

    public GuiDialogScribeLecternLibGui(BlockPos pos, BlockEntityScribeLectern lectern, ICoreClientAPI capi)
        : base(pos, capi)
    {
        this.lectern = lectern;
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
    /// auto-close fires in every game mode, including while a row is being edited.
    /// </summary>
    protected override double InteractionRange => GlobalConstants.DefaultPickingRange + 0.5;

    // ---------------- Input capture + macOS caret translation ----------------

    /// <summary>
    /// Capture ALL keyboard/mouse input while editing so typed keys (movement, hotbar, other
    /// keybinds) don't leak through to the game (migrate-editor-view-libgui task 2.6 / design D6 —
    /// the keypress-leak fix deferred from change 1). Only while in editor mode: the read view has no
    /// typing and should not trap game input.
    /// </summary>
    public override bool CaptureAllInputs() => isEditorMode;

    /// <summary>
    /// LibGUI's <see cref="Gui.Widgets.Events.KeyboardEvent"/> carries only Shift/Ctrl/Alt — it drops
    /// VS's Command modifier — so the macOS caret idioms can't be handled inside the editor field.
    /// Translate them here, before <c>base.OnKeyDown</c> maps the (mutable) VS <see cref="KeyEvent"/>
    /// into the Cmd-less LibGUI event: Cmd+Left/Right → Home/End (line start/end), Cmd+A/C/X/V →
    /// Ctrl+A/C/X/V (select-all / clipboard). Alt/Option is already delivered as Alt, so Alt+Arrow
    /// word-skip works in the field without translation. Mirrors the native
    /// <c>ScribeRowTextInput.TranslateMacCaretModifiers</c>, one layer up.
    /// </summary>
    public override void OnKeyDown(KeyEvent args)
    {
        if (isEditorMode && args.CommandPressed)
        {
            switch (args.KeyCode)
            {
                case (int)GlKeys.Left:
                    args.KeyCode = (int)GlKeys.Home;
                    args.CommandPressed = false;
                    break;
                case (int)GlKeys.Right:
                    args.KeyCode = (int)GlKeys.End;
                    args.CommandPressed = false;
                    break;
                case (int)GlKeys.A:
                case (int)GlKeys.C:
                case (int)GlKeys.X:
                case (int)GlKeys.V:
                    args.CtrlPressed = true;
                    args.CommandPressed = false;
                    break;
            }
        }

        base.OnKeyDown(args);
    }

    // ---------------- View switching ----------------

    /// <summary>Enter (or refresh) the editor view on the given authoritative document bytes. Called
    /// by <see cref="BlockEntityScribeLectern.HandleServerReply"/> when editor access is granted
    /// (the lock is now held). Seeds a fresh scratch copy, starts the autosave tick, and rebuilds
    /// into the editor tree (or lets <c>TryOpen</c> build it if the dialog isn't open yet).</summary>
    public void EnterEditorMode(byte[]? documentBytes)
    {
        scratch = ScribeDocumentCodec.TryDeserialize(documentBytes, out var doc) && doc is not null
            ? doc
            : new ScribeDocument();
        isDirty = false;
        isEditorMode = true;
        focusedEditIndex = null;
        autoFocusRowOnRebuild = null;
        SyncFocusNodesToScratch();
        StartAutosaveTick();

        if (IsOpened())
        {
            ForceRebuild();
        }
    }

    /// <summary>Enter (or stay in) the read view. Called on a read-access grant.</summary>
    public void EnterReadMode()
    {
        if (isEditorMode)
        {
            LeaveEditorMode();
        }
        if (IsOpened())
        {
            ForceRebuild();
        }
    }

    /// <summary>"Done editing" button: flush the pending edit, release the lock, and swap to the read
    /// view — all locally (read is lock-free and reads the block entity's now-optimistically-updated
    /// document). Flush BEFORE releasing the lock: the server processes packets in send order, so
    /// releasing first would let the flushed edit arrive lock-less and be rejected by
    /// <see cref="BlockEntityScribeLectern.ApplyEdit"/>'s lock check.</summary>
    private void OnClickSwitchToRead()
    {
        if (focusedEditIndex is { } idx) NormalizeRowOnCommit(idx);
        FlushIfDirty();
        SendReleaseLockPacket();
        LeaveEditorMode();
        ForceRebuild();
    }

    /// <summary>Tear down the editor state (stop autosave, drop scratch + focus nodes) and return to
    /// read mode. Does NOT flush or release the lock — callers do that first when appropriate.</summary>
    private void LeaveEditorMode()
    {
        StopAutosaveTick();
        isEditorMode = false;
        scratch = null;
        isDirty = false;
        focusedEditIndex = null;
        autoFocusRowOnRebuild = null;
        DisposeFocusNodes();
    }

    /// <summary>"Switch to editor" button in the read view: request editor access from the server
    /// (design D2 flow, unchanged). A granted reply round-trips back to
    /// <see cref="BlockEntityScribeLectern.HandleServerReply"/>, which calls
    /// <see cref="EnterEditorMode"/>.</summary>
    private void RequestEditorAccess()
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeRequestAccessMessage
        {
            PosX = lectern.Pos.X,
            PosY = lectern.Pos.Y,
            PosZ = lectern.Pos.Z,
            WantEditor = true,
        });
    }

    /// <summary>
    /// Called by <see cref="BlockEntityScribeLectern.FromTreeAttributes"/> whenever the authoritative
    /// document changes (e.g. another viewer toggled a task). Rebuilds the READ view from the
    /// now-current document. A NO-OP while in editor mode: the editor edits a private scratch copy and
    /// must not be clobbered by an external resync (mirrors the native editor's RefreshReadView).
    /// </summary>
    public void RefreshReadView()
    {
        if (!isEditorMode && IsOpened())
        {
            ForceRebuild();
        }
    }

    // ---------------- Editor operations (called from the editor rows) ----------------

    /// <summary>Live text-change from a focused editor field: write straight through to the scratch
    /// document's block and mark dirty (the autosave tick / commit path serializes it). The field
    /// auto-grows itself on wrap/newline, and the SingleChildScrollView+Column re-lays-out, so no
    /// explicit height tracking is needed here — just keep the growing focused row in view.</summary>
    private void NotifyTextChanged(int index, string text)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        scratch.SetBlockText(index, text);
        isDirty = true;
        pendingEnsureVisible = true;
    }

    /// <summary>Enter: commit the focused row and advance focus to the next.</summary>
    private void EditorAdvanceFrom(int index)
    {
        if (scratch is null) return;
        NormalizeRowOnCommit(index);
        FlushIfDirty();
        int next = Math.Min(index + 1, scratch.Blocks.Count - 1);
        FocusEditorRow(next);
    }

    /// <summary>Shift+Tab: commit the focused row and retreat focus to the previous.</summary>
    private void EditorRetreatFrom(int index)
    {
        if (scratch is null) return;
        NormalizeRowOnCommit(index);
        FlushIfDirty();
        int prev = Math.Max(index - 1, 0);
        FocusEditorRow(prev);
    }

    /// <summary>Enter: commit the focused row, then insert a new placeholder task directly beneath it
    /// and focus it (so the player types straight into the new row). Rebuilds because the row set
    /// changed. If the insert is rejected (shouldn't happen — placeholder is non-blank), falls back to
    /// advancing focus so Enter is never a dead key.</summary>
    private void EditorInsertTaskBelow(int index)
    {
        if (scratch is null) return;
        NormalizeRowOnCommit(index);
        FlushIfDirty();

        int insertAt = index + 1;
        if (scratch.InsertTask(insertAt, Lang.Get("scribe:scribe-gui-newtask-placeholder")))
        {
            isDirty = true;
            SyncFocusNodesToScratch();
            autoFocusRowOnRebuild = insertAt;
            focusedEditIndex = insertAt;
            pendingEnsureVisible = true;
            ForceRebuild();
        }
        else
        {
            FocusEditorRow(Math.Min(index + 1, scratch.Blocks.Count - 1));
        }
    }

    /// <summary>Editor checkbox toggle: mutate the scratch document and mark dirty (lock-gated, unlike
    /// the read view's lock-free toggle). The row flips its own checkbox optimistically.</summary>
    private void ToggleEditorTask(int index)
    {
        if (scratch is null) return;
        if (scratch.ToggleTask(index))
        {
            isDirty = true;
        }
    }

    /// <summary>"Add task" button: append a placeholder task, grow the focus-node list, and rebuild
    /// with the new row auto-focused so the player can type over the placeholder immediately.</summary>
    private void OnClickAddTask()
    {
        if (scratch is null) return;
        if (focusedEditIndex is { } leaving) NormalizeRowOnCommit(leaving);
        scratch.AddTask(Lang.Get("scribe:scribe-gui-newtask-placeholder"));
        isDirty = true;
        SyncFocusNodesToScratch();
        autoFocusRowOnRebuild = scratch.Blocks.Count - 1;
        pendingEnsureVisible = true;
        ForceRebuild();
    }

    /// <summary>Moves editor focus to <paramref name="index"/> by requesting focus on that row's node
    /// (the row stays mounted — the editor uses a non-virtualized scroll container, design D2 — so its
    /// node is always live) and scheduling a scroll-into-view.</summary>
    private void FocusEditorRow(int index)
    {
        if (index < 0 || index >= editorFocusNodes.Count) return;
        editorFocusNodes[index].RequestFocus();
        focusedEditIndex = index;
        pendingEnsureVisible = true;
    }

    /// <summary>
    /// Commit-time text normalization for a row (ported from the native editor): strip trailing blank
    /// lines and trailing whitespace while PRESERVING interior newlines, e.g. "a\n\nb\n" → "a\n\nb".
    /// Prevents a stray trailing Shift+Enter from committing a row that looks empty but stays tall,
    /// while keeping intentional interior spacing. Applied only at genuine row-commit sites (Enter,
    /// Shift+Tab, add-task, switch-to-read, close) — NOT on every keystroke or autosave tick, which
    /// would fight a player who just pressed Shift+Enter. No leading trim (indenting may be intended).
    /// </summary>
    private void NormalizeRowOnCommit(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        string current = scratch.Blocks[index].Text;
        string trimmed = current.TrimEnd();
        if (trimmed != current)
        {
            scratch.SetBlockText(index, trimmed);
            isDirty = true;
        }
    }

    // ---------------- Autosave / flush (throttled, lock-gated) ----------------

    private void StartAutosaveTick()
    {
        autosaveTickListenerId ??= capi.Event.RegisterGameTickListener(OnAutosaveTick, 1000);
    }

    private void StopAutosaveTick()
    {
        if (autosaveTickListenerId is { } id)
        {
            capi.Event.UnregisterGameTickListener(id);
            autosaveTickListenerId = null;
        }
    }

    private void OnAutosaveTick(float deltaTime) => FlushIfDirty();

    /// <summary>Send the scratch document to the server through the existing lock-gated edit path and
    /// optimistically update the local cache so an immediate switch-to-read doesn't flash pre-edit
    /// content. No-op when nothing changed. Semantics unchanged from the native editor's FlushIfDirty.</summary>
    private void FlushIfDirty()
    {
        if (!isDirty || scratch is null) return;

        var bytes = ScribeDocumentCodec.Serialize(scratch);
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeEditDocumentMessage
        {
            PosX = lectern.Pos.X,
            PosY = lectern.Pos.Y,
            PosZ = lectern.Pos.Z,
            DocumentBytes = bytes,
        });
        isDirty = false;

        // Un-aliased fresh copy: scratch keeps mutating while editing continues.
        if (ScribeDocumentCodec.TryDeserialize(bytes, out var copy) && copy is not null)
        {
            lectern.ApplyLocalOptimisticEdit(copy);
        }
    }

    // ---------------- Focus-node lifecycle ----------------

    /// <summary>Resize <see cref="editorFocusNodes"/> to one node per scratch block, keeping existing
    /// nodes (so a rebuild after add-task doesn't disturb other rows' focus). Each node carries a
    /// listener that tracks the focused row index and commits the row being left when focus moves
    /// row→row (e.g. clicking another row). Click-away-to-nothing is covered by the autosave tick and
    /// the close/switch commit.</summary>
    private void SyncFocusNodesToScratch()
    {
        int want = scratch?.Blocks.Count ?? 0;

        while (editorFocusNodes.Count > want)
        {
            int last = editorFocusNodes.Count - 1;
            editorFocusNodes[last].Dispose();
            editorFocusNodes.RemoveAt(last);
        }
        while (editorFocusNodes.Count < want)
        {
            int i = editorFocusNodes.Count;
            var node = new FocusNode();
            node.AddListener(() => OnRowFocusChanged(i));
            editorFocusNodes.Add(node);
        }
    }

    private void OnRowFocusChanged(int index)
    {
        if (index < 0 || index >= editorFocusNodes.Count) return;
        if (!editorFocusNodes[index].HasFocus) return;

        // A different row just gained focus (e.g. click-to-edit another row): commit the row we left.
        if (focusedEditIndex is { } prev && prev != index)
        {
            NormalizeRowOnCommit(prev);
            FlushIfDirty();
        }
        focusedEditIndex = index;
    }

    private void DisposeFocusNodes()
    {
        foreach (var node in editorFocusNodes) node.Dispose();
        editorFocusNodes.Clear();
    }

    // ---------------- Lifecycle ----------------

    /// <summary>After layout, honor a pending scroll-into-view for the focused editor row (a focus
    /// move or a row that grew while typing). Deferred to here because
    /// <see cref="Scrollable.EnsureVisible"/> reads the target's live post-layout geometry.</summary>
    public override void OnRenderGUI(float deltaTime)
    {
        base.OnRenderGUI(deltaTime);

        if (pendingEnsureVisible && isEditorMode && focusedEditIndex is { } idx
            && idx < editorFocusNodes.Count && editorFocusNodes[idx].Owner is { } element)
        {
            Scrollable.EnsureVisible(element);
            pendingEnsureVisible = false;
        }
    }

    public override void OnGuiClosed()
    {
        if (isEditorMode)
        {
            if (focusedEditIndex is { } closeIdx) NormalizeRowOnCommit(closeIdx);
            FlushIfDirty();
            SendReleaseLockPacket();
            StopAutosaveTick();
            DisposeFocusNodes();
        }
        base.OnGuiClosed();
    }

    private void SendReleaseLockPacket()
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeReleaseLockMessage
        {
            PosX = lectern.Pos.X,
            PosY = lectern.Pos.Y,
            PosZ = lectern.Pos.Z,
        });
    }

    // ---------------- Build ----------------

    protected override Widget Build() =>
        new WindowFrame(
            title: Lang.Get("scribe:scribe-gui-title"),
            onClose: () => TryClose(),
            fillHeight: true,
            child: isEditorMode ? BuildEditorContent() : BuildReadContent());

    private Widget BuildReadContent() =>
        new ScribeLecternReadContent(
            // Snapshot the block list for this build into value copies (never a live block
            // reference), so a later mutation of the authoritative document can't alias into a built
            // row — a re-sync rebuilds instead.
            blocks: lectern.Document.Blocks
                .Select((b, i) => new ScribeReadRowData(i, b.IsTask, b.Done, b.Text))
                .ToList(),
            onToggleTask: OnReadViewToggleTask,
            onSwitchToEditor: RequestEditorAccess);

    private Widget BuildEditorContent()
    {
        var blocks = scratch!.Blocks
            .Select((b, i) => new ScribeEditRowData(i, b.IsTask, b.Done, b.Text))
            .ToList();

        int? autoFocus = autoFocusRowOnRebuild;
        autoFocusRowOnRebuild = null; // one-shot

        return new ScribeLecternEditorContent(
            blocks: blocks,
            focusNodes: editorFocusNodes,
            autoFocusIndex: autoFocus,
            onTextChanged: NotifyTextChanged,
            onCommitAndAdvance: EditorAdvanceFrom,
            onCommitAndRetreat: EditorRetreatFrom,
            onInsertTaskBelow: EditorInsertTaskBelow,
            onToggleTask: ToggleEditorTask,
            onAddTask: OnClickAddTask,
            onSwitchToRead: OnClickSwitchToRead);
    }

    /// <summary>Read-view task checkbox click: fire-and-forget a lock-free toggle to the server. The
    /// read view holds no editor lock, so this uses the dedicated <see cref="ScribeToggleTaskMessage"/>
    /// rather than the lock-gated edit path.</summary>
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

// ============================================================================
// Read view content (unchanged behavior from change 1)
// ============================================================================

/// <summary>
/// A read-only row model: a value snapshot of one <see cref="ScribeBlock"/> plus its index. Passed
/// to <see cref="ScribeReadRow"/> so a row never holds a live block reference.
/// </summary>
internal readonly record struct ScribeReadRowData(int Index, bool IsTask, bool Done, string Text);

/// <summary>
/// The read view's content tree: the document rendered as a scrollable <see cref="ListView"/> of
/// rows, with a "switch to editor" control below. The interactive per-row state lives in the row
/// widgets themselves (design D4), not here.
/// </summary>
internal sealed class ScribeLecternReadContent : StatefulWidget
{
    public ScribeLecternReadContent(
        IReadOnlyList<ScribeReadRowData> blocks,
        Action<int> onToggleTask,
        Action onSwitchToEditor)
    {
        Blocks = blocks;
        OnToggleTask = onToggleTask;
        OnSwitchToEditor = onSwitchToEditor;
    }

    public IReadOnlyList<ScribeReadRowData> Blocks { get; }
    public Action<int> OnToggleTask { get; }
    public Action OnSwitchToEditor { get; }

    public override State CreateState() => new ScribeLecternReadContentState();
}

internal sealed class ScribeLecternReadContentState : State<ScribeLecternReadContent>
{
    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;

        TextStyle switchTextStyle = new() { FontSize = 14, Color = colors.OnPrimary };

        // The scrollable row list. Each row is a self-stateful widget keyed by its block index so the
        // ListView tracks it across document changes (design D4). variableHeight so a wrapped
        // multi-line note row measures to its real height.
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
                    new Expanded(child: rowList),
                    new Button(
                        child: new Text(Lang.Get("scribe:scribe-gui-switch-to-editor"), switchTextStyle),
                        onTap: _ => Widget.OnSwitchToEditor()),
                }));
    }
}

/// <summary>
/// One read-view row: a task checkbox (reflecting/toggling Done) or a note, plus wrapped text.
/// Self-stateful and keyed (design D4). Only the checkbox is interactive.
/// </summary>
internal sealed class ScribeReadRow : StatefulWidget
{
    public ScribeReadRow(ScribeReadRowData data, Action<int> onToggleTask, Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        Data = data;
        OnToggleTask = onToggleTask;
    }

    public ScribeReadRowData Data { get; }
    public Action<int> OnToggleTask { get; }

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
                    SetState(() => done = !done);
                    Widget.OnToggleTask(Widget.Data.Index);
                },
                size: 22));
        }

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

// ============================================================================
// Editor view content (migrate-editor-view-libgui)
// ============================================================================

/// <summary>A value snapshot of one editable block plus its index. The live text lives in the
/// dialog's scratch document (the field writes through on every keystroke); this is only the seed
/// for building the row.</summary>
internal readonly record struct ScribeEditRowData(int Index, bool IsTask, bool Done, string Text);

/// <summary>
/// The editor view's content tree. Unlike the read view it uses a NON-virtualized
/// <see cref="SingleChildScrollView"/> + <see cref="Column"/> of ALL rows (design D2): LibGUI's
/// <see cref="ListView"/> unmounts off-screen rows, which would destroy an off-screen row's focus
/// node and drop focus/caret when a focused row grows past the viewport. Keeping every row mounted
/// lets the dialog coordinate cross-row focus (Enter/Shift+Tab) and keep a growing focused row in
/// view. A footer carries "Add task" and "Done editing".
/// </summary>
internal sealed class ScribeLecternEditorContent : StatefulWidget
{
    public ScribeLecternEditorContent(
        IReadOnlyList<ScribeEditRowData> blocks,
        IReadOnlyList<FocusNode> focusNodes,
        int? autoFocusIndex,
        Action<int, string> onTextChanged,
        Action<int> onCommitAndAdvance,
        Action<int> onCommitAndRetreat,
        Action<int> onInsertTaskBelow,
        Action<int> onToggleTask,
        Action onAddTask,
        Action onSwitchToRead)
    {
        Blocks = blocks;
        FocusNodes = focusNodes;
        AutoFocusIndex = autoFocusIndex;
        OnTextChanged = onTextChanged;
        OnCommitAndAdvance = onCommitAndAdvance;
        OnCommitAndRetreat = onCommitAndRetreat;
        OnInsertTaskBelow = onInsertTaskBelow;
        OnToggleTask = onToggleTask;
        OnAddTask = onAddTask;
        OnSwitchToRead = onSwitchToRead;
    }

    public IReadOnlyList<ScribeEditRowData> Blocks { get; }
    public IReadOnlyList<FocusNode> FocusNodes { get; }
    public int? AutoFocusIndex { get; }
    public Action<int, string> OnTextChanged { get; }
    public Action<int> OnCommitAndAdvance { get; }
    public Action<int> OnCommitAndRetreat { get; }
    public Action<int> OnInsertTaskBelow { get; }
    public Action<int> OnToggleTask { get; }
    public Action OnAddTask { get; }
    public Action OnSwitchToRead { get; }

    public override State CreateState() => new ScribeLecternEditorContentState();
}

internal sealed class ScribeLecternEditorContentState : State<ScribeLecternEditorContent>
{
    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        TextStyle buttonTextStyle = new() { FontSize = 14, Color = colors.OnPrimary };

        Widget scrollBody;
        if (Widget.Blocks.Count == 0)
        {
            scrollBody = new Padding(
                EdgeInsets.All(12),
                child: new Text(
                    Lang.Get("scribe:scribe-gui-edit-hint"),
                    new TextStyle { FontSize = 14, Color = colors.OnSurfaceVariant, SoftWrap = true }));
        }
        else
        {
            var rows = Widget.Blocks
                .Select(b => (Widget)new ScribeEditRow(
                    data: b,
                    focusNode: b.Index < Widget.FocusNodes.Count ? Widget.FocusNodes[b.Index] : null,
                    autoFocus: Widget.AutoFocusIndex == b.Index,
                    onTextChanged: Widget.OnTextChanged,
                    onCommitAndAdvance: Widget.OnCommitAndAdvance,
                    onCommitAndRetreat: Widget.OnCommitAndRetreat,
                    onInsertTaskBelow: Widget.OnInsertTaskBelow,
                    onToggleTask: Widget.OnToggleTask,
                    key: new ValueKey<int>(b.Index)))
                .ToList();

            scrollBody = new SingleChildScrollView(
                child: new Column(
                    spacing: 6,
                    crossAxisAlignment: CrossAxisAlignment.Stretch,
                    mainAxisSize: MainAxisSize.Min,
                    children: rows));
        }

        return new Padding(
            EdgeInsets.All(10),
            child: new Column(
                spacing: 8,
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: new Widget[]
                {
                    new Expanded(child: scrollBody),
                    new Row(
                        spacing: 8,
                        mainAxisSize: MainAxisSize.Max,
                        children: new Widget[]
                        {
                            new Expanded(child: new Button(
                                child: new Text(Lang.Get("scribe:scribe-gui-addtask"), buttonTextStyle),
                                onTap: _ => Widget.OnAddTask())),
                            new Expanded(child: new Button(
                                child: new Text(Lang.Get("scribe:scribe-gui-switch-to-read"), buttonTextStyle),
                                onTap: _ => Widget.OnSwitchToRead())),
                        }),
                }));
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
        Action<int, string> onTextChanged,
        Action<int> onCommitAndAdvance,
        Action<int> onCommitAndRetreat,
        Action<int> onInsertTaskBelow,
        Action<int> onToggleTask,
        Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        Data = data;
        FocusNode = focusNode;
        AutoFocus = autoFocus;
        OnTextChanged = onTextChanged;
        OnCommitAndAdvance = onCommitAndAdvance;
        OnCommitAndRetreat = onCommitAndRetreat;
        OnInsertTaskBelow = onInsertTaskBelow;
        OnToggleTask = onToggleTask;
    }

    public ScribeEditRowData Data { get; }
    public FocusNode? FocusNode { get; }
    public bool AutoFocus { get; }
    public Action<int, string> OnTextChanged { get; }
    public Action<int> OnCommitAndAdvance { get; }
    public Action<int> OnCommitAndRetreat { get; }
    public Action<int> OnInsertTaskBelow { get; }
    public Action<int> OnToggleTask { get; }

    public override State CreateState() => new ScribeEditRowState();
}

internal sealed class ScribeEditRowState : State<ScribeEditRow>
{
    private bool done;

    public override void InitState()
    {
        base.InitState();
        done = Widget.Data.Done;
    }

    public override Widget Build(BuildContext context)
    {
        int index = Widget.Data.Index;

        var children = new List<Widget>();

        if (Widget.Data.IsTask)
        {
            children.Add(new Checkbox(
                value: done,
                onChanged: _ =>
                {
                    SetState(() => done = !done);
                    Widget.OnToggleTask(index);
                },
                size: 22));
        }

        children.Add(new Expanded(child: new ScribeMultilineField(
            initialText: Widget.Data.Text,
            focusNode: Widget.FocusNode,
            fontSize: 15f,
            autoFocus: Widget.AutoFocus,
            onChanged: text => Widget.OnTextChanged(index, text),
            onCommitAndAdvance: () => Widget.OnCommitAndAdvance(index),
            onCommitAndRetreat: () => Widget.OnCommitAndRetreat(index),
            onInsertTaskBelow: () => Widget.OnInsertTaskBelow(index))));

        return new Padding(
            EdgeInsets.Symmetric(vertical: 4, horizontal: 2),
            child: new Row(
                spacing: 6,
                crossAxisAlignment: CrossAxisAlignment.Start,
                mainAxisSize: MainAxisSize.Max,
                children: children));
    }
}
