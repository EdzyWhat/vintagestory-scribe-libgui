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
        LeaveEditorIfActive();
        viewMode = ScribeLecternView.Read; // also covers a Pin Tab → Read switch (no editor teardown)
        if (IsOpened())
        {
            ForceRebuild();
        }
    }

    /// <summary>Shared teardown half of <see cref="EnterReadMode"/>: flushes and releases the editor
    /// lock if one is held, without touching <c>viewMode</c>. Full editor teardown, matching
    /// OnClickSwitchToPinned/History and the editor footer's OnClickSwitchToRead: flush the last edit
    /// AND release the server-held lock before leaving (LeaveEditorMode itself does NOT release, by
    /// contract — the missing release here was the exact asymmetry reported in
    /// fix-transient-lectern-editor-lock). Exposed so <see cref="EnterGrantedView"/> overrides that have
    /// no Read view to land on (Assignment Desk, Inbox) can tear down an editor session without being
    /// forced onto Read.</summary>
    protected void LeaveEditorIfActive()
    {
        CommitTitleIfEditing();
        if (!isEditorMode) return;
        if (focusedEditIndex is { } idx) NormalizeRowOnCommit(idx);
        PurgeEmptyRowsFromScratch();
        pendingEmptyRowRemoval = null;
        FlushIfDirty();
        SendReleaseLockPacket();
        LeaveEditorMode();
    }

    /// <summary>Called by <see cref="BlockEntityScribeWritingStation.HandleServerReply"/> for every
    /// non-editor access grant/refusal — the ordinary reply a plain right-click gets. The base lands this
    /// on the Read view, matching every Read/Editor/Pinned surface (Lectern, Notebook, Scriptorium,
    /// Chalkboard). The Assignment Desk and Inbox have no Read view in their nav model at all (design.md
    /// Decision 1 / <c>inbox-block</c>'s spec) — routing them through <see cref="EnterReadMode"/>
    /// force-switched them to a tab that doesn't exist for them every time the block was opened, which is
    /// what a plain right-click always triggers. They override this to tear down an editor session if one
    /// was (rarely, incidentally) active without reasserting any particular tab, leaving whatever view is
    /// already selected — the constructor's <see cref="DefaultToAssignmentView"/>/
    /// <see cref="DefaultToInboxView"/> on first open, or the player's own last nav-button pick on a
    /// re-open of an already-open dialog.</summary>
    public virtual void EnterGrantedView() => EnterReadMode();

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

    /// <summary>Rebuilds the Inbox/Assignment view if either is currently active, in place (reconcile,
    /// not <see cref="GuiBase.ForceRebuild"/>) so each row's expanded state and the filter-chip selection
    /// (both dialog-owned — <see cref="expandedAssignmentIds"/>/<see cref="assignmentFilterGroup"/> — since
    /// manage-terminal-assignment-records lifted them out of <see cref="ScribeInboxContent"/>'s own State)
    /// survive the refresh — mirroring how a pin/settings change reconciles the Read/Editor/Pinned views
    /// rather than remounting them. Called on every <see cref="ScribeModSystem.MyAssignmentsChanged"/> push
    /// (§7.5).</summary>
    private void OnMyAssignmentsChanged()
    {
        if (!IsOpened()) return;
        // SentHistory added here (triage 2026-08-31: "we aren't updating Completed or Discarded properly")
        // — its own tab was split out by 13.2 after this check was written and was never added to it, so a
        // sync arriving while that tab was the open view updated modSystem.MySentAssignments correctly but
        // never told this dialog to repaint; the stale chip only refreshed on the next unrelated rebuild
        // (e.g. navigating away and back).
        if (viewMode is ScribeLecternView.Inbox or ScribeLecternView.Assignment or ScribeLecternView.SentHistory)
            RebuildBody();
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

    /// <summary>Switches to the Inbox Inventory tab (the standalone Inbox block's own mixed
    /// restricted/open storage — add-inbox-inventory-tab), tearing down the editor first if active.
    /// Scoped in effect to <see cref="GuiDialogScribeInbox"/> — no other surface has this tab — but
    /// defined here alongside its peers (<see cref="OnClickSwitchToInventory"/>,
    /// <see cref="OnClickSwitchToAssignment"/>) since only <c>private</c>/<c>private protected</c>
    /// dialog-base state (the editor teardown sequence, <see cref="viewMode"/>) is reachable from this
    /// partial class.</summary>
    protected void OnClickSwitchToInboxInventory()
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
        viewMode = ScribeLecternView.InboxInventory;
        if (IsOpened()) ForceRebuild();
    }

    /// <summary>Rebuilds the Inbox Inventory view if it is currently active. Called after an inventory
    /// resync (a slot changed server-side) so a second client viewing the same Inbox block repaints its
    /// slots — the peer of <see cref="RefreshInventoryView"/> for this tab.</summary>
    protected internal void RefreshInboxInventoryView()
    {
        if (viewMode == ScribeLecternView.InboxInventory && IsOpened()) ForceRebuild();
    }

    /// <summary>Switches to the Assignment tab, tearing down the editor first if active. Called from the
    /// Assignment Desk's own Assignment nav button — no other surface ever calls this (design.md
    /// Decision 1: "Only the Assignment Desk dialog ever sets viewMode to Assignment").</summary>
    protected void OnClickSwitchToAssignment()
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
        viewMode = ScribeLecternView.Assignment;
        if (IsOpened()) ForceRebuild();
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
        MarkInboxSeenIfNeeded();
        if (IsOpened()) ForceRebuild();
    }

    /// <summary>Switches to the Sent Assignment History tab (refine-assignment-desk-inbox-ux 12.2) —
    /// mirrors <see cref="OnClickSwitchToAssignment"/>'s editor-teardown; only the Assignment Desk's own
    /// nav button ever calls this.</summary>
    protected void OnClickSwitchToSentHistory()
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
        viewMode = ScribeLecternView.SentHistory;
        if (IsOpened()) ForceRebuild();
    }

    /// <summary>Sends the mark-seen request (design.md Decision 4: "Opening the Inbox flips [Seen]
    /// server-side"). The request is unconditional — the server no-ops/skips the re-push when nothing
    /// was actually unseen — so it is safe to call from every path that makes the Inbox view active,
    /// not only <see cref="OnClickSwitchToInbox"/>'s nav-button click (refine-assignment-desk-inbox-ux
    /// D2): the standalone Inbox block's dialog also calls this from its constructor and its
    /// <c>EnterGrantedView()</c> override, since it lands on (and stays on) the Inbox view without ever
    /// going through <see cref="OnClickSwitchToInbox"/>.</summary>
    protected void MarkInboxSeenIfNeeded() =>
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeMarkAssignmentsSeenMessage());

    /// <summary>Builds the shared Inbox tab (add-assignment-and-quest-support §7): the viewing player's
    /// RECEIVED assignments (<see cref="ScribeModSystem.MyReceivedAssignments"/>), rendered by
    /// <see cref="ScribeInboxContent"/> — one implementation reused by every Inbox-capable surface
    /// (§7.5). Sent-history rendering is the Assignment Desk's separate Sent Assignment History tab
    /// (<see cref="BuildSentAssignmentHistoryContent"/>, design.md Decision 3, split out by
    /// refine-assignment-desk-inbox-ux 12.2); this is Received-only, viewed as the Assignee.</summary>
    protected virtual Widget BuildInboxContent()
    {
        var received = modSystem.MyReceivedAssignments.Where(b => b.Assignment is not null);
        var rows = NewestBatchFirst(received)
            .Select(b => new ScribeInboxRowData(
                TaskId: b.TaskId, Text: b.Text, Depth: b.Depth,
                State: b.Assignment!.State, AssignerUid: b.Assignment.AssignerUid,
                TargetPlayerUid: b.Assignment.TargetPlayerUid, AssignedDate: b.Assignment.AssignedDate,
                Seen: b.Assignment.Seen, ViewerRole: ScribeAssignmentActor.Assignee,
                DisplayName: ResolveRowItem(b).Name, AcceptedDate: b.Assignment.AcceptedDate,
                DeclinedDate: b.Assignment.DeclinedDate, CancelledDate: b.Assignment.CancelledDate,
                DiscardedDate: b.Assignment.DiscardedDate, CompletedDate: b.Assignment.CompletedDate,
                AcceptedIntoLabel: b.Assignment.AcceptedIntoLabel, ReceivedDate: b.Assignment.ReceivedDate))
            .ToList();

        return new ScribeInboxContent(
            rows: rows,
            resolvePlayerName: ResolvePlayerNameForInbox,
            onAction: SendAssignmentAction,
            onAccept: AcceptAssignment,
            acceptCandidates: ComputeAcceptCandidates(),
            onDelete: DeleteAssignmentRecord,
            activeFilterGroup: assignmentFilterGroup,
            onFilterGroupChanged: SetAssignmentFilterGroup,
            isExpanded: expandedAssignmentIds.Contains,
            onToggleExpand: ToggleAssignmentRowExpanded,
            style: RowStyle,
            scrollController: sharedScrollController);
    }

    /// <summary>Sets the shared Inbox/Sent-History filter-chip selection (lifted to the dialog — see
    /// <see cref="assignmentFilterGroup"/>'s remarks) and reconciles the body in place so both the content
    /// and the title-bar toggle (which reads the new filter to recompute "all visible rows expanded")
    /// repaint together.</summary>
    private void SetAssignmentFilterGroup(ScribeAssignmentFilterGroup group)
    {
        if (assignmentFilterGroup == group) return;
        assignmentFilterGroup = group;
        RebuildBody();
    }

    /// <summary>Toggles one assignment row's expanded state in the shared set (lifted to the dialog — see
    /// <see cref="expandedAssignmentIds"/>'s remarks) and reconciles the body in place so the title-bar
    /// toggle's icon/tooltip stays in sync with the individual chevron.</summary>
    private void ToggleAssignmentRowExpanded(Guid taskId)
    {
        if (!expandedAssignmentIds.Remove(taskId)) expandedAssignmentIds.Add(taskId);
        RebuildBody();
    }

    /// <summary>Every assignment TaskId currently visible in whichever of the Inbox/Sent History views is
    /// active, after the shared filter-chip selection — the "visible rows" the title-bar expand/collapse-all
    /// toggle operates on (manage-terminal-assignment-records). Empty (and thus inert) for any other view.</summary>
    private List<Guid> CurrentlyVisibleAssignmentRowIds()
    {
        IEnumerable<ScribeBlock> source = viewMode switch
        {
            ScribeLecternView.Inbox => modSystem.MyReceivedAssignments,
            ScribeLecternView.SentHistory => modSystem.MySentAssignments,
            _ => Enumerable.Empty<ScribeBlock>(),
        };
        var visibleStates = ScribeAssignmentFilterGroups.StatesFor(assignmentFilterGroup);
        return source
            .Where(b => b.Assignment is not null && visibleStates.Contains(b.Assignment!.State))
            .Select(b => b.TaskId)
            .ToList();
    }

    /// <summary>Whether every currently-visible assignment row (see <see cref="CurrentlyVisibleAssignmentRowIds"/>)
    /// is expanded right now — drives the title-bar toggle's icon direction/tooltip. False (not true) when
    /// there are no visible rows at all, so an empty list never claims to be "all expanded."</summary>
    private bool AllVisibleAssignmentRowsExpanded()
    {
        var ids = CurrentlyVisibleAssignmentRowIds();
        return ids.Count > 0 && ids.All(expandedAssignmentIds.Contains);
    }

    /// <summary>The title-bar expand/collapse-all toggle's tap handler (manage-terminal-assignment-records):
    /// expands every currently-visible row if any is collapsed, else collapses all of them. A no-op when
    /// nothing is currently visible (filtered out, or an empty list).</summary>
    private void ToggleAllVisibleAssignmentRows()
    {
        var ids = CurrentlyVisibleAssignmentRowIds();
        if (ids.Count == 0) return;
        bool allExpanded = ids.All(expandedAssignmentIds.Contains);
        foreach (var id in ids)
        {
            if (allExpanded) expandedAssignmentIds.Remove(id);
            else expandedAssignmentIds.Add(id);
        }
        RebuildBody();
    }

    /// <summary>Requests deletion of the CURRENT viewer's own side of a terminal-state assignment
    /// record (manage-terminal-assignment-records / split-assignment-delete-by-viewer). The delete
    /// control only ever renders while <see cref="viewMode"/> is <see cref="ScribeLecternView.Inbox"/> or
    /// <see cref="ScribeLecternView.SentHistory"/>, so which side to claim is unambiguous from that alone
    /// — Inbox is always the Assignee's view, Sent History always the Assigner's. The server
    /// re-validates the terminal-state restriction AND that the sender actually holds that side through
    /// <see cref="Scribe.Core.ScribeAssignmentStore.TryDelete"/> — this is a request, not a
    /// locally-applied change; the row disappears once the resynced
    /// <see cref="ScribeModSystem.MyAssignmentsChanged"/> arrives.</summary>
    private void DeleteAssignmentRecord(Guid taskId)
    {
        var side = viewMode == ScribeLecternView.Inbox
            ? ScribeAssignmentActor.Assignee
            : ScribeAssignmentActor.Assigner;
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeDeleteAssignmentMessage
        {
            AssignmentId = taskId.ToByteArray(),
            Side = (byte)side,
        });
    }

    /// <summary>Newest-batch-first ordering shared by the Inbox and Sent Assignment History tabs
    /// (refine-assignment-desk-inbox-ux 12.2 / triage 2026-08-31 general notes): groups by
    /// <see cref="ScribeAssignment.BatchId"/> (every row one send call created shares one id — see its
    /// remarks), preserving each group's own internal creation order (parent-before-subtask), then
    /// reverses the GROUP order so the most-recently-sent batch surfaces at the top intact. <c>GroupBy</c>
    /// preserves both properties by contract (group-of-first-occurrence order, and source order within
    /// each group), so this needs no manual contiguous-run bookkeeping — and works even if two batches'
    /// records aren't contiguous in the source (they always are here, but nothing relies on it).</summary>
    private static IEnumerable<ScribeBlock> NewestBatchFirst(IEnumerable<ScribeBlock> blocks) =>
        blocks.GroupBy(b => b.Assignment!.BatchId).Reverse().SelectMany(g => g);

    /// <summary>UID→display-name lookup for the Inbox tab's "Assigned by" line — see
    /// <see cref="ScribeInboxContent.ResolvePlayerName"/>'s remarks on why this has no dedicated cache.</summary>
    private protected string ResolvePlayerNameForInbox(string uid) => capi.World.PlayerByUid(uid)?.PlayerName ?? uid;

    /// <summary>Resolves the (assigner name, assigned date, accepted date) triple for the assignment
    /// marker's hover tooltip on Read/Editor rows — null for a task that isn't an accepted assignment.
    /// The Pin Tab has its own equivalent (<see cref="ScribePinnedRef"/>'s snapshotted fields; it has no
    /// live <see cref="ScribeAssignment"/> to read from — assignment-icon-and-tab-defaults).</summary>
    private protected (string? name, string? assignedDate, string? acceptedDate) ResolveAssignmentTooltipInfo(
        ScribeAssignment? assignment)
        => assignment is { State: ScribeAssignmentState.Accepted }
            ? (ResolvePlayerNameForInbox(assignment.AssignerUid), assignment.AssignedDate, assignment.AcceptedDate)
            : (null, null, null);

    /// <summary>Sends a Decline/Cancel/Discard request for an assignment (§4.1's
    /// <see cref="ScribeAssignmentActionMessage"/>). Accept goes through <see cref="AcceptAssignment"/>
    /// instead — it needs a placement target. Purely a request — the server re-validates and the row
    /// updates once its resync (<see cref="ScribeModSystem.MyAssignmentsChanged"/>) arrives.</summary>
    private protected void SendAssignmentAction(Guid taskId, ScribeAssignmentAction action)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeAssignmentActionMessage
        {
            AssignmentId = taskId.ToByteArray(),
            Action = (byte)action,
        });
    }

    /// <summary>Sends an Accept request naming the resolved placement target (assignment-state-machine's
    /// placement requirement) — the currently-held document, or the single/chosen inventory candidate the
    /// row's Accept control resolved via <see cref="ComputeAcceptCandidates"/>. The server re-validates and
    /// re-resolves the slot itself; this is a request, not a locally-applied placement.</summary>
    private void AcceptAssignment(Guid taskId, ScribeAcceptCandidate target)
    {
        // Diagnostic for the accept-destination-remembers-a-dropped-item investigation (2026-09-01):
        // records what the CLIENT resolved (label, inv/slot) right before sending, to compare against the
        // server's own "assignment-action ... Accept placed onto ..." trace. Drop once concluded.
        capi.Logger.Notification("[scribe] Accept sent for {0}: target=\"{1}\" inv={2} slot={3}",
            taskId, target.Label, target.InventoryId, target.SlotId);
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeAssignmentActionMessage
        {
            AssignmentId = taskId.ToByteArray(),
            Action = (byte)ScribeAssignmentAction.Accept,
            TargetInventoryId = target.InventoryId,
            TargetSlotId = target.SlotId,
            NewTaskInsert = (byte)ScribePlayerSettings.NormalizeNewTaskInsert(modSystem.MySettings.NewTaskInsert),
        });
    }

    /// <summary>This player's current Accept-placement candidates (assignment-state-machine's placement
    /// requirement) — thin wrapper over the shared <see cref="ScribeAcceptCandidates.Compute"/> helper
    /// (add-progression-framework-quest-support Decision 3, extracted so Quest auto-link's Accept flow can
    /// reuse the exact same eligibility rule and ordering instead of diverging), preferring the last-opened
    /// Scribe item as the picker's convenience default. Recomputed on each Inbox/Assignment rebuild — a
    /// stale list (inventory changed without a rebuild trigger) is harmless, since the server re-validates
    /// the resolved slot itself regardless.</summary>
    private List<ScribeAcceptCandidate> ComputeAcceptCandidates()
        => ScribeAcceptCandidates.Compute(capi, modSystem.LastOpenedScribeItemDocId);

    /// <summary>Builds the Assignment Desk's Create Assignments tab. Only
    /// <see cref="GuiDialogScribeAssignmentDesk"/> ever routes <c>viewMode</c> here (design.md Decision 1),
    /// and its real content (the staging slot + multi-select row list + batch-send form, per
    /// assignment-multi-item-creation design.md D8-D13) needs that dialog's typed
    /// <see cref="BlockEntityAssignmentDesk"/>/slot-controller access — mirroring how
    /// <see cref="BuildInventoryContent"/> is a base placeholder overridden by the one surface that
    /// actually has that tab. The base placeholder is never seen in practice.</summary>
    protected virtual Widget BuildAssignmentContent()
    {
        var colors = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        return new Center(child: new Text(Lang.Get("scribe:scribe-gui-inventory-empty"),
            new TextStyle { Color = colors.OnSurfaceVariant }));
    }

    /// <summary>Every other online player, as (uid, display name) — the target-player picker's options,
    /// shared by the Create Assignments form. Self-assignment is deliberately allowed
    /// (<c>ScribeAssignmentStore.TryApplyAction</c> already resolves it correctly — a self-assignment
    /// matches both the Assigner and Assignee role checks) so the list is never empty in singleplayer,
    /// where the local player is the only "online" player; their own entry is labeled distinctly so it
    /// doesn't read as a stray duplicate.</summary>
    private protected List<(string Uid, string Name)> ComputeAssignmentTargetPlayers()
    {
        var localUid = capi.World.Player.PlayerUID;
        return capi.World.AllOnlinePlayers
            .Select(p => (p.PlayerUID, p.PlayerUID == localUid
                ? Lang.Get("scribe:scribe-assignment-target-self", p.PlayerName)
                : p.PlayerName))
            .ToList();
    }

    /// <summary>This player's own Sent assignments (design.md Decision 3's read-only history), as
    /// <see cref="ScribeInboxRowData"/> for <see cref="ScribeInboxContent"/>. Newest-batch-first, matching
    /// the Inbox tab (triage 2026-08-31 general notes: "Both ... should have the same ordering").</summary>
    private protected List<ScribeInboxRowData> ComputeSentAssignmentRows() =>
        NewestBatchFirst(modSystem.MySentAssignments.Where(b => b.Assignment is not null))
            .Select(b => new ScribeInboxRowData(
                TaskId: b.TaskId, Text: b.Text, Depth: b.Depth,
                State: b.Assignment!.State, AssignerUid: b.Assignment.AssignerUid,
                TargetPlayerUid: b.Assignment.TargetPlayerUid, AssignedDate: b.Assignment.AssignedDate,
                Seen: b.Assignment.Seen, ViewerRole: ScribeAssignmentActor.Assigner,
                DisplayName: ResolveRowItem(b).Name, AcceptedDate: b.Assignment.AcceptedDate,
                DeclinedDate: b.Assignment.DeclinedDate, CancelledDate: b.Assignment.CancelledDate,
                DiscardedDate: b.Assignment.DiscardedDate, CompletedDate: b.Assignment.CompletedDate,
                ReceivedDate: b.Assignment.ReceivedDate))
            .ToList();

    /// <summary>Builds the Sent Assignment History tab (refine-assignment-desk-inbox-ux 12.2/12.3): this
    /// player's own read-only Sent history — state pills + the historical row list — split OUT of the
    /// Create Assignments tab, which now shows staging-and-select only (12.1). Only the Assignment Desk
    /// routes <c>viewMode</c> here (mirrors <see cref="BuildAssignmentContent"/>'s Decision-1 scoping),
    /// but unlike that method this needs nothing Desk-specific — every piece
    /// (<see cref="ComputeSentAssignmentRows"/>, <see cref="ResolvePlayerNameForInbox"/>,
    /// <see cref="SendAssignmentAction"/>) already lives on the base — so this is a real implementation,
    /// not a placeholder-plus-override.</summary>
    protected Widget BuildSentAssignmentHistoryContent() =>
        new ScribeInboxContent(
            rows: ComputeSentAssignmentRows(),
            resolvePlayerName: ResolvePlayerNameForInbox,
            onAction: SendAssignmentAction,
            onDelete: DeleteAssignmentRecord,
            activeFilterGroup: assignmentFilterGroup,
            onFilterGroupChanged: SetAssignmentFilterGroup,
            isExpanded: expandedAssignmentIds.Contains,
            onToggleExpand: ToggleAssignmentRowExpanded,
            style: RowStyle,
            scrollController: sharedScrollController,
            emptyHintLangKey: "scribe:scribe-assignment-sent-empty");

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

    /// <summary>Builds the Inbox Inventory tab content (add-inbox-inventory-tab). Only the standalone
    /// Inbox block exposes this tab, so the base returns an empty placeholder; <see cref="GuiDialogScribeInbox"/>
    /// overrides it to place the 8-slot mixed restricted/open grid. Mirrors <see cref="BuildInventoryContent"/>.</summary>
    protected virtual Widget BuildInboxInventoryContent()
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
    /// re-acquire (<see cref="HandleSaveFailed"/>) and must NOT be gated. Protected (not private) so a
    /// subclass building its own Edit nav button (the Assignment Desk, add-assignment-desk-own-tasks) can
    /// wire directly to it instead of duplicating the lock-check.</summary>
    protected void TryEnterEditor()
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
