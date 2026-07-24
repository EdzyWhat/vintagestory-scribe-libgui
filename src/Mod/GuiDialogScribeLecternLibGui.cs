using System;
using System.Collections.Generic;
using System.Linq;
using Gui;                       // GuiDialogBlockEntityBase, WindowConfig
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle, FontWeight
using Gui.Widgets.Basic;         // Text, WindowFrame, VsIcon, Container, Button
using Gui.Widgets.Events;        // PointerEvent
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, Theme, ValueKey, Key
using Gui.Widgets.Input;         // Checkbox, FocusNode, GestureDetector, MouseRegion
using Gui.Widgets.Gestures;      // ScrollController
using Gui.Widgets.Layout;        // Column, Row, Expanded, Padding, SizedBox, Center, CrossAxisAlignment, MainAxisAlignment
using Gui.Widgets.Painting;      // BoxStyle
using Gui.Widgets.Scroll;        // ListView, SingleChildScrollView, Scrollable, Scrollbar
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

    /// <summary>Row-sizing style for this dialog instance, loaded from client config when the dialog
    /// opens (see the constructor). Loading per-open means editing <c>scribe-client-config.json</c>
    /// and reopening the lectern applies new values, with no shared mutable state.</summary>
    private readonly ScribeRowStyle rowStyle;

    /// <summary>One scroll controller shared by BOTH views' scroll regions, owned by the dialog rather
    /// than each view's <c>State</c>. Because a view switch is a <see cref="GuiBase.ForceRebuild"/> that
    /// tears down the outgoing view's <c>State</c> (which would dispose a State-owned controller and
    /// lose the offset), sharing one dialog-lived controller keeps the scroll position across the
    /// switch — and since row heights are unified across views (<see cref="ScribeRowStyle"/>), the same
    /// offset shows the same rows. Passed into the <c>ListView</c>/<c>SingleChildScrollView</c>, which
    /// then do NOT dispose it (they only dispose their own internal fallback); the dialog disposes it in
    /// <see cref="OnGuiClosed"/>. An offset past a shorter view's max is clamped on layout.</summary>
    private readonly ScrollController sharedScrollController = new();

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

    /// <summary>Scroll offset captured just before a switch-to-read rebuild, to be re-applied after the
    /// read view lays out. Needed because the read view's virtualized <c>ListView</c> re-derives its
    /// content height (hence <c>MaxScrollExtent</c>) from <c>estimatedItemHeight</c> on the FIRST layout
    /// after the swap, so the shared controller's offset gets clamped toward the top before the real row
    /// heights are known — the edit→read half of the "scroll survives the switch" behavior. (read→edit
    /// doesn't need this: the editor's non-virtualized scroll view measures full content immediately.)
    /// Null when no restore is pending. See <see cref="OnRenderGUI"/> for the re-apply.</summary>
    private float? pendingRestoreScrollOffset;
    /// <summary>Frames spent trying to re-apply <see cref="pendingRestoreScrollOffset"/>; bounds the
    /// retry so a genuinely-shorter list (target past the real max) gives up instead of looping.</summary>
    private int scrollRestoreFrames;

    // ---- Lost-lock autosave recovery (task 8.6) ----
    /// <summary>Max consecutive autosave failures to auto-recover from before giving up, so a lock
    /// permanently held elsewhere can't spin the re-request/resend loop forever.</summary>
    private const int MaxSaveFailureRetries = 3;
    /// <summary>Count of consecutive lost-lock autosave failures; reset to 0 the moment the lock is
    /// re-acquired (a successful recovery re-grant) or a fresh editor session begins.</summary>
    private int saveFailureRetries;
    /// <summary>True between a save-failed ack and its recovery re-grant, so <see cref="EnterEditorMode"/>
    /// keeps the unsaved scratch instead of reseeding it from the authoritative document.</summary>
    private bool recoveringLostLock;

    public GuiDialogScribeLecternLibGui(BlockPos pos, BlockEntityScribeLectern lectern, ICoreClientAPI capi)
        : base(pos, capi)
    {
        this.lectern = lectern;

        // Load row-sizing config fresh per open (matches this dialog's per-open lifecycle) so a
        // hand-edit of the JSON -- or a ConfigLib panel change -- is picked up on the next open.
        // Falls back to defaults when the file doesn't exist yet.
        var config = capi.LoadModConfig<ScribeClientConfig>(ScribeModSystem.ClientConfigFileName)
                     ?? new ScribeClientConfig();
        rowStyle = ScribeRowStyle.FromConfig(config);
    }

    protected override WindowConfig CreateWindowConfig() => new()
    {
        // 567px wide to match the vanilla survival Handbook's dialog: its detail view composes to a
        // 567px outer width (500px content clip -> FixedGrow(6) = 506 -> ForkBoundingParent(5,_,36,_)
        // = 547 -> +2x10 ElementToDialogPadding = 567; confirmed by decompiling
        // Vintagestory.GameContent.GuiDialogHandbook). Height unchanged.
        Size = new Vector2(567, 520),
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
        if (recoveringLostLock && scratch is not null)
        {
            // This grant is the recovery re-acquire after a lost-lock save failure (task 8.6): the
            // lock is ours again, so keep the player's unsaved scratch and re-flush it rather than
            // reseeding from the authoritative document (which would silently discard their edits).
            recoveringLostLock = false;
            saveFailureRetries = 0;
            isEditorMode = true;
            StartAutosaveTick();
            isDirty = true;   // force the pending edit to be re-sent on the next flush
            FlushIfDirty();
            if (IsOpened()) ForceRebuild();
            return;
        }

        scratch = ScribeDocumentCodec.TryDeserialize(documentBytes, out var doc) && doc is not null
            ? doc
            : new ScribeDocument();
        isDirty = false;
        isEditorMode = true;
        focusedEditIndex = null;
        autoFocusRowOnRebuild = null;
        saveFailureRetries = 0;
        recoveringLostLock = false;
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
        CaptureScrollForRestore();
        LeaveEditorMode();
        ForceRebuild();
    }

    /// <summary>Snapshot the current scroll offset so it can be re-applied after the next view's first
    /// layout (see <see cref="pendingRestoreScrollOffset"/>). Called before a switch-to-read rebuild so
    /// the read view's ListView content-height re-derivation can't strand the offset at the top.</summary>
    private void CaptureScrollForRestore()
    {
        pendingRestoreScrollOffset = sharedScrollController.Offset;
        scrollRestoreFrames = 0;
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
        saveFailureRetries = 0;
        recoveringLostLock = false;
        DisposeFocusNodes();
    }

    /// <summary>
    /// Called by <see cref="BlockEntityScribeLectern.HandleServerReply"/> when an autosave was rejected
    /// because this client lost the editor lock (task 8.6). The failing <see cref="FlushIfDirty"/>
    /// already cleared <see cref="isDirty"/> optimistically, so without recovery the unsaved edit
    /// would be silently dropped. Instead we re-request the editor lock (keeping the scratch intact via
    /// <see cref="recoveringLostLock"/>); a re-grant lands back in <see cref="EnterEditorMode"/>, which
    /// re-flushes the pending edit. Bounded by <see cref="MaxSaveFailureRetries"/> so a lock genuinely
    /// held by another player can't spin this forever — after the cap we stop, leaving the edits visible
    /// in the (now read-only-until-relock) scratch and the one-time error toast already shown.
    /// Returns true if a recovery re-request was sent, false if the cap was hit.
    /// </summary>
    public bool HandleSaveFailed()
    {
        if (!isEditorMode) return false;

        if (saveFailureRetries >= MaxSaveFailureRetries)
        {
            recoveringLostLock = false;
            return false;
        }

        saveFailureRetries++;
        recoveringLostLock = true;
        RequestEditorAccess();
        return true;
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

    /// <summary>Per-row delete: remove the block from the scratch document and rebuild. Mirrors
    /// <see cref="OnClickAddTask"/> (mutate scratch -> mark dirty -> resync focus nodes -> rebuild),
    /// with focus cleanup so no path is left focusing a removed row: if the focused row was deleted
    /// or shifted, clear/adjust <see cref="focusedEditIndex"/> before the rebuild (spec "deleting the
    /// focused row does not break focus"). An empty document falls back to the editor's empty-state
    /// hint, which needs no focus.</summary>
    private void DeleteEditorBlock(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        if (!scratch.DeleteBlock(index)) return;

        isDirty = true;

        // Fix up the focused index across the deletion: the focused row is gone (clear), or sat after
        // the deleted one (shift up by one), or before it (unchanged).
        if (focusedEditIndex is { } f)
        {
            if (f == index) focusedEditIndex = null;
            else if (f > index) focusedEditIndex = f - 1;
        }

        SyncFocusNodesToScratch();
        pendingEnsureVisible = false;
        ForceRebuild();
    }

    /// <summary>Per-row pin toggle (task rows only; the pin control is absent on text-section rows).
    /// Flips the scratch block's pinned flag and marks dirty; the autosave/commit path serializes it.
    /// The row reflects the new state via its own rebuild, so no full ForceRebuild is required — but
    /// the resting-tint indicator (read + editor) is driven off the block's Pinned snapshot, so a
    /// rebuild keeps both views consistent.</summary>
    private void TogglePinnedEditorTask(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        if (!scratch.TogglePinned(index)) return; // no-op on a text section or bad index

        isDirty = true;
        ForceRebuild();
    }

    /// <summary>Drag-reorder drop: move the block from <paramref name="from"/> to <paramref name="to"/>
    /// in the scratch document and rebuild. A move-to-same index is a safe no-op
    /// (<see cref="ScribeDocument.MoveBlock"/> returns true without changing anything, so no edit is
    /// sent). Keeps the moved row focused across the rebuild.</summary>
    private void ReorderEditorBlock(int from, int to)
    {
        if (scratch is null) return;
        if (from == to) return; // released in place -> no edit (spec "Dropping in place changes nothing")
        if (!scratch.MoveBlock(from, to)) return;

        isDirty = true;
        focusedEditIndex = to;
        SyncFocusNodesToScratch();
        autoFocusRowOnRebuild = to;
        pendingEnsureVisible = true;
        ForceRebuild();
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

        // Re-apply a scroll offset captured across a view switch (see pendingRestoreScrollOffset). The
        // read view's ListView settles its content height over the first frame(s) after the swap, so a
        // single JumpTo can still be clamped; re-apply until the offset sticks or we've given a few
        // frames (a genuinely shorter list clamps to its real max and stops differing, so this also
        // self-terminates when the target is simply unreachable).
        if (pendingRestoreScrollOffset is { } want)
        {
            sharedScrollController.JumpTo(want);
            scrollRestoreFrames++;
            if (Math.Abs(sharedScrollController.Offset - want) < 0.5f || scrollRestoreFrames >= 5)
            {
                pendingRestoreScrollOffset = null;
            }
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
        // The dialog owns the shared scroll controller (see its field); dispose it once here rather
        // than in either view's State, which come and go with each view-switch ForceRebuild.
        sharedScrollController.Dispose();
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
                .Select((b, i) => new ScribeReadRowData(i, b.IsTask, b.Done, b.Pinned, b.Text))
                .ToList(),
            onToggleTask: OnReadViewToggleTask,
            onSwitchToEditor: RequestEditorAccess,
            style: rowStyle,
            scrollController: sharedScrollController);

    private Widget BuildEditorContent()
    {
        var blocks = scratch!.Blocks
            .Select((b, i) => new ScribeEditRowData(i, b.IsTask, b.Done, b.Pinned, b.Text))
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
            onDeleteBlock: DeleteEditorBlock,
            onTogglePinned: TogglePinnedEditorTask,
            onReorderBlock: ReorderEditorBlock,
            onAddTask: OnClickAddTask,
            onSwitchToRead: OnClickSwitchToRead,
            style: rowStyle,
            scrollController: sharedScrollController);
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
internal readonly record struct ScribeReadRowData(int Index, bool IsTask, bool Done, bool Pinned, string Text);

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
        Action onSwitchToEditor,
        ScribeRowStyle style,
        ScrollController scrollController)
    {
        Blocks = blocks;
        OnToggleTask = onToggleTask;
        OnSwitchToEditor = onSwitchToEditor;
        Style = style;
        ScrollController = scrollController;
    }

    public IReadOnlyList<ScribeReadRowData> Blocks { get; }
    public Action<int> OnToggleTask { get; }
    public Action OnSwitchToEditor { get; }
    public ScribeRowStyle Style { get; }
    /// <summary>Dialog-owned scroll controller shared by both views (see the dialog field); NOT disposed
    /// here — the dialog owns its lifetime so the scroll offset survives the view-switch rebuild.</summary>
    public ScrollController ScrollController { get; }

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
        // multi-line note row measures to its real height. Wrapped in a Scrollbar so a list taller
        // than the viewport shows a draggable track (wheel scroll worked before; the visible bar
        // did not exist — task 8.15).
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
            var style = Widget.Style;
            // AutoHide off: keep the bar permanently visible (matches the pre-LibGUI native GUI). This
            // also sidesteps the flicker where a ForceRebuild (delete/pin/reorder) nudged the controller
            // and re-triggered the auto-hide fade-in/out (task 5.7). AutoHide is an init-only property,
            // not a ctor param, so it's set in an object initializer.
            rowList = new Scrollbar(
                controller: Widget.ScrollController,
                child: new ListView(
                    children: Widget.Blocks
                        .Select(b => (Widget)new ScribeReadRow(b, Widget.OnToggleTask, style, new ValueKey<int>(b.Index)))
                        .ToList(),
                    // Scroll estimate only (variableHeight measures the real height); keep it close to a
                    // single-line row's true height so the scrollbar doesn't jump: font line + field pad + row pad.
                    estimatedItemHeight: style.FontSize * 1.2f + style.FieldPadY * 2 + style.RowVerticalPadding * 2,
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
    public ScribeReadRow(ScribeReadRowData data, Action<int> onToggleTask, ScribeRowStyle style, Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        Data = data;
        OnToggleTask = onToggleTask;
        Style = style;
    }

    public ScribeReadRowData Data { get; }
    public Action<int> OnToggleTask { get; }
    public ScribeRowStyle Style { get; }

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
        var style = Widget.Style;
        TextStyle textStyle = new() { FontSize = style.FontSize, Color = colors.OnSurface, SoftWrap = true };

        var children = new List<Widget>();

        // Reserve the same far-left grip column the editor row draws (f07783f7), so read and edit rows
        // are column-identical and align seamlessly across a view switch. It's the actual grip glyph
        // (keeps the reserved width in lockstep with the editor's grip if ControlSize changes) but drawn
        // at zero opacity and with NO gesture wrapper -- purely a spacer, uninteractable and invisible.
        // The read view exposes no reorder (dragging is a lock-gated authoring action, design D4). Nudged
        // down by the same amount as the editor grip so the reserved column matches row-for-row.
        children.Add(new Padding(
            EdgeInsets.Only(top: ScribeRowControlNudge.CheckboxAndGripTop),
            child: new Opacity(
                opacity: 0f,
                child: new ScribeVsIconGlyph("scribegrip", style.ControlSize, colors.OnSurfaceVariant))));

        if (Widget.Data.IsTask)
        {
            children.Add(new Padding(
                EdgeInsets.Only(top: ScribeRowControlNudge.CheckboxAndGripTop),
                child: new Checkbox(
                    value: done,
                    onChanged: _ =>
                    {
                        SetState(() => done = !done);
                        Widget.OnToggleTask(Widget.Data.Index);
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
        // it reads as pinned without hovering. Unpinned tasks and text sections get no tint. The read
        // view exposes no pin *toggle* (pinning is a lock-gated authoring action, design D4) — only this.
        if (Widget.Data.IsTask && Widget.Data.Pinned)
        {
            rowBody = new Container(
                style: new BoxStyle { Color = style.PinnedTint },
                child: rowBody);
        }

        return rowBody;
    }
}

// ============================================================================
// Editor view content (migrate-editor-view-libgui)
// ============================================================================

/// <summary>A value snapshot of one editable block plus its index. The live text lives in the
/// dialog's scratch document (the field writes through on every keystroke); this is only the seed
/// for building the row.</summary>
internal readonly record struct ScribeEditRowData(int Index, bool IsTask, bool Done, bool Pinned, string Text);

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
        Action<int> onDeleteBlock,
        Action<int> onTogglePinned,
        Action<int, int> onReorderBlock,
        Action onAddTask,
        Action onSwitchToRead,
        ScribeRowStyle style,
        ScrollController scrollController)
    {
        Blocks = blocks;
        FocusNodes = focusNodes;
        AutoFocusIndex = autoFocusIndex;
        OnTextChanged = onTextChanged;
        OnCommitAndAdvance = onCommitAndAdvance;
        OnCommitAndRetreat = onCommitAndRetreat;
        OnInsertTaskBelow = onInsertTaskBelow;
        OnToggleTask = onToggleTask;
        OnDeleteBlock = onDeleteBlock;
        OnTogglePinned = onTogglePinned;
        OnReorderBlock = onReorderBlock;
        OnAddTask = onAddTask;
        OnSwitchToRead = onSwitchToRead;
        Style = style;
        ScrollController = scrollController;
    }

    public IReadOnlyList<ScribeEditRowData> Blocks { get; }
    public IReadOnlyList<FocusNode> FocusNodes { get; }
    public int? AutoFocusIndex { get; }
    public Action<int, string> OnTextChanged { get; }
    public Action<int> OnCommitAndAdvance { get; }
    public Action<int> OnCommitAndRetreat { get; }
    public Action<int> OnInsertTaskBelow { get; }
    public Action<int> OnToggleTask { get; }
    public Action<int> OnDeleteBlock { get; }
    public Action<int> OnTogglePinned { get; }
    /// <summary>Reorder a block from one index to another (drag drop). See
    /// <see cref="ScribeLecternEditorContentState"/> for the drag mechanics.</summary>
    public Action<int, int> OnReorderBlock { get; }
    public Action OnAddTask { get; }
    public Action OnSwitchToRead { get; }
    public ScribeRowStyle Style { get; }
    /// <summary>Dialog-owned scroll controller shared by both views (see the dialog field); NOT disposed
    /// here — the dialog owns its lifetime so the scroll offset survives the view-switch rebuild. The
    /// same controller <see cref="Scrollable.EnsureVisible"/> drives when a focused row grows/moves.</summary>
    public ScrollController ScrollController { get; }

    public override State CreateState() => new ScribeLecternEditorContentState();
}

internal sealed class ScribeLecternEditorContentState : State<ScribeLecternEditorContent>
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
                    isDropTarget: dragFromIndex is not null && dragOverIndex == b.Index,
                    onTextChanged: Widget.OnTextChanged,
                    onCommitAndAdvance: Widget.OnCommitAndAdvance,
                    onCommitAndRetreat: Widget.OnCommitAndRetreat,
                    onInsertTaskBelow: Widget.OnInsertTaskBelow,
                    onToggleTask: Widget.OnToggleTask,
                    onDelete: Widget.OnDeleteBlock,
                    onTogglePinned: Widget.OnTogglePinned,
                    onDragStart: OnRowDragStart,
                    onDragOver: OnRowDragOver,
                    onDragEnd: OnRowDragEnd,
                    style: Widget.Style,
                    key: new ValueKey<int>(b.Index)))
                .ToList();

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
        bool isDropTarget,
        Action<int, string> onTextChanged,
        Action<int> onCommitAndAdvance,
        Action<int> onCommitAndRetreat,
        Action<int> onInsertTaskBelow,
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
        OnTextChanged = onTextChanged;
        OnCommitAndAdvance = onCommitAndAdvance;
        OnCommitAndRetreat = onCommitAndRetreat;
        OnInsertTaskBelow = onInsertTaskBelow;
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
    /// paints a highlight so the player sees where the dragged row would land.</summary>
    public bool IsDropTarget { get; }
    public Action<int, string> OnTextChanged { get; }
    public Action<int> OnCommitAndAdvance { get; }
    public Action<int> OnCommitAndRetreat { get; }
    public Action<int> OnInsertTaskBelow { get; }
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
    /// signal that drives <see cref="ScribeLecternEditorContentState.OnRowDragOver"/>. The grip itself
    /// is NOT hover-gated (it stays mounted so a drag it started can't lose pointer capture mid-move).</summary>
    private bool hovered;

    public override void InitState()
    {
        base.InitState();
        done = Widget.Data.Done;
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
        // drag hit-target still covers the visible glyph.
        children.Add(new Padding(
            EdgeInsets.Only(top: ScribeRowControlNudge.CheckboxAndGripTop),
            child: new GestureDetector(
                onPress: _ => Widget.OnDragStart(index),
                onRelease: _ => Widget.OnDragEnd(),
                child: new ScribeVsIconGlyph("scribegrip", style.ControlSize, colors.OnSurfaceVariant))));

        if (Widget.Data.IsTask)
        {
            children.Add(new Padding(
                EdgeInsets.Only(top: ScribeRowControlNudge.CheckboxAndGripTop),
                child: new Checkbox(
                    value: done,
                    onChanged: _ =>
                    {
                        SetState(() => done = !done);
                        Widget.OnToggleTask(index);
                    },
                    size: style.CheckboxSize)));
        }

        children.Add(new Expanded(child: new ScribeMultilineField(
            initialText: Widget.Data.Text,
            focusNode: Widget.FocusNode,
            fontSize: style.FontSize,
            padX: style.FieldPadX,
            padY: style.FieldPadY,
            autoFocus: Widget.AutoFocus,
            onChanged: text => Widget.OnTextChanged(index, text),
            onCommitAndAdvance: () => Widget.OnCommitAndAdvance(index),
            onCommitAndRetreat: () => Widget.OnCommitAndRetreat(index),
            onInsertTaskBelow: () => Widget.OnInsertTaskBelow(index))));

        // Row body: [grip][checkbox][text]. Delete/pin no longer reserve columns here — they float on
        // top of the row (see below), so the text can use the full width.
        Widget rowBody = new Padding(
            EdgeInsets.Symmetric(vertical: style.RowVerticalPadding, horizontal: style.RowHorizontalPadding),
            child: new Row(
                spacing: style.CheckboxTextGap,
                crossAxisAlignment: CrossAxisAlignment.Start,
                mainAxisSize: MainAxisSize.Max,
                children: children));

        // Drop-target highlight / resting pinned tint, drawn behind the row content. The Container is
        // ALWAYS present (fill may be transparent Vector4.Zero) — see the structural-stability note below
        // — so toggling the fill is a cheap property update, not a widget-type swap.
        Vector4 rowFill = Widget.IsDropTarget
            ? colors.StateSelected
            : (Widget.Data.IsTask && Widget.Data.Pinned ? style.PinnedTint : Vector4.Zero);

        rowBody = new Container(
            style: new BoxStyle { Color = rowFill },
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
            // Nudge the buttons down from their resting inset to center on a one-line row (see
            // ScribeRowControlNudge).
            float btnTop = gap + ScribeRowControlNudge.FloatingButtonTop;
            // delete: right-most; pin: to its left (task rows only).
            stackChildren.Add(new Positioned(
                right: gap, top: btnTop,
                child: new ScribeRowButton(
                    iconName: "scribeclose",
                    iconColor: colors.Error,
                    size: btn,
                    onTap: () => Widget.OnDelete(index))));
            if (Widget.Data.IsTask)
            {
                stackChildren.Add(new Positioned(
                    right: gap + btn + gap, top: btnTop,
                    child: new ScribeRowButton(
                        iconName: "scribepin",
                        // Pinned reads "active" (accent); unpinned is muted.
                        iconColor: Widget.Data.Pinned ? colors.Primary : colors.OnSurfaceVariant,
                        size: btn,
                        onTap: () => Widget.OnTogglePinned(index))));
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

/// <summary>Fixed, unscaled top-margin nudges that visually center a row's shorter controls on a
/// SINGLE-LINE text input, without moving them when the text wraps to multiple lines.
///
/// <para>The row lays its children out with <see cref="CrossAxisAlignment.Start"/> (top-aligned) so a
/// multi-line input keeps the controls pinned to its first line. That means the controls do NOT auto-
/// center — a one-line input is ~24px tall while the grip/checkbox glyphs are ~22px and the floating
/// pin/delete buttons ~22px, so each reads a hair high. We nudge each control DOWN by a fixed top
/// margin to sit centered on a one-line row. Because the nudge is smaller than the (input − control)
/// slack, it never grows the row height.</para>
///
/// <para><b>These are hand-tuned for the current font size (15) and control size (22).</b> They are
/// deliberately NOT scaled by <c>TextSizeScale</c>: centering is `(inputHeight − controlHeight) / 2`,
/// which is not linear in the scale, so a single multiplier wouldn't stay centered. If the font size
/// or control size ever becomes user-adjustable, replace these constants with a computed offset from
/// the measured input/control heights (2026-07-24 feedback).</para></summary>
internal static class ScribeRowControlNudge
{
    /// <summary>Down-nudge for the drag grip and the task checkbox (both ~22px on a ~24px input).</summary>
    public const float CheckboxAndGripTop = 2f;

    /// <summary>Additional down-nudge for the floating pin/delete buttons, on top of their resting
    /// <see cref="ScribeEditRowState"/> inset, to center them on a one-line row.</summary>
    public const float FloatingButtonTop = 3f;
}

// ============================================================================
// Shared per-row control primitives (add-lectern-row-affordances-libgui)
// ============================================================================

/// <summary>A bare (non-interactive) VS icon glyph rendered by registered <c>CustomIcons</c> code via
/// <see cref="VsIcon"/>. Used for the grip, whose pointer handling lives in a wrapping
/// <see cref="GestureDetector"/> rather than the glyph itself.
///
/// <para>NOTE: this deliberately uses <see cref="VsIcon"/> (icon-by-code) rather than LibGUI's
/// <see cref="Icon"/>/<see cref="IconButton"/> (SVG-by-path). <see cref="Icon"/> loads its SVG through
/// <c>SkiaAssetLoader.LoadSvg</c>, which calls <c>Assets.TryGet</c> WITHOUT <c>loadAsset: true</c> — and
/// VS nulls out every non-patched asset's <c>Data</c> after startup, so that path would fail to draw
/// our icons. <see cref="VsIcon"/> routes through <c>IconUtil.DrawIconInt</c> → the mod's self-healing
/// <c>CustomIcons</c> delegate (which re-resolves the asset on demand), which is why the icons were
/// registered that way (see <c>ScribeModSystem.RegisterSvgIcon</c> / VSAPI-NOTES.md).</para></summary>
internal sealed class ScribeVsIconGlyph : StatelessWidget
{
    private readonly string iconName;
    private readonly float size;
    private readonly Vector4 color;

    public ScribeVsIconGlyph(string iconName, float size, Vector4 color)
    {
        this.iconName = iconName;
        this.size = size;
        this.color = color;
    }

    public override Widget Build(BuildContext context) => new VsIcon(iconName, size, color);
}

/// <summary>A floating per-row action button (delete / pin) with real button chrome: a bordered,
/// solid-background square with theme-derived resting/hover/press fills, wrapping a <see cref="VsIcon"/>
/// glyph. Unlike the earlier bare hover-glyph, this reads as a proper button (2026-07-24 feedback) so it
/// floats legibly ON TOP of the row's text via the row's Stack. Uses <see cref="VsIcon"/> (icon-by-code)
/// for the glyph — NOT LibGUI's <see cref="IconButton"/>/<see cref="Icon"/>, whose SVG-by-path
/// <c>LoadSvg</c> fails on our post-startup-unloaded assets (see <see cref="ScribeVsIconGlyph"/>).</summary>
internal sealed class ScribeRowButton : StatefulWidget
{
    public ScribeRowButton(string iconName, Vector4 iconColor, float size, Action onTap, Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        IconName = iconName;
        IconColor = iconColor;
        Size = size;
        OnTap = onTap;
    }

    public string IconName { get; }
    public Vector4 IconColor { get; }
    /// <summary>Outer button box side length. The glyph is inset from this by the button's padding.</summary>
    public float Size { get; }
    public Action OnTap { get; }

    public override State CreateState() => new ScribeRowButtonState();
}

internal sealed class ScribeRowButtonState : State<ScribeRowButton>
{
    private bool hovered;
    private bool pressed;

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;

        // Solid (opaque) background from the theme's raised-surface tone, brightening resting -> hover ->
        // press so the button reads as interactive. SurfaceHigh is the raised-element tone; nudge it up
        // for hover/press. Kept opaque (W=1) so it fully covers the row text it floats over.
        Vector4 baseBg = colors.SurfaceHigh with { W = 1f };
        float lift = pressed ? -0.06f : hovered ? 0.10f : 0f;
        Vector4 bg = new(
            Math.Clamp(baseBg.X + lift, 0f, 1f),
            Math.Clamp(baseBg.Y + lift, 0f, 1f),
            Math.Clamp(baseBg.Z + lift, 0f, 1f),
            1f);

        float pad = MathF.Max(3f, Widget.Size * 0.18f); // small padding; glyph fills the rest
        float glyph = Widget.Size - pad * 2f;

        return new GestureDetector(
            onTap: _ => Widget.OnTap(),
            onEnter: _ => { if (!hovered) SetState(() => hovered = true); },
            onExit: _ => { if (hovered || pressed) SetState(() => { hovered = false; pressed = false; }); },
            onPress: _ => { if (!pressed) SetState(() => pressed = true); },
            onRelease: _ => { if (pressed) SetState(() => pressed = false); },
            child: new Container(
                style: new BoxStyle
                {
                    Color = bg,
                    Width = Widget.Size,
                    Height = Widget.Size,
                    CornerRadius = new Vector4(3f),
                    BorderThickness = 1f,
                    BorderColor = colors.Border,
                    Padding = EdgeInsets.All(pad),
                },
                child: new VsIcon(Widget.IconName, glyph, Widget.IconColor)));
    }
}
