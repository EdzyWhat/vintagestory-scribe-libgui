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

    /// <summary>Enters (or refreshes) the editor view on the authoritative document bytes, called when editor access is granted
    /// (lock held). Seeds a fresh scratch copy, starts autosave, and rebuilds (or lets <c>TryOpen</c> build it if not yet open).</summary>
    public void EnterEditorMode(byte[]? documentBytes)
    {
        if ((recoveringLostLock || optimisticEditorEntry) && scratch is not null)
        {
            // A live scratch must NOT be overwritten by the authoritative document; this grant just confirms the lock, so keep the
            // scratch and re-flush it. Two callers: lost-lock recovery after a save failure (preserve unsaved edits), and a
            // singleplayer-optimistic Handbook append whose delayed grant carries the pre-flush document.
            // The INITIAL optimistic entry has scratch still null, so it falls through to the full seed path.
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
        // Enforce "empty tasks are never persisted" at the LOAD boundary too: a purge-flush and a re-access request can cross on the
        // wire, so the granted seed can carry the PRE-purge doc. PurgeEmptyRowsFromScratch sets isDirty iff it removed something, so a
        // stale seed is re-flushed clean. Runs BEFORE SyncFocusNodesToScratch so node count matches; empty text sections are untouched.
        PurgeEmptyRowsFromScratch();
        isEditorMode = true;
        focusedEditIndex = null;
        autoFocusRowOnRebuild = null;
        saveFailureRetries = 0;
        recoveringLostLock = false;
        SyncFocusNodesToScratch();
        StartAutosaveTick();

        // Apply a Handbook "Add to Scribe" append that arrived while not editing, now that editor access has landed. The recovery
        // branch above returns early, so this never fires on a lost-lock re-acquire. Applied BEFORE the ForceRebuild so the fresh tree
        // includes the new row: ApplyHandbookAppend's in-place RebuildBody no-ops right after Mount (GlobalKey state not yet resolvable).
        FlushPendingHandbookAppend();

        if (IsOpened())
        {
            ForceRebuild();
        }
    }

    /// <summary>Enters (or stays in) the read view, tearing down the editor if active; also lands on Read from the Pin Tab.</summary>
    public void EnterReadMode()
    {
        LeaveEditorIfActive();
        viewMode = ScribeLecternView.Read; // also covers a Pin Tab → Read switch (no editor teardown)
        if (IsOpened())
        {
            ForceRebuild();
        }
    }

    /// <summary>Flushes and releases the editor lock if held, without touching <c>viewMode</c> (<see cref="LeaveEditorMode"/> itself
    /// does NOT release). Lets <see cref="EnterGrantedView"/> overrides with no Read view tear down an editor session without landing on Read.</summary>
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

    /// <summary>Called for every non-editor access grant/refusal (an ordinary right-click). The base lands on Read; the Assignment
    /// Desk and Inbox have no Read view, so they override it to tear down any editor session and keep the currently selected tab.</summary>
    public virtual void EnterGrantedView() => EnterReadMode();

    /// <summary>Switches to the Pin Tab, tearing down the editor first if active (flush + release lock, like
    /// <see cref="OnClickSwitchToRead"/>). Needs no server round-trip: the Pin Tab reads the synced <c>MyPins</c>.</summary>
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

    /// <summary>Rebuilds the Inbox/Assignment view in place (reconcile, not <see cref="GuiBase.ForceRebuild"/>) so the dialog-owned
    /// expanded state and filter selection survive. Called on every <see cref="ScribeModSystem.MyAssignmentsChanged"/>.</summary>
    private void OnMyAssignmentsChanged()
    {
        if (!IsOpened()) return;
        // SentHistory is included so a sync repaints that tab too.
        if (viewMode is ScribeLecternView.Inbox or ScribeLecternView.Assignment or ScribeLecternView.SentHistory)
            RebuildBody();
    }

    /// <summary>Rebuilds the Create Assignments tab so its target picker reflects a known-players sync. Inert off the Assignment Desk.</summary>
    private void OnKnownPlayersChanged()
    {
        if (IsOpened() && IsAssignmentView) RebuildBody();
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
        // Family inherited from the wrap; only size/color are set. Placeholder for hosts that don't override History.
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
        // Family inherited from the wrap; only size/color are set. Placeholder for hosts that don't override Timer.
        var bodyStyle = new TextStyle { FontSize = bodySize, Color = colors.OnSurface };
        return ScribeTextDefaults.Wrap(modSystem.MySettings.TaskFontFamily, bodySize,
            new Center(child: new Text(Lang.Get("scribe:scribe-gui-timer-empty"), bodyStyle)));
    }

    /// <summary>Switches to the Inventory tab (Scriptorium storage), tearing down the editor first if active.</summary>
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

    /// <summary>Rebuilds the Inventory view if active, after an inventory resync so another client's slots repaint.</summary>
    protected internal void RefreshInventoryView()
    {
        if (viewMode == ScribeLecternView.Inventory && IsOpened()) ForceRebuild();
    }

    /// <summary>Switches to the Inbox Inventory tab (only <see cref="GuiDialogScribeInbox"/> has it), tearing down the editor first if
    /// active. Lives here because the editor teardown state is only reachable from this partial class.</summary>
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

    /// <summary>Rebuilds the Inbox Inventory view if active; the peer of <see cref="RefreshInventoryView"/>.</summary>
    protected internal void RefreshInboxInventoryView()
    {
        if (viewMode == ScribeLecternView.InboxInventory && IsOpened()) ForceRebuild();
    }

    /// <summary>Switches to the Assignment tab (Assignment Desk only), tearing down the editor first if active.</summary>
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

    /// <summary>Switches to the Sent Assignment History tab (Assignment Desk only), tearing down the editor first if active.</summary>
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

    /// <summary>Sends the mark-seen request. Unconditional because the server skips the re-push when nothing was unseen; also called by
    /// the Inbox block's dialog from its ctor and <c>EnterGrantedView()</c>, which never go through <see cref="OnClickSwitchToInbox"/>.</summary>
    protected void MarkInboxSeenIfNeeded() =>
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeMarkAssignmentsSeenMessage());

    /// <summary>Builds the shared Inbox tab from <see cref="ScribeModSystem.MyReceivedAssignments"/> (Assignee view) via
    /// <see cref="ScribeInboxContent"/>. Sent history is <see cref="BuildSentAssignmentHistoryContent"/>.</summary>
    protected virtual Widget BuildInboxContent()
    {
        var received = modSystem.MyReceivedAssignments.Where(b => b.Assignment is not null);
        var rows = NewestBatchFirst(received)
            .Select(b => new ScribeInboxRowData(
                TaskId: b.TaskId, Text: b.Text, Depth: b.Depth,
                State: b.Assignment!.State, AssignerUid: b.Assignment.AssignerUid,
                TargetPlayerUid: b.Assignment.TargetPlayerUid,
                AssignedDate: AssignmentDisplay.Date(b.Assignment.AssignedDate, b.Assignment.AssignedTimestamp, capi.World),
                Seen: b.Assignment.Seen, ViewerRole: ScribeAssignmentActor.Assignee,
                DisplayName: ResolveRowItem(b).Name,
                AcceptedDate: OptionalAssignmentDate(b.Assignment.AcceptedDate, b.Assignment.AcceptedTimestamp),
                DeclinedDate: OptionalAssignmentDate(b.Assignment.DeclinedDate, b.Assignment.DeclinedTimestamp),
                CancelledDate: OptionalAssignmentDate(b.Assignment.CancelledDate, b.Assignment.CancelledTimestamp),
                DiscardedDate: OptionalAssignmentDate(b.Assignment.DiscardedDate, b.Assignment.DiscardedTimestamp),
                CompletedDate: OptionalAssignmentDate(b.Assignment.CompletedDate, b.Assignment.CompletedTimestamp),
                AcceptedIntoLabel: b.Assignment.AcceptedIntoLabel,
                ReceivedDate: OptionalAssignmentDate(b.Assignment.ReceivedDate, b.Assignment.ReceivedTimestamp),
                RedirectedFromUid: b.Assignment.RedirectedFromUid,
                RedirectedDate: OptionalAssignmentDate(b.Assignment.RedirectedDate, b.Assignment.RedirectedTimestamp)))
            .ToList();

        return new ScribeInboxContent(
            rows: rows,
            resolvePlayerName: ResolvePlayerNameForInbox,
            onAction: SendAssignmentAction,
            labelLangKey: "scribe:scribe-tab-inbox",
            descriptorLangKey: "scribe:scribe-tab-subtitle-inbox",
            onAccept: AcceptAssignment,
            acceptCandidates: ComputeAcceptCandidates(),
            onDelete: DeleteAssignmentRecord,
            activeFilterGroup: assignmentFilterGroup,
            onFilterGroupChanged: SetAssignmentFilterGroup,
            isExpanded: expandedAssignmentIds.Contains,
            onToggleExpand: ToggleAssignmentRowExpanded,
            style: RowStyle,
            scrollController: sharedScrollController,
            showSubtitleRow: modSystem.VisualTuning.ShowSubtitleRow);
    }

    /// <summary>Sets the dialog-owned Inbox/Sent-History filter and reconciles so content and the title-bar toggle repaint together.</summary>
    private void SetAssignmentFilterGroup(ScribeAssignmentFilterGroup group)
    {
        if (assignmentFilterGroup == group) return;
        assignmentFilterGroup = group;
        RebuildBody();
    }

    /// <summary>Toggles one row in <see cref="expandedAssignmentIds"/> and reconciles so the title-bar toggle stays in sync.</summary>
    private void ToggleAssignmentRowExpanded(Guid taskId)
    {
        if (!expandedAssignmentIds.Remove(taskId)) expandedAssignmentIds.Add(taskId);
        RebuildBody();
    }

    /// <summary>TaskIds visible in the active Inbox/Sent History view after the filter: the rows the title-bar expand/collapse-all
    /// toggle operates on. Empty for other views.</summary>
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

    /// <summary>Whether every visible assignment row is expanded; false when none are visible.</summary>
    private bool AllVisibleAssignmentRowsExpanded()
    {
        var ids = CurrentlyVisibleAssignmentRowIds();
        return ids.Count > 0 && ids.All(expandedAssignmentIds.Contains);
    }

    /// <summary>Title-bar toggle handler: expands every visible row if any is collapsed, else collapses all. No-op when none are visible.</summary>
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

    /// <summary>Requests deletion of the viewer's own side of a terminal-state record: Inbox is the Assignee's view, Sent History the
    /// Assigner's. The server re-validates (<see cref="Scribe.Core.ScribeAssignmentStore.TryDelete"/>); the row disappears when the resync arrives.</summary>
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

    /// <summary>Newest-batch-first ordering for Inbox and Sent History: groups by <see cref="ScribeAssignment.BatchId"/>, keeps each
    /// group's internal order (parent before subtask), and reverses the group order. <c>GroupBy</c> preserves first-occurrence and source order by contract.</summary>
    private static IEnumerable<ScribeBlock> NewestBatchFirst(IEnumerable<ScribeBlock> blocks) =>
        blocks.GroupBy(b => b.Assignment!.BatchId).Reverse().SelectMany(g => g);

    /// <summary>UID to display-name lookup for the "Assigned by" line; resolves offline-but-known assigners.</summary>
    private protected string ResolvePlayerNameForInbox(string uid) => modSystem.ResolvePlayerName(uid);

    /// <summary>(assigner name, assigned date, accepted date) for the assignment marker's tooltip on Read/Editor rows; null unless
    /// Accepted. Dates with a timestamp follow the viewer's locale. The Pin Tab uses snapshotted <see cref="ScribePinnedRef"/> fields instead.</summary>
    private protected (string? name, string? assignedDate, string? acceptedDate) ResolveAssignmentTooltipInfo(
        ScribeAssignment? assignment)
        => assignment is { State: ScribeAssignmentState.Accepted }
            ? (ResolvePlayerNameForInbox(assignment.AssignerUid),
                AssignmentDisplay.Date(assignment.AssignedDate, assignment.AssignedTimestamp, capi.World),
                OptionalAssignmentDate(assignment.AcceptedDate, assignment.AcceptedTimestamp))
            : (null, null, null);

    /// <summary>Viewer-locale calendar date when <paramref name="timestamp"/> is present; otherwise the
    /// stored identity string (null stays null).</summary>
    private protected string? OptionalAssignmentDate(string? baked, double? timestamp)
        => timestamp is { } ts
            ? AssignmentDisplay.Date(baked ?? "", ts, capi.World)
            : baked;

    /// <summary>Sends a Decline/Cancel/Discard request (<see cref="ScribeAssignmentActionMessage"/>). Accept uses
    /// <see cref="AcceptAssignment"/> since it needs a placement target. The server re-validates.</summary>
    private protected void SendAssignmentAction(Guid taskId, ScribeAssignmentAction action)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeAssignmentActionMessage
        {
            AssignmentId = taskId.ToByteArray(),
            Action = (byte)action,
        });
    }

    /// <summary>Sends an Accept request naming the resolved placement target (from <see cref="ComputeAcceptCandidates"/>). The server re-validates and re-resolves the slot.</summary>
    private void AcceptAssignment(Guid taskId, ScribeAcceptCandidate target)
    {
        // Diagnostic: logs what the client resolved, to compare with the server's "Accept placed onto ..." trace.
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

    /// <summary>This player's Accept-placement candidates via <see cref="ScribeAcceptCandidates.Compute"/> (shared with Quest auto-link),
    /// preferring the last-opened Scribe item. A stale list is harmless; the server re-validates.</summary>
    private List<ScribeAcceptCandidate> ComputeAcceptCandidates()
        => ScribeAcceptCandidates.Compute(capi, modSystem.LastOpenedScribeItemDocId);

    /// <summary>Builds the Create Assignments tab. Only <see cref="GuiDialogScribeAssignmentDesk"/> routes here and its real content
    /// needs that dialog's typed access, so the base is a placeholder never seen in practice.</summary>
    protected virtual Widget BuildAssignmentContent()
    {
        var colors = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        return new Center(child: new Text(Lang.Get("scribe:scribe-gui-inventory-empty"),
            new TextStyle { Color = colors.OnSurfaceVariant }));
    }

    /// <summary>All players ever connected plus those online, as (uid, display name) for the target picker. Self-assignment is allowed
    /// (so singleplayer's list is never empty) and the local entry is labeled distinctly. Sorted case-insensitively by real name BEFORE
    /// the self-label substitution; live online names win over stale cached ones.</summary>
    private protected List<(string Uid, string Name)> ComputeAssignmentTargetPlayers()
    {
        var localUid = capi.World.Player.PlayerUID;

        var byUid = new Dictionary<string, string>();
        foreach (var (uid, name) in modSystem.MyKnownPlayers) byUid[uid] = name;
        foreach (var p in capi.World.AllOnlinePlayers) byUid[p.PlayerUID] = p.PlayerName;

        return byUid
            .OrderBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .Select(kv => (kv.Key, kv.Key == localUid
                ? Lang.Get("scribe:scribe-assignment-target-self", kv.Value)
                : kv.Value))
            .ToList();
    }

    /// <summary>This player's Sent assignments as rows for <see cref="ScribeInboxContent"/>, newest-batch-first like the Inbox.</summary>
    private protected List<ScribeInboxRowData> ComputeSentAssignmentRows() =>
        NewestBatchFirst(modSystem.MySentAssignments.Where(b => b.Assignment is not null))
            .Select(b => new ScribeInboxRowData(
                TaskId: b.TaskId, Text: b.Text, Depth: b.Depth,
                State: b.Assignment!.State, AssignerUid: b.Assignment.AssignerUid,
                TargetPlayerUid: b.Assignment.TargetPlayerUid,
                AssignedDate: AssignmentDisplay.Date(b.Assignment.AssignedDate, b.Assignment.AssignedTimestamp, capi.World),
                Seen: b.Assignment.Seen, ViewerRole: ScribeAssignmentActor.Assigner,
                DisplayName: ResolveRowItem(b).Name,
                AcceptedDate: OptionalAssignmentDate(b.Assignment.AcceptedDate, b.Assignment.AcceptedTimestamp),
                DeclinedDate: OptionalAssignmentDate(b.Assignment.DeclinedDate, b.Assignment.DeclinedTimestamp),
                CancelledDate: OptionalAssignmentDate(b.Assignment.CancelledDate, b.Assignment.CancelledTimestamp),
                DiscardedDate: OptionalAssignmentDate(b.Assignment.DiscardedDate, b.Assignment.DiscardedTimestamp),
                CompletedDate: OptionalAssignmentDate(b.Assignment.CompletedDate, b.Assignment.CompletedTimestamp),
                ReceivedDate: OptionalAssignmentDate(b.Assignment.ReceivedDate, b.Assignment.ReceivedTimestamp),
                RedirectedFromUid: b.Assignment.RedirectedFromUid,
                RedirectedDate: OptionalAssignmentDate(b.Assignment.RedirectedDate, b.Assignment.RedirectedTimestamp)))
            .ToList();

    /// <summary>Builds the Sent Assignment History tab: the player's read-only Sent history with state pills, separate from Create
    /// Assignments. A real implementation, since nothing Desk-specific is needed.</summary>
    protected Widget BuildSentAssignmentHistoryContent() =>
        new ScribeInboxContent(
            rows: ComputeSentAssignmentRows(),
            resolvePlayerName: ResolvePlayerNameForInbox,
            onAction: SendAssignmentAction,
            labelLangKey: "scribe:scribe-tab-senthistory",
            descriptorLangKey: "scribe:scribe-tab-subtitle-senthistory",
            onDelete: DeleteAssignmentRecord,
            activeFilterGroup: assignmentFilterGroup,
            onFilterGroupChanged: SetAssignmentFilterGroup,
            isExpanded: expandedAssignmentIds.Contains,
            onToggleExpand: ToggleAssignmentRowExpanded,
            style: RowStyle,
            scrollController: sharedScrollController,
            showSubtitleRow: modSystem.VisualTuning.ShowSubtitleRow,
            emptyHintLangKey: "scribe:scribe-assignment-sent-empty");

    /// <summary>Builds the Inventory tab content; the base is an empty placeholder that <see cref="GuiDialogScribeScriptorium"/> overrides with the slots.</summary>
    protected virtual Widget BuildInventoryContent()
    {
        var colors = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        float bodySize = ScribeRowConstants.BaseWindowFontSize
            * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.WindowFontScale);
        var bodyStyle = new TextStyle { FontSize = bodySize, Color = colors.OnSurface };
        return ScribeTextDefaults.Wrap(modSystem.MySettings.TaskFontFamily, bodySize,
            new Center(child: new Text(Lang.Get("scribe:scribe-gui-inventory-empty"), bodyStyle)));
    }

    /// <summary>Builds the Inbox Inventory tab content; the base is an empty placeholder that <see cref="GuiDialogScribeInbox"/> overrides with the slot grid.</summary>
    protected virtual Widget BuildInboxInventoryContent()
    {
        var colors = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        float bodySize = ScribeRowConstants.BaseWindowFontSize
            * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.WindowFontScale);
        var bodyStyle = new TextStyle { FontSize = bodySize, Color = colors.OnSurface };
        return ScribeTextDefaults.Wrap(modSystem.MySettings.TaskFontFamily, bodySize,
            new Center(child: new Text(Lang.Get("scribe:scribe-gui-inventory-empty"), bodyStyle)));
    }

    /// <summary>"Done editing": flush the pending edit, release the lock, and swap to read, all locally. Flush BEFORE releasing: the
    /// server processes packets in order, so releasing first would make the edit arrive lock-less and be rejected by
    /// <see cref="BlockEntityScribeLectern.ApplyEdit"/>.</summary>
    private void OnClickSwitchToRead()
    {
        if (focusedEditIndex is { } idx) NormalizeRowOnCommit(idx);
        // Drop abandoned empty tasks before flushing; the focused row may be an untyped new task the blur hasn't swept.
        PurgeEmptyRowsFromScratch();
        pendingEmptyRowRemoval = null; // superseded by the purge; don't act on it after the editor tears down
        FlushIfDirty();
        SendReleaseLockPacket();
        CaptureScrollForRestore();
        LeaveEditorMode();
        ForceRebuild();
    }

    /// <summary>Snapshots the scroll offset to re-apply after the next view's first layout (see <see cref="pendingRestoreScrollOffset"/>). Needed on edit-to-read
    /// (the virtualized read list lays out lazily); read-to-edit needs none since the editor's non-virtualized scroll view measures immediately.</summary>
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
        isEditorMode = false; // lands on Read by default (the isEditorMode setter's false branch)
        OnLeftEditorMode();
        scratch = null;
        isDirty = false;
        focusedEditIndex = null;
        autoFocusRowOnRebuild = null;
        saveFailureRetries = 0;
        recoveringLostLock = false;
        optimisticEditorEntry = false;
        DisposeFocusNodes();
    }

    /// <summary>Fires on every editor teardown ("Done editing" and <see cref="LeaveEditorIfActive"/>). The default is a no-op since the
    /// <see cref="isEditorMode"/> setter already landed on Read; the Assignment Desk overrides it to land on Create Assignments instead.</summary>
    protected virtual void OnLeftEditorMode() { }

    /// <summary>
    /// Called when an autosave was rejected because this client lost the editor lock. Re-requests the lock keeping the scratch
    /// (<see cref="recoveringLostLock"/>) so the re-grant in <see cref="EnterEditorMode"/> re-flushes the edit that
    /// <see cref="FlushIfDirty"/> optimistically cleared. Bounded by <see cref="MaxSaveFailureRetries"/>.
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

    /// <summary>The "switch to editor" entry point for the read-view footer and Edit nav button. If the synced state shows another
    /// player holds the lock, it shows the in-game error without a server round-trip (the server refusal remains the backstop).
    /// Distinct from the ungated <see cref="RequestEditorAccess"/>, which lost-lock recovery also uses. Protected so the Assignment
    /// Desk's own Edit button can wire to it.</summary>
    protected void TryEnterEditor()
    {
        if (host.IsLockedByOther(capi.World.Player.PlayerUID))
        {
            capi.TriggerIngameError(this, "scribe-lectern-locked", Lang.Get("scribe:scribe-gui-locked"));
            return;
        }

        RequestEditorAccess();
    }

    /// <summary>Requests editor access from the server; a grant returns via <see cref="BlockEntityScribeLectern.HandleServerReply"/>
    /// to <see cref="EnterEditorMode"/>. Raw form also used by lost-lock recovery; subclasses without a round-trip (Notebook) override it.</summary>
    protected virtual void RequestEditorAccess()
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeRequestAccessMessage
        {
            DocIdBytes = host.Document.DocId.ToByteArray(),
            WantEditor = true,
        });
    }

    /// <summary>
    /// Called when the authoritative document changes. Read mode rebuilds from it. Editor mode keeps scratch as the source of truth
    /// for text but propagates three orthogonal external effects: tasks the document no longer contains are dropped; externally
    /// flipped <see cref="ScribeBlock.Done"/> state is synced; and a just-completed task the server sank to the bottom is moved
    /// there too. Syncing done-state + order (never text) keeps the next autosave from reverting an external completion.
    /// </summary>
    /// <summary>Read View filter-pill tap: updates and persists the selection and rebuilds so <see cref="BuildReadContent"/>
    /// re-filters. Skips a tap on the active pill.</summary>
    private void OnReadViewFilterCategoryChanged(ReadViewFilterCategory category)
    {
        if (readViewFilterCategory == category) return;
        readViewFilterCategory = category;
        // Mark pending until the server's echo lands so RefreshReadView() doesn't revert it to the stale host mirror.
        pendingReadViewFilterCategory = category;
        PersistReadViewState();
        if (IsOpened()) ForceRebuild();
    }

    /// <summary>Flips a Read View subtask group's collapsed state (by parent TaskId) and persists it like the filter pill.</summary>
    private void OnReadViewToggleGroupCollapsed(Guid groupParentTaskId)
    {
        if (!collapsedReadViewGroupIds.Remove(groupParentTaskId)) collapsedReadViewGroupIds.Add(groupParentTaskId);
        // Same pending-until-confirmed stale-mirror guard as the filter pill.
        pendingCollapsedReadViewGroupIds = new HashSet<Guid>(collapsedReadViewGroupIds);
        PersistReadViewState();
        if (IsOpened()) ForceRebuild();
    }

    /// <summary>Sends the filter pill + collapsed-group set to the server for per-instance persistence; lock-free (a viewer preference, not a document edit).</summary>
    private void PersistReadViewState()
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeSetReadViewStateMessage
        {
            DocId = host.Document.DocId.ToByteArray(),
            FilterCategory = (byte)readViewFilterCategory,
            CollapsedGroupIds = collapsedReadViewGroupIds.Select(g => g.ToByteArray()).ToList(),
        });
    }

    public void RefreshReadView()
    {
        // Re-derive the Read View filter/collapse fields from the host's persisted state on EVERY resync, so this dialog converges with
        // echoes and other clients' changes. A local pick still in flight is not overwritten until the mirror catches up to it.
        var hostFilterCategory = (ReadViewFilterCategory)host.ReadViewFilterCategory;
        if (ScribeReadViewFilter.ShouldAcceptHostFilterCategory(pendingReadViewFilterCategory, hostFilterCategory))
        {
            readViewFilterCategory = hostFilterCategory;
            pendingReadViewFilterCategory = null;
        }
        if (ScribeReadViewFilter.ShouldAcceptHostCollapsedGroupIds(pendingCollapsedReadViewGroupIds, host.CollapsedGroupIds))
        {
            collapsedReadViewGroupIds.Clear();
            collapsedReadViewGroupIds.UnionWith(host.CollapsedGroupIds);
            pendingCollapsedReadViewGroupIds = null;
        }

        if (!isEditorMode)
        {
            if (!IsOpened()) return;
            // Read view: the list is non-virtualized and TaskId-keyed, so an external change reconciles in place (rows reused, scroll
            // offset preserved). Arm the hover latch in case a completion slid a row under a stationary cursor.
            if (viewMode == ScribeLecternView.Read)
            {
                hoverRefreshLatch.Arm();
                RebuildBody();
                return;
            }
            // Other non-editor views ForceRebuild. Pinned keeps its offset via reconcile through OnMyPinsChanged, so it skips scroll capture-restore.
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
            // A scratch task absent from the server is either one the server dropped (HUD/Delete policy elsewhere), which should disappear
            // here, or one this editor JUST created that autosave hasn't sent yet (throttled ~1s, skips an empty focused row), which must not
            // be yanked out. So never drop the row being edited or an empty Task (never persisted by design). Trackers/Links have empty Text
            // by design yet ARE persisted, so their server-side deletion is real and must drop; otherwise the next flush would resurrect them.
            if (focusedEditIndex == i || (b.IsTask && string.IsNullOrWhiteSpace(b.Text))) continue;
            DeleteEditorBlock(i);
            structural = true;
        }

        // (2) Sync external completion (done-state only, NOT text) from the server's authoritative state into surviving scratch tasks,
        // noting a just-completed task the server sank to the bottom. ToggleTask is called only on a mismatch, so it flips to match.
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
            isDirty = true; // carry the synced done-state into the next flush so it isn't reverted
            doneChanged = true;
            // A just-completed task now last server-side was sunk by Sink/UnpinSink; mirror it. Only that row moves, so a local drag-reorder elsewhere is undisturbed.
            if (serverBlockDone && serverLastTaskId == b.TaskId && i != scratch.Blocks.Count - 1)
                sinkFrom = i;
        }

        // (3) Mirror the sink move via reconcile, preserving the edited row's focus (the move is external). It owns its own rebuild.
        if (sinkFrom >= 0)
        {
            ReorderEditorBlock(sinkFrom, scratch.Blocks.Count - 1, anchorViewport: true, preserveFocusedRow: true);
            return;
        }

        // A deletion already scheduled its rebuild; a done-only change still needs one to repaint the checkbox.
        if (structural) return;
        if (doneChanged) RebuildBody();
    }
}
