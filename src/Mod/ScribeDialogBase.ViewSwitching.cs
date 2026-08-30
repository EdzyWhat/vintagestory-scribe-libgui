using System;
using System.Collections.Generic;
using System.Diagnostics;        // Conditional (DEBUG-only scroll trace)
using System.Linq;
using Gui;                       // GuiDialogBlockEntityBase, WindowConfig
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle, FontWeight
using Gui.Widgets.Basic;         // Text, WindowFrame, VsIcon, Container, Button
using Gui.Widgets.Events;        // PointerEvent, KeyboardEvent
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, Theme, ValueKey, Key
using Gui.Widgets.Input;         // Checkbox, FocusNode, GestureDetector, MouseRegion, Dropdown, DropdownItem, TextField, TextFieldStyle, TextEditingController, TextSelection, TextEditingValue
using Gui.Widgets.Gestures;      // ScrollController
using Gui.Widgets.Layout;        // Column, Row, Expanded, Padding, SizedBox, Center, Align, Alignment, CrossAxisAlignment, MainAxisAlignment
using Gui.Widgets.Overlay;       // Tooltip
using Gui.Widgets.Painting;      // BoxStyle
using Gui.Widgets.Scroll;        // ListView, SingleChildScrollView, Scrollable, Scrollbar
using Gui.Widgets.Spans;         // TextSpan
using Gui.Core.Layout;           // MainAxisSize
using OpenTK.Mathematics;        // Vector2
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;   // Lang, GlobalConstants
using Vintagestory.API.MathTools;  // BlockPos

namespace Scribe;

public abstract partial class ScribeDialogBase
{
    // ---------------- View switching ----------------

    /// <summary>Enter (or refresh) the editor view on the given authoritative document bytes. Called
    /// by <see cref="BlockEntityScribeLectern.HandleServerReply"/> when editor access is granted
    /// (the lock is now held). Seeds a fresh scratch copy, starts the autosave tick, and rebuilds
    /// into the editor tree (or lets <c>TryOpen</c> build it if the dialog isn't open yet).</summary>
    public void EnterEditorMode(byte[]? documentBytes)
    {
        if ((recoveringLostLock || optimisticEditorEntry) && scratch is not null)
        {
            // We already hold a live scratch that the authoritative document must NOT overwrite, and this grant
            // just confirms the lock is ours — so keep the scratch and re-flush it rather than reseeding. Two
            // callers land here:
            //   • the lost-lock recovery re-acquire after a save failure (task 8.6), which must preserve the
            //     player's unsaved edits;
            //   • a singleplayer-optimistic Handbook append (feedback 7.13 follow-up): we entered the editor
            //     locally and applied the append while the server was paused, and THIS is the delayed grant
            //     reply (carrying the pre-flush document) arriving on unpause — reseeding would drop the
            //     optimistic append, so keep our scratch and re-flush it to persist it authoritatively.
            // NOTE: the INITIAL optimistic entry reaches EnterEditorMode with scratch still null (not editing
            // yet), so it correctly falls through to the full seed path below and applies the pending append.
            recoveringLostLock = false;
            optimisticEditorEntry = false;
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
        // Enforce the "empty tasks are never persisted" invariant (add-empty-task-lifecycle D5) at the LOAD
        // boundary, not only at the leave boundary. The leave paths (EnterReadMode / switch / close) already
        // purge + flush, but the seed bytes we re-enter on can still contain an empty task: on the lectern a
        // re-entry round-trips to the server (EnterEditorMode(message.DocumentBytes)), so a purge-flush and
        // the re-access request can cross on the wire and the grant carries the PRE-purge doc. Reconcile made
        // this visible — the reappearing empties were the symptom behind reconcile-animating-surfaces §3.9 —
        // but the race is host/path-independent, so heal it here for every seed. PurgeEmptyRowsFromScratch
        // sets isDirty=true iff it removed anything, so a stale seed is re-flushed clean (self-healing) and a
        // clean seed stays isDirty=false. Runs BEFORE SyncFocusNodesToScratch so the node count matches the
        // trued-up block list, and touches only empty TASK blocks (empty text sections are valid, untouched).
        PurgeEmptyRowsFromScratch();
        isEditorMode = true;
        focusedEditIndex = null;
        autoFocusRowOnRebuild = null;
        saveFailureRetries = 0;
        recoveringLostLock = false;
        SyncFocusNodesToScratch();
        StartAutosaveTick();

        // add-tracker-link-tasks 3.4 (Case B): a Handbook "Add to Scribe" click that arrived while this
        // dialog was NOT editing stashed its append and requested editor access; now that access has landed
        // (synchronous for items, or the async server grant for blocks), apply the deferred append and flush.
        // The recovery branch above returns early, so this fires only on a genuine editor entry — never on a
        // lost-lock re-acquire (which must preserve the player's own in-flight scratch untouched).
        //
        // Apply the append BEFORE the ForceRebuild below, so the fresh editor tree is built from the
        // already-mutated scratch and the new Tracker/Link row is visible immediately — landing the player in
        // a live editor view where they can set the count. Applying it AFTER the rebuild (as this once did)
        // left the row out of the just-built tree, and ApplyHandbookAppend's in-place RebuildBody can't recover
        // it: the body's GlobalKey state isn't resolvable in the same synchronous call right after Mount, so
        // RebuildBody no-ops and the row only appeared on the NEXT full rebuild — i.e. a manual view swap. That
        // was the Lectern "task created but invisible until a view swap" bug (feedback 7.13).
        FlushPendingHandbookAppend();

        if (IsOpened())
        {
            ForceRebuild();
        }
    }

    /// <summary>Enter (or stay in) the read view. Called on a read-access grant and from the Read nav
    /// button. Tears down the editor if it was active; also lands on Read from the Pin Tab (which holds no
    /// lock/scratch, so nothing to tear down — just select the view).</summary>
    public void EnterReadMode()
    {
        CommitTitleIfEditing();
        if (isEditorMode)
        {
            // Full editor teardown, matching OnClickSwitchToPinned/History and the editor footer's
            // OnClickSwitchToRead: flush the last edit AND release the server-held lock before leaving.
            // The Read nav button routes here directly (onTap: EnterReadMode), so without the release a
            // switch to Read via the nav button leaked the lock while every other tab freed it — the
            // exact asymmetry reported (fix-transient-lectern-editor-lock). LeaveEditorMode itself does
            // NOT release, by contract.
            if (focusedEditIndex is { } idx) NormalizeRowOnCommit(idx);
            PurgeEmptyRowsFromScratch();
            pendingEmptyRowRemoval = null;
            FlushIfDirty();
            SendReleaseLockPacket();
            LeaveEditorMode();
        }
        viewMode = ScribeLecternView.Read; // also covers a Pin Tab → Read switch (no editor teardown)
        if (IsOpened())
        {
            ForceRebuild();
        }
    }

    /// <summary>Switch to the Pin Tab view (scribe-pin-editor). Wired to the <c>scribepin</c> nav button —
    /// a real entry method, not an inline flag flip (the nav discipline the editor's
    /// <see cref="RequestEditorAccess"/> / <see cref="EnterReadMode"/> follow). If the editor view is
    /// active, tear it down first (flush + release the lock) exactly like <see cref="OnClickSwitchToRead"/>,
    /// then select the Pinned view. The Pin Tab reads the server-synced <c>MyPins</c> — no lock, no scratch
    /// doc — so this needs no server round-trip.</summary>
    private void OnClickSwitchToPinned()
    {
        CommitTitleIfEditing();
        if (isEditorMode)
        {
            if (focusedEditIndex is { } idx) NormalizeRowOnCommit(idx);
            PurgeEmptyRowsFromScratch();
            pendingEmptyRowRemoval = null;
            FlushIfDirty();
            SendReleaseLockPacket();
            LeaveEditorMode();
        }
        viewMode = ScribeLecternView.Pinned;
        focusedPinTaskId = null;
        autoFocusPinTaskId = null;
        pendingFocusPinTaskId = null;
        SyncPinFocusNodes();
        if (IsOpened()) ForceRebuild();
    }

    /// <summary>Switches to the Guestbook (Visitors) view, tearing down the editor first if active.
    /// Called from the Guestbook nav button in subclasses that expose the tab.</summary>
    protected void OnClickSwitchToVisitors()
    {
        CommitTitleIfEditing();
        if (isEditorMode)
        {
            if (focusedEditIndex is { } idx) NormalizeRowOnCommit(idx);
            PurgeEmptyRowsFromScratch();
            pendingEmptyRowRemoval = null;
            FlushIfDirty();
            SendReleaseLockPacket();
            LeaveEditorMode();
        }
        viewMode = ScribeLecternView.Visitors;
        if (IsOpened()) ForceRebuild();
    }

    /// <summary>If the Guestbook tab is currently active, rebuilds it to reflect a just-received
    /// guestbook sync from the server.</summary>
    protected internal void RefreshGuestbookView()
    {
        if (viewMode == ScribeLecternView.Visitors && IsOpened()) ForceRebuild();
    }

    /// <summary>Switches to the History tab, tearing down the editor first if active.
    /// Called from the History nav button in subclasses that expose the tab.</summary>
    protected void OnClickSwitchToHistory()
    {
        CommitTitleIfEditing();
        if (isEditorMode)
        {
            if (focusedEditIndex is { } idx) NormalizeRowOnCommit(idx);
            PurgeEmptyRowsFromScratch();
            pendingEmptyRowRemoval = null;
            FlushIfDirty();
            SendReleaseLockPacket();
            LeaveEditorMode();
        }
        viewMode = ScribeLecternView.History;
        if (IsOpened()) ForceRebuild();
    }

    /// <summary>Rebuilds the History view if it is currently active. Called after a history sync.</summary>
    protected internal void RefreshHistoryView()
    {
        if (viewMode == ScribeLecternView.History && IsOpened()) ForceRebuild();
    }

    /// <summary>Builds the History tab content. Subclasses that expose a History tab override this
    /// to provide a real implementation. The base returns an empty placeholder.</summary>
    protected virtual Widget BuildHistoryContent()
    {
        var colors = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        float bodySize = ScribeRowConstants.BaseWindowFontSize
            * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.WindowFontScale);
        // Family inherited from the wrap; only the size/color are set. (Base placeholder shown by hosts
        // that don't override History; wrapped for parity with the Notebook's real History tab.)
        var bodyStyle = new TextStyle { FontSize = bodySize, Color = colors.OnSurface };
        return ScribeTextDefaults.Wrap(modSystem.MySettings.TaskFontFamily, bodySize,
            new Center(child: new Text(Lang.Get("scribe:scribe-gui-history-empty"), bodyStyle)));
    }

    /// <summary>Switches to the Timer tab, tearing down the editor first if active.
    /// Called from the Timer nav button in subclasses that expose the tab.</summary>
    protected void OnClickSwitchToTimer()
    {
        CommitTitleIfEditing();
        if (isEditorMode)
        {
            if (focusedEditIndex is { } idx) NormalizeRowOnCommit(idx);
            PurgeEmptyRowsFromScratch();
            pendingEmptyRowRemoval = null;
            FlushIfDirty();
            SendReleaseLockPacket();
            LeaveEditorMode();
        }
        viewMode = ScribeLecternView.Timer;
        if (IsOpened()) ForceRebuild();
    }

    /// <summary>Rebuilds the Timer view if it is currently active. Called after a timer state push.</summary>
    protected internal void RefreshTimerView()
    {
        if (viewMode == ScribeLecternView.Timer && IsOpened()) ForceRebuild();
    }

    /// <summary>Builds the Timer tab content. Subclasses that expose a Timer tab override this.</summary>
    protected virtual Widget BuildTimerContent()
    {
        var colors = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        float bodySize = ScribeRowConstants.BaseWindowFontSize
            * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.WindowFontScale);
        // Family inherited from the wrap; only the size/color are set. (Base placeholder shown by hosts
        // that don't override Timer; wrapped for parity with the Clockmaker Notebook's real Timer tab.)
        var bodyStyle = new TextStyle { FontSize = bodySize, Color = colors.OnSurface };
        return ScribeTextDefaults.Wrap(modSystem.MySettings.TaskFontFamily, bodySize,
            new Center(child: new Text(Lang.Get("scribe:scribe-gui-timer-empty"), bodyStyle)));
    }

    /// <summary>Switches to the Inventory tab (the Scriptorium's Scribe-items-only storage —
    /// add-scriptorium-inventory), tearing down the editor first if active. Called from the Inventory nav
    /// button in the Scriptorium subclass that exposes the tab. Mirrors the Guestbook/History/Timer
    /// teardown template exactly.</summary>
    protected void OnClickSwitchToInventory()
    {
        CommitTitleIfEditing();
        if (isEditorMode)
        {
            if (focusedEditIndex is { } idx) NormalizeRowOnCommit(idx);
            PurgeEmptyRowsFromScratch();
            pendingEmptyRowRemoval = null;
            FlushIfDirty();
            SendReleaseLockPacket();
            LeaveEditorMode();
        }
        viewMode = ScribeLecternView.Inventory;
        if (IsOpened()) ForceRebuild();
    }

    /// <summary>Rebuilds the Inventory view if it is currently active. Called after an inventory resync
    /// (a slot changed server-side) so a second client viewing the same block repaints its slots.</summary>
    protected internal void RefreshInventoryView()
    {
        if (viewMode == ScribeLecternView.Inventory && IsOpened()) ForceRebuild();
    }

    protected void OnClickSwitchToInbox()
    {
        CommitTitleIfEditing();
        if (isEditorMode)
        {
            if (focusedEditIndex is { } idx) NormalizeRowOnCommit(idx);
            PurgeEmptyRowsFromScratch();
            pendingEmptyRowRemoval = null;
            FlushIfDirty();
            SendReleaseLockPacket();
            LeaveEditorMode();
        }
        viewMode = ScribeLecternView.Inbox;
        if (IsOpened()) ForceRebuild();
    }

    protected virtual Widget BuildInboxContent()
    {
        var colors = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        float bodySize = ScribeRowConstants.BaseWindowFontSize
            * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.WindowFontScale);
        return ScribeTextDefaults.Wrap(modSystem.MySettings.TaskFontFamily, bodySize,
            new Center(child: new Text(Lang.Get("scribe:scribe-gui-inbox-empty"),
                new TextStyle { FontSize = bodySize, Color = colors.OnSurface })));
    }

    /// <summary>Builds the Inventory tab content. Only the Scriptorium exposes this tab, so the base
    /// returns an empty placeholder; <see cref="GuiDialogScribeScriptorium"/> overrides it to place the
    /// Scribe-item slots. Mirrors <see cref="BuildHistoryContent"/> / <see cref="BuildTimerContent"/>.</summary>
    protected virtual Widget BuildInventoryContent()
    {
        var colors = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        float bodySize = ScribeRowConstants.BaseWindowFontSize
            * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.WindowFontScale);
        var bodyStyle = new TextStyle { FontSize = bodySize, Color = colors.OnSurface };
        return ScribeTextDefaults.Wrap(modSystem.MySettings.TaskFontFamily, bodySize,
            new Center(child: new Text(Lang.Get("scribe:scribe-gui-inventory-empty"), bodyStyle)));
    }

    /// <summary>"Done editing" button: flush the pending edit, release the lock, and swap to the read
    /// view — all locally (read is lock-free and reads the block entity's now-optimistically-updated
    /// document). Flush BEFORE releasing the lock: the server processes packets in send order, so
    /// releasing first would let the flushed edit arrive lock-less and be rejected by
    /// <see cref="BlockEntityScribeLectern.ApplyEdit"/>'s lock check.</summary>
    private void OnClickSwitchToRead()
    {
        if (focusedEditIndex is { } idx) NormalizeRowOnCommit(idx);
        // Drop any abandoned empty task before flushing so the read view / persisted doc never shows one
        // (add-empty-task-lifecycle D5) — the focused row may be an untyped new task the blur hasn't swept.
        PurgeEmptyRowsFromScratch();
        pendingEmptyRowRemoval = null; // superseded by the purge; don't act on it after the editor tears down
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
        TraceScroll("capture-restore");
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
        optimisticEditorEntry = false;
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

    /// <summary>The user-facing "switch to editor" entry point for both affordances (read-view footer
    /// button and the right-column Edit nav button) — fix-multiplayer-editor-lock §4. If the synced lock
    /// state shows another player already holds the editor lock, this does NOT round-trip to the server:
    /// it surfaces the native in-game error and stays put (the affordance is also rendered inert in that
    /// state — see BuildRightColNav / the read-view footer). Otherwise it requests access normally; the
    /// server refusal remains the authoritative backstop for the render→click race. Distinct from
    /// <see cref="RequestEditorAccess"/>, which is the raw request also used by the lost-lock recovery
    /// re-acquire (<see cref="HandleSaveFailed"/>) and must NOT be gated.</summary>
    private void TryEnterEditor()
    {
        if (host.IsLockedByOther(capi.World.Player.PlayerUID))
        {
            capi.TriggerIngameError(this, "scribe-lectern-locked", Lang.Get("scribe:scribe-gui-locked"));
            return;
        }

        RequestEditorAccess();
    }

    /// <summary>"Switch to editor" button in the read view: request editor access from the server
    /// (design D2 flow, unchanged). A granted reply round-trips back to
    /// <see cref="BlockEntityScribeLectern.HandleServerReply"/>, which calls
    /// <see cref="EnterEditorMode"/>. Callers that gate on the synced lock go through
    /// <see cref="TryEnterEditor"/>; this raw form is also the lost-lock recovery re-acquire.
    /// Subclasses that don't require a server round-trip (e.g. Notebook) override this to call
    /// <see cref="EnterEditorMode"/> directly.</summary>
    protected virtual void RequestEditorAccess()
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeRequestAccessMessage
        {
            DocIdBytes = host.Document.DocId.ToByteArray(),
            WantEditor = true,
        });
    }

    /// <summary>
    /// Called by <see cref="BlockEntityScribeLectern.FromTreeAttributes"/> whenever the authoritative
    /// document changes (e.g. another viewer toggled a task, or a HUD-Delete removed one). In read mode,
    /// rebuilds from the current document. In editor mode, the scratch is the source of truth for content
    /// being edited — a full resync must NOT overwrite in-progress text. However, three narrower external
    /// effects ARE propagated into scratch, because they are orthogonal to the text being edited:
    /// <list type="bullet">
    /// <item>a task the fresh authoritative document no longer contains (e.g. this player completed it via
    /// the HUD under a destructive policy) is dropped from the editor, without disturbing other rows' edits
    /// (add-pinned-task-hud follow-up — <c>80777b7b</c>);</item>
    /// <item>a task whose authoritative <see cref="ScribeBlock.Done"/> flipped externally (e.g. a HUD
    /// completion under Keep/Sink, where the task stays in the document) has its scratch done-state synced,
    /// so the open editor's checkbox reflects it live;</item>
    /// <item>a just-completed task the server sank to the bottom (Sink/UnpinSink) is moved to the bottom of
    /// the editor too, matching the Read/Pinned views.</item>
    /// </list>
    /// Syncing done-state + order (never text) also makes scratch consistent with the live document, so the
    /// next autosave <c>ApplyEdit</c> flush no longer reverts the external completion — closing the
    /// last-write-wins data-loss window for the completion case (sync-editor-view-on-external-completion).
    /// </summary>
    public void RefreshReadView()
    {
        if (!isEditorMode)
        {
            if (!IsOpened()) return;
            // Read view (reconcile-animating-surfaces §5): the read list is now non-virtualized and keyed by
            // ValueKey<Guid>(TaskId), so an external document change (another client toggled a task, or the
            // Delete completion policy removed one) reconciles in place — surviving rows are REUSED, the
            // deleted row's element unmounts, and the shared scroll offset is preserved inherently, so no
            // capture-restore is needed. A completion under a stationary cursor can slide a different row up;
            // arm the hover latch so the hover-gated pin control re-homes without a mouse wiggle
            // (fix-list-collapse-stale-hover), harmless when nothing moved.
            if (viewMode == ScribeLecternView.Read)
            {
                hoverRefreshLatch.Arm();
                RebuildBody();
                return;
            }
            // Other non-editor views (Pinned/History/Timer/Visitors) still ForceRebuild here. Pinned keeps its
            // offset via reconcile through OnMyPinsChanged; the rest have no scroll state worth capturing, so
            // capture-restore is gated to the (now Pinned-excluded) remaining cases as before.
            if (viewMode != ScribeLecternView.Pinned) CaptureScrollForRestore();
            ForceRebuild();
            return;
        }

        // Editor mode: don't resync in-progress TEXT, but reconcile the orthogonal external effects below.
        if (scratch is null || !IsOpened()) return;
        var serverTasks = host.Document.Blocks.Where(b => b.IsCompletable).ToList();
        var serverTaskIds = serverTasks.Select(b => b.TaskId).ToHashSet();

        // (1) Drop tasks the server no longer knows about.
        bool structural = false;
        for (int i = scratch.Blocks.Count - 1; i >= 0; i--)
        {
            var b = scratch.Blocks[i];
            if (!b.IsCompletable || serverTaskIds.Contains(b.TaskId)) continue;
            // A task in scratch but absent from the server is EITHER a real task the server dropped
            // (completed via the HUD / Delete policy elsewhere) — which should disappear here too — OR a
            // task this editor JUST created that hasn't reached the server yet: EditorInsertTaskBelow
            // flushes the pre-insert doc, and autosave is throttled (~1s) and skips an empty focused row,
            // so a brand-new row lives locally-only for a beat. A resync landing in that window must NOT
            // yank the new row out from under the player — that was the "Enter makes a task that self-
            // destructs a few frames later" race (trace signature: insert-below N → delete N+1 with no
            // sweep guard tripping, because the delete comes from HERE, not the empty-row sweep). Tell the
            // two apart: never drop the row currently being edited, and never drop an empty task (empty
            // tasks are never persisted by design — see PurgeEmptyRowsFromScratch — so their absence from
            // the server is always expected, never a server-side deletion). The empty-text protection is
            // scoped to real Tasks: a Tracker/Link carries empty Text by design (its label comes from the
            // referenced item, not typed text) yet IS persisted, so a server-side deletion of one is real
            // and must drop here — otherwise the next flush would resurrect it from stale scratch.
            if (focusedEditIndex == i || (b.IsTask && string.IsNullOrWhiteSpace(b.Text))) continue;
            DeleteEditorBlock(i);
            structural = true;
        }

        // (2) Sync external completion (done-state only, NOT text) into surviving scratch tasks, and note a
        // just-completed task the server sank to the bottom so we can mirror the move. Uses the server's
        // authoritative done-state directly, so the merge follows any external completion path (HUD, another
        // reader) without inferring which policy produced it. ToggleTask flips to match because it is called
        // ONLY on a mismatch (post-toggle the block's Done equals the server's).
        var serverDone = serverTasks.ToDictionary(b => b.TaskId, b => b.Done);
        Guid? serverLastTaskId = serverTasks.Count > 0 ? serverTasks[^1].TaskId : null;
        bool doneChanged = false;
        int sinkFrom = -1;
        for (int i = 0; i < scratch.Blocks.Count; i++)
        {
            var b = scratch.Blocks[i];
            if (!b.IsCompletable || !serverDone.TryGetValue(b.TaskId, out bool serverBlockDone)) continue;
            if (b.Done == serverBlockDone) continue;
            scratch.ToggleTask(i);
            isDirty = true; // carry the synced done-state into the next flush so it isn't reverted (D4)
            doneChanged = true;
            // A task that just completed AND is now last server-side was sunk by a Sink/UnpinSink policy;
            // mirror the move to the bottom of scratch. Only the just-completed row is moved, so a local
            // drag-reorder of a different (not-just-completed) row is never disturbed by this merge.
            if (serverBlockDone && serverLastTaskId == b.TaskId && i != scratch.Blocks.Count - 1)
                sinkFrom = i;
        }

        // (3) Mirror the sink move via the reconcile path, preserving the edited row's focus/caret rather
        // than grabbing focus onto the sunk row (external, not player-initiated). It owns its own rebuild.
        if (sinkFrom >= 0)
        {
            ReorderEditorBlock(sinkFrom, scratch.Blocks.Count - 1, anchorViewport: true, preserveFocusedRow: true);
            return;
        }

        // A deletion already scheduled its own rebuild (collapse/cleanup path); a done-only change still
        // needs one to repaint the reused row's checkbox. If neither happened, nothing to do.
        if (structural) return;
        if (doneChanged) RebuildBody();
    }
}
