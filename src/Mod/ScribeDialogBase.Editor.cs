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
    // ---------------- Editor operations (called from the editor rows) ----------------

    /// <summary>Live text change from a focused field: writes through to the scratch block and marks dirty (autosave
    /// or commit serializes it). The field auto-grows itself, so only the scroll-follow is armed.</summary>
    private void NotifyTextChanged(int index, string text)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        scratch.SetBlockText(index, text);
        isDirty = true;
        pendingEnsureVisible = true;
        TraceScroll($"text-changed {index}");
    }

    /// <summary>A Tracker row's +/- stepper changed its target quantity: writes through to the scratch block and marks
    /// dirty (the codec already serializes it). The Core setter clamps to at least 1. Deliberately does NOT rebuild, which
    /// would drop the stepper's focus mid-step.</summary>
    private void SetEditorTrackerTargetQuantity(int index, int qty)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        var block = scratch.Blocks[index];
        if (!block.IsCarriedCountTracked) return; // Tracker OR Craft parent (both carry a target stepper)
        block.TargetQuantity = qty; // clamps ≥ 1 in the Core setter
        isDirty = true;

        // A Craft parent owns ingredient subtasks whose targets scale with its own: re-heal the depth-1 run and
        // rebuild so the rescaled rows are visible (warranted here, unlike a plain Tracker's stepper).
        if (block.IsCraft)
        {
            RescaleCraftFromSignature(scratch, block);
            RebuildBody();
        }
    }

    /// <summary>A tap on a row's grip toggles its subtask depth between 0 and 1 (any row kind). Mutates scratch,
    /// marks dirty, rebuilds; the codec already round-trips <see cref="ScribeBlock.Depth"/>. A drag never routes here
    /// (the dispatcher suppresses the tap during a drag).</summary>
    private void OnGripTap(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        var block = scratch.Blocks[index];
        block.Depth = block.Depth == 0 ? 1 : 0; // Core clamps to [0, 1]; no depth-2 is reachable
        isDirty = true;
        RebuildBody();
    }

    /// <summary>A focused row's caret moved by keyboard navigation with no text change. A bare caret move skips
    /// <c>OnChanged</c>, so this arms <see cref="pendingEnsureVisible"/> (serviced by <see cref="EnsureCaretVisible"/>).</summary>
    private void NotifyCaretMoved(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        pendingEnsureVisible = true;
        TraceScroll($"caret-moved {index}");
    }

    /// <summary>A row was focused by a MOUSE CLICK. The caret is already in view where the player clicked, so the
    /// view must not move: this cancels our scroll-follow (<see cref="pendingEnsureVisible"/>) and undoes the
    /// shipped <c>FocusManager.RequestFocus</c>'s unconditional <c>Scrollable.EnsureVisible</c> (which bounces a
    /// taller-than-viewport row) by jumping back to <see cref="lastStableScrollOffset"/> before the next render.
    /// Programmatic focus (<see cref="FocusEditorRow"/>) should scroll and does not route here.</summary>
    private void NotifyPointerFocus(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        pendingEnsureVisible = false;
        // Restore the pre-click offset; a no-op when focus didn't change (the controller filters equal values).
        sharedScrollController.JumpTo(lastStableScrollOffset);
        TraceScroll($"pointer-focus {index}");
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

    /// <summary>Enter: commits the focused row, inserts an empty task beneath it, and focuses it. Enter on an
    /// already-empty task row is a no-op so repeated Enter can't stack empty rows.</summary>
    private void EditorInsertTaskBelow(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        TraceScroll($"insert-below {index}");

        // Don't stack another empty task beneath an already-empty task row.
        var current = scratch.Blocks[index];
        if (current.IsTask && string.IsNullOrWhiteSpace(current.Text)) return;

        // Tier cap: the Enter=new-task gesture also stops at the tablet cap.
        if (!CanAddTaskUnderPolicy()) { NotifyTabletFull(); return; }

        NormalizeRowOnCommit(index);
        FlushIfDirty();

        int insertAt = index + 1;
        if (scratch.InsertTask(insertAt, ""))
        {
            isDirty = true;
            SyncFocusNodesToScratch();
            // A new row MOUNTS, so its mount-only autoFocus fires (use autoFocusRowOnRebuild, not pendingFocusRow).
            // Rows above keep their slots; rows below remount unfocused.
            autoFocusRowOnRebuild = insertAt;
            focusedEditIndex = insertAt;
            pendingEnsureVisible = true;
            RebuildBody();
        }
        else
        {
            FocusEditorRow(Math.Min(index + 1, scratch.Blocks.Count - 1));
        }
    }

    /// <summary>Quick-add seam: inserts an empty task at the New Task Insert edge and focuses it, for the
    /// Shift+right-click capture gesture. Called after the editor view is active (via <see cref="EnterEditorMode"/>
    /// -> <see cref="QuickAddTopTask"/> for the lectern, directly for item hosts).
    ///
    /// <para>At the tier cap the editor opens but nothing is inserted and <see cref="NotifyTabletFull"/> fires.
    /// No-op if the editor isn't active.</para></summary>
    public void QuickAddTopTask()
    {
        if (!isEditorMode || scratch is null) return;
        TraceScroll("quick-add");

        // Tier cap: open the editor but refuse the insert at the cap.
        if (!CanAddTaskUnderPolicy()) { NotifyTabletFull(); return; }

        // Commit whatever row was focused before we shift indices (parity with OnClickAdd).
        if (focusedEditIndex is { } leaving) NormalizeRowOnCommit(leaving);

        int at = NewTaskInsertIndex();
        if (scratch.InsertTask(at, ""))
        {
            isDirty = true;
            SyncFocusNodesToScratch();
            // The fresh row MOUNTS, so autoFocus fires on it (reconcile path — see EditorInsertTaskBelow).
            autoFocusRowOnRebuild = at;
            focusedEditIndex = at;
            pendingEnsureVisible = true;
            if (IsOpened()) RebuildBody();
        }
    }

    /// <summary>A row's field lost focus. If the task or note at <paramref name="index"/> is blank, schedules its
    /// removal, so clearing a row and clicking away deletes it.
    ///
    /// <para>The delete is DEFERRED to <see cref="OnRenderGUI"/> (<see cref="pendingEmptyRowRemoval"/>): blur fires
    /// mid-way through <c>FocusManager.RequestFocus</c> on a row-to-row move, and deleting then would strand the
    /// focus of the row being entered. Emptiness is re-checked at removal, so this is idempotent with
    /// <see cref="OnRowFocusChanged"/>.</para></summary>
    private void OnRowBlurred(int index)
    {
        if (!isEditorMode || scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        // Tracker/Link rows are excluded (empty Text by design); see IsAbandonableEmptyBlock.
        if (!IsAbandonableEmptyBlock(scratch.Blocks[index])) return;
        pendingEmptyRowRemoval = index;
    }

    /// <summary>A row's editor field hit its per-kind character cap (already clamped by the field): shows a
    /// per-kind notice using the limit constants so message and cap can't drift.</summary>
    private void OnRowMaxLengthReached(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        bool isTask = scratch.Blocks[index].Kind == ScribeBlockKind.Task;
        int limit = isTask ? ScribeDocumentCodec.MaxTaskTextLength : ScribeDocumentCodec.MaxTextLength;
        capi.TriggerIngameError(
            this,
            isTask ? "scribe-task-limit" : "scribe-note-limit",
            Lang.Get(isTask ? "scribe:task-limit" : "scribe:note-limit", limit.ToString("N0")));
    }

    /// <summary>Editor checkbox toggle: completes a task under the player's completion policy, identically to the
    /// read/pinned views and HUD.
    ///
    /// <para>The policy is enacted in scratch, not via <see cref="ScribeCompleteTaskMessage"/>: the editor holds the
    /// edit lock and flushes the whole scratch document, so a lock-free completion message would be clobbered by the
    /// next flush. <c>ReconcileActorPins</c> carries pin effects, except <c>Unpin</c> (the task survives), which is
    /// sent explicitly.</para>
    ///
    /// <para>Focus: clicking the checkbox blurs the focused field via LibGUI's <c>DispatchPointerDown</c> focus-clear.
    /// The checkbox is focusable but excluded from Tab by <see cref="ScribeFieldOnlyTraversalPolicy"/>. Keep/Unpin
    /// do no rebuild, so focus is re-requested on the row that held it; Sink/Delete delegate to
    /// <see cref="ReorderEditorBlock"/>/<see cref="DeleteEditorBlock"/>, which own their focus fix-up.</para></summary>
    private void ToggleEditorTask(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        var block = scratch.Blocks[index];
        if (!block.IsCompletable) return;

        Guid taskId = block.TaskId;
        var behavior = modSystem.MySettings.SubtaskBehavior;
        var outcome = ScribeCompletion.ApplyLocal(scratch, taskId, modSystem.MySettings.CompletionPolicy, behavior);
        if (!outcome.Toggled) return;
        bool nowDone = outcome.NowDone;
        isDirty = true;
        TraceScroll($"complete {index} done={nowDone} policy={modSystem.MySettings.CompletionPolicy}");
        if (outcome.ShouldRemovePin)
        {
            foreach (var id in outcome.AffectedTaskIds)
                if (IsPinnedForMe(id)) SendSetPin(id, false);
        }
        foreach (var id in outcome.DeletedTaskIds)
            if (IsPinnedForMe(id)) SendSetPin(id, false);

        bool rangeOrMove = outcome.AffectedTaskIds.Count > 1
            || outcome.DeletedTaskIds.Count > 0
            || (nowDone && modSystem.MySettings.CompletionPolicy is ScribeCompletionPolicy.Delete
                or ScribeCompletionPolicy.Sink or ScribeCompletionPolicy.UnpinSink);
        if (rangeOrMove)
        {
            RebuildBody();
            return;
        }

        // Keep/Unpin (and un-check): no rebuild happened, so re-grant focus to the row that held it. Uses
        // RequestFocus directly (not FocusEditorRow) so clicking a checkbox on an off-screen row doesn't scroll.
        if (focusedEditIndex is { } held && held < editorFocusNodes.Count)
        {
            editorFocusNodes[held].RequestFocus();
        }
    }

    /// <summary>Per-row delete: removes the block from scratch and rebuilds, adjusting <see cref="focusedEditIndex"/>
    /// so nothing focuses a removed row. An empty document shows the empty-state hint.</summary>
    private void DeleteEditorBlock(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        TraceScroll($"delete {index}");

        Guid taskId = scratch.Blocks[index].TaskId;
        var deleted = ScribeCompletion.ApplyDelete(scratch, taskId, modSystem.MySettings.SubtaskBehavior);
        if (deleted.Count == 0) return; // deletion refused: nothing removed, no departure
        int removedCount = deleted.Count;

        isDirty = true;

        // Fix up the focused index: cleared if its row was removed, shifted if it sat after the range.
        if (focusedEditIndex is { } f)
        {
            if (f >= index && f < index + removedCount) focusedEditIndex = null;
            else if (f >= index + removedCount) focusedEditIndex = f - removedCount;
        }

        // The delete button press already blurred the field (it isn't IFocusable), so re-request focus. If the
        // focused row was deleted, land on the row above (or the new first row); an emptied document gets no focus.
        // The target is always a REUSED row whose field skips mount-only autoFocus, so use pendingFocusRow, not
        // autoFocusRowOnRebuild. Rows below remount (losing caret position) when the departing ghost retires.
        if (scratch.Blocks.Count > 0)
        {
            int target = focusedEditIndex ?? Math.Max(0, index - 1);
            target = Math.Min(target, scratch.Blocks.Count - 1);
            focusedEditIndex = target;
            pendingFocusRow = target;
        }
        else
        {
            focusedEditIndex = null;
        }

        SyncFocusNodesToScratch();
        pendingEnsureVisible = false;
        // The re-clamp to the shrunk extent is DEFERRED until the collapse completes (the ghost still occupies
        // height until then); OnDepartureSettled -> RequestClampToExtent does it.
        RebuildBody();
    }

    /// <summary>Per-row pin toggle (any block kind). Pinning is per-player state, not document state: sends a
    /// lock-free <see cref="ScribeSetPinMessage"/> and never touches scratch, the edit lock, or autosave. The
    /// server re-push lands in <see cref="OnMyPinsChanged"/>.</summary>
    private void TogglePinnedEditorTask(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        var block = scratch.Blocks[index];
        // Tablet tier allows 1 pin: pinning at the cap swaps out the older pin; unpinning is always allowed.
        TogglePinWithPolicy(block.TaskId);
    }

    /// <summary>Fire-and-forget pin/unpin by stable identity (DocId + TaskId; no block position). Also records the
    /// same snapshot as an optimistic overlay on <see cref="modSystem"/> so every surface updates at once, since in
    /// singleplayer the round-trip stalls while the Handbook holds the embedded server paused.</summary>
    private void SendSetPin(Guid taskId, bool pinned)
    {
        var block = host.Document.FindByTaskId(taskId);
        var insertEdge = ScribePlayerSettings.NormalizePinInsert(modSystem.MySettings.PinInsert);

        ScribePinnedRef? snapshot = !pinned ? null : new ScribePinnedRef
        {
            OwnerDocId = host.Document.DocId,
            TaskId = taskId,
            PinnedAtTotalHours = capi.World.Calendar.TotalHours,
            LastKnownText = block?.Text ?? "",
            LastKnownDone = block?.Done ?? false,
            Kind = block?.Kind ?? Scribe.Core.ScribeBlockKind.Task,
            LinkTarget = block?.LinkTarget,
            TargetItemCode = block?.TargetItemCode,
            TargetQuantity = block?.TargetQuantity ?? 1,
            CurrentQuantity = block?.CurrentQuantity ?? 0,
            LinkLabel = block?.LinkLabel,
            Depth = block?.Depth ?? 0,
        };
        modSystem.SetOptimisticPin(host.Document.DocId, taskId, snapshot, host.Document, insertEdge);

        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeSetPinMessage
        {
            DocId = host.Document.DocId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Pinned = pinned,
            // Client-side snapshot so the server can record it even when the host isn't registered server-side
            // (Notebook items); includes kind and Link target.
            SnapshotText = block?.Text ?? "",
            SnapshotDone = block?.Done ?? false,
            SnapshotKind = (byte)(block?.Kind ?? Scribe.Core.ScribeBlockKind.Task),
            SnapshotLinkTarget = block?.LinkTarget,
            // Tracker target item + counts, for item hosts the server can't resolve.
            SnapshotTargetItemCode = block?.TargetItemCode,
            SnapshotTargetQuantity = block?.TargetQuantity ?? 1,
            SnapshotCurrentQuantity = block?.CurrentQuantity ?? 0,
            // A guide-page Link's display title (a "page:" target has no item to resolve a name from).
            SnapshotLinkLabel = block?.LinkLabel,
            // Subtask depth.
            SnapshotDepth = block?.Depth ?? 0,
            // Where an unrelated new pin lands (Top or Bottom); ignored on unpin.
            PinInsert = (byte)insertEdge,
        });
    }

    /// <summary>Drag-reorder drop: moves the block (and its subtask cluster) from <paramref name="from"/> to
    /// <paramref name="to"/> in scratch and rebuilds. A move to the same index is a no-op.
    ///
    /// <para><paramref name="anchorViewport"/>: false (drag) chases the moved row into view; true (Sink completion)
    /// holds the viewport still.</para>
    ///
    /// <para><paramref name="preserveFocusedRow"/>: false grabs focus onto the moved row; true (external move, e.g.
    /// a HUD Sink arriving via <see cref="RefreshReadView"/>) keeps focus on the row being edited, recomputing its
    /// index across the shift. Rows below the move remount with text preserved but caret position lost.</para></summary>
    private void ReorderEditorBlock(int from, int to, bool anchorViewport = false, bool preserveFocusedRow = false)
    {
        if (scratch is null) return;
        TraceScroll($"reorder {from}->{to} anchor={anchorViewport} preserveFocus={preserveFocusedRow}");

        int start = from;
        int end = from + 1;
        if (from >= 0 && from < scratch.Blocks.Count && scratch.Blocks[from].Depth == 0)
            end = scratch.OwnedRun(from).End; // cluster [from, runEnd); empty run → from+1

        bool dropOnCluster = to >= start && to < end;
        // A depth-0 cluster may only land on a depth-0 row, a depth-1 leaf on depth-1 (shared with the grip's
        // drop-target arrow via ScribeReorderValidity). dropOnCluster counts as valid (a no-op), so a false result
        // means a cross-depth drop outside the dragged row's own cluster.
        var depths = scratch.Blocks.Select(b => b.Depth).ToList();
        bool crossDepth = !ScribeReorderValidity.IsValidDropTarget(depths, start, end, to);
        if (from == to || dropOnCluster || crossDepth)
        {
            // No edit, but the grip press already blurred the focused field (the grip isn't IFocusable), so re-home
            // the caret; no rebuild needed.
            if (focusedEditIndex is { } held && held < editorFocusNodes.Count) FocusEditorRow(held);
            return;
        }

        Guid? preservedId = null;
        if (preserveFocusedRow && focusedEditIndex is { } pf && pf < scratch.Blocks.Count)
            preservedId = scratch.Blocks[pf].TaskId;

        // Dropping forward onto a parent with children lands after its whole cluster.
        int destination = ScribeReorderValidity.ResolveDestination(depths, start, to);
        int len = end - start;
        bool ok = len == 1
            ? scratch.MoveBlock(from, destination)
            : scratch.MoveRange(start, end, destination);
        if (!ok) return;

        isDirty = true;
        int newStart = destination < start ? destination : destination - len + 1;

        int? previousFocus = focusedEditIndex;
        int focusTarget;
        if (preserveFocusedRow)
        {
            int preserved = preservedId is { } id ? scratch.IndexOf(id) : -1;
            focusTarget = preserved;
        }
        else
        {
            focusTarget = newStart;
        }
        focusedEditIndex = focusTarget >= 0 ? focusTarget : (int?)null;
        SyncFocusNodesToScratch();
        // Re-home focus via pendingFocusRow rather than the field's mount-only autoFocus: a reorder shifts every slot
        // between from and to, so rows may remount. Skipped on an external move whose edited row kept its slot,
        // since re-requesting would reset its caret to the end.
        if (preserveFocusedRow)
        {
            if (focusedEditIndex is { } nt && nt != previousFocus) pendingFocusRow = nt;
        }
        else
        {
            pendingFocusRow = newStart;
        }
        if (anchorViewport)
        {
            // Sink: reconcile preserves the shared controller's scroll offset, so the viewport holds still.
        }
        else
        {
            // Drag: follow the moved row into view.
            pendingEnsureVisible = true;
        }
        RebuildBody();
    }

    /// <summary>Footer add-picker action: inserts an empty block of <paramref name="kind"/> at the New Task Insert
    /// edge and rebuilds with the new row auto-focused. A row abandoned empty is removed on blur (see
    /// <see cref="OnRowBlurred"/>). Kinds and their add delegates come from <see cref="ScribeAddKinds"/>.</summary>
    private void OnClickAdd(ScribeAddKind kind)
    {
        if (scratch is null) return;
        // Cap check runs first so a footer tap on Add Link / Tracker / Craft at the cap shows the "full" notice (the
        // picker dims at the cap but stays clickable). Below the cap, item-bound kinds can't be created from a bare
        // footer click (they need a Handbook-supplied target), so the click is a guide gesture; nothing is mutated.
        if (!CanAddTaskUnderPolicy()) { NotifyTabletFull(); return; }
        if (kind.RequiresItemContext) { DispatchItemKindGuide(kind); return; }
        if (focusedEditIndex is { } leaving) NormalizeRowOnCommit(leaving);
        // Task/Note ignore the item code argument.
        int at = NewTaskInsertIndex();
        if (!kind.Add(scratch, null, at)) return;
        isDirty = true;
        SyncFocusNodesToScratch();
        // The new row mounts at `at`, so its mount-only autoFocus fires. See EditorInsertTaskBelow.
        autoFocusRowOnRebuild = at;
        focusedEditIndex = at;
        pendingEnsureVisible = true;
        RebuildBody();
    }

    /// <summary>Footer add-picker action for a chosen quest: inserts a quest Link at the New Task Insert edge like
    /// <see cref="OnClickAdd"/>, without auto-focus (a Link has no editable text). Counts against the task cap.</summary>
    private void OnClickAddQuestLink(ScribeQuestCatalogEntry entry)
    {
        if (scratch is null) return;
        if (!CanAddTaskUnderPolicy()) { NotifyTabletFull(); return; }
        if (focusedEditIndex is { } leaving) NormalizeRowOnCommit(leaving);
        int at = NewTaskInsertIndex();
        if (!scratch.InsertQuestLink(at, entry.Source, entry.QuestCode, entry.Title, entry.Description)) return;
        if (entry.Source == ScribeQuestSource.ProgressionFramework
            && modSystem.TryGetPfObjectiveDefs(entry.QuestCode, out var pfObjectives) && pfObjectives.Count > 0)
        {
            var parentTaskId = scratch.Blocks[at].TaskId;
            modSystem.TryGetPfObjectiveProgress(entry.QuestCode, out var progressByCode);
            scratch.ReconcileQuestObjectives(parentTaskId, pfObjectives
                .Select(o => (o.Code, o.ItemCode, (string?)o.Label, o.Required))
                .ToList(), createMissing: true);
            foreach (var obj in pfObjectives)
            {
                if (!progressByCode.TryGetValue(obj.Code, out int progress)) continue;
                var child = scratch.Blocks.FirstOrDefault(b => b.IsQuestObjective
                    && string.Equals(b.LinkTarget, obj.Code, StringComparison.Ordinal));
                if (child is not null) scratch.SetQuestObjectiveProgress(child.TaskId, progress);
            }
        }
        else if (entry.Source == ScribeQuestSource.VsQuest && entry.Objectives.Count > 0)
        {
            var parentTaskId = scratch.Blocks[at].TaskId;
            var staticObjectives = ScribeQuestCatalog.BuildStaticObjectives(capi, entry.Objectives);
            scratch.ReconcileQuestObjectives(parentTaskId, staticObjectives, createMissing: true);
        }
        isDirty = true;
        SyncFocusNodesToScratch();
        pendingEnsureVisible = true;
        RebuildBody();
    }

    /// <summary>The Quest Link picker's catalog (all installed backends merged), read once per dialog session since
    /// it can't change while the game runs. Empty, and the picker option hidden, when no backend has entries.</summary>
    private IReadOnlyList<ScribeQuestCatalogEntry>? questCatalogCache;
    private IReadOnlyList<ScribeQuestCatalogEntry> QuestCatalogForPicker
        => questCatalogCache ??= ScribeQuestCatalog.ReadCatalog(capi)
            .Concat(ScribeProgressionFrameworkQuestCatalog.ReadCatalog(capi).Select(e => e.ToPickerEntry()))
            .OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The picker's candidate list: <see cref="QuestCatalogForPicker"/> narrowed to quests the player has
    /// started this session, re-filtered on every read.</summary>
    private IReadOnlyList<ScribeQuestCatalogEntry> StartedQuestCatalogForPicker
        => QuestCatalogForPicker.Where(e => modSystem.HasStartedQuest(e.QuestCode)).ToList();

    /// <summary>Requests focus on row <paramref name="index"/>'s node (always live, since the editor doesn't
    /// virtualize) and schedules a scroll-into-view.</summary>
    private void FocusEditorRow(int index)
    {
        if (index < 0 || index >= editorFocusNodes.Count) return;
        editorFocusNodes[index].RequestFocus();
        focusedEditIndex = index;
        pendingEnsureVisible = true;
    }

    /// <summary>Cmd/Ctrl+Up: commit the current row and jump to the first row, caret at its start.</summary>
    private void EditorJumpToFirstRow(int index) => JumpToEditorEdge(index, toFirst: true);

    /// <summary>Cmd/Ctrl+Down: jump to the last row, caret at its end.</summary>
    private void EditorJumpToLastRow(int index) => JumpToEditorEdge(index, toFirst: false);

    /// <summary>Shared first-/last-row jump: commits the row being left, focuses the edge row, and snaps its caret
    /// to the document edge (start for first, end for last). <see cref="FocusEditorRow"/> arms
    /// <see cref="pendingEnsureVisible"/> so the edge scrolls into view.</summary>
    private void JumpToEditorEdge(int index, bool toFirst)
    {
        if (scratch is null || scratch.Blocks.Count == 0) return;
        NormalizeRowOnCommit(index);
        FlushIfDirty();
        int target = toFirst ? 0 : scratch.Blocks.Count - 1;
        FocusEditorRow(target);
        // The field's caret otherwise persists wherever it last sat (focus-gain never resets it).
        ResolveEditorFieldState(target)?.PlaceCaretAtEdge(atStart: toFirst);
    }

    /// <summary>Resolves the live <see cref="ScribeMultilineFieldState"/> for row <paramref name="index"/> via its
    /// focus node's owning element (set in the field's <c>InitState</c>). Null if not mounted or not a multiline
    /// field. Drives imperative caret placement that has no declarative prop.</summary>
    private ScribeMultilineFieldState? ResolveEditorFieldState(int index)
    {
        if (index < 0 || index >= editorFocusNodes.Count) return null;
        return (editorFocusNodes[index].Owner as StatefulElement)?.State as ScribeMultilineFieldState;
    }

    /// <summary>Commit-time normalization: strips trailing blank lines and whitespace, preserving interior
    /// newlines. Applied only at genuine row-commit sites, never per keystroke or
    /// autosave tick, which would fight a player who just pressed Shift+Enter. No leading trim.</summary>
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

    /// <summary>True when the focused row is an empty task or note; the autosave tick uses this to avoid
    /// serializing the transient empty row.</summary>
    private bool FocusedRowIsEmptyBlock()
    {
        if (scratch is null || focusedEditIndex is not { } idx || idx < 0 || idx >= scratch.Blocks.Count) return false;
        return IsAbandonableEmptyBlock(scratch.Blocks[idx]);
    }

    /// <summary>True for a Task or Note whose text is blank. Tracker and Link rows are excluded: they legitimately
    /// carry empty <see cref="ScribeBlock.Text"/>, so the sweep must not purge a freshly-added one. Shared by
    /// <see cref="OnRowBlurred"/>, <see cref="FocusedRowIsEmptyBlock"/>, and <see cref="PurgeEmptyRowsFromScratch"/>.</summary>
    private static bool IsAbandonableEmptyBlock(ScribeBlock b) =>
        (b.IsTask || b.Kind == ScribeBlockKind.Text) && string.IsNullOrWhiteSpace(b.Text);

    /// <summary>Removes every empty/whitespace-only task or note from scratch, marking dirty if any were removed.
    /// Called at terminal commit paths (switch-to-read, close); no rebuild, since the caller tears down or rebuilds.</summary>
    private void PurgeEmptyRowsFromScratch()
    {
        if (scratch is null) return;
        for (int i = scratch.Blocks.Count - 1; i >= 0; i--)
        {
            if (IsAbandonableEmptyBlock(scratch.Blocks[i]) && scratch.DeleteBlock(i))
            {
                isDirty = true;
            }
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

    /// <summary>Autosave tick. Skips the flush while the focused row is a transiently-empty task or note, so an
    /// empty block isn't round-tripped a beat before its blur removes it; other dirty edits flush next tick.</summary>
    private void OnAutosaveTick(float deltaTime)
    {
        if (FocusedRowIsEmptyBlock()) return;
        FlushIfDirty();
    }

    /// <summary>Commits the title input (if active): trims, defaults empty to host's default title,
    /// clamps to <see cref="ScribeDocument.MaxTitleLength"/> chars, writes to <see cref="scratch"/>,
    /// resets the editing flag, and flushes. Safe to call when not editing (no-op) or when
    /// <see cref="scratch"/> is null (no-op).</summary>
    private void CommitTitleIfEditing()
    {
        if (!_isTitleEditing || scratch is null) return;
        var raw = _titleController?.Text ?? "";
        var trimmed = raw.Trim();
        var final = string.IsNullOrEmpty(trimmed)
            ? host.DefaultDocumentTitle
            : trimmed[..Math.Min(trimmed.Length, ScribeDocument.MaxTitleLength)];
        scratch.Title = final;
        _isTitleEditing = false;
        isDirty = true;
        FlushIfDirty();
    }

    /// <summary>FocusNode listener for the title input: commits on focus loss and rebuilds only when the editing
    /// state changed. The ForceRebuild is DEFERRED to OnRenderGUI via <see cref="_pendingTitleEditRebuild"/>: this
    /// fires inside pointer dispatch, and a synchronous rebuild there orphans the clicked button and NPEs in
    /// ButtonState.PlaySound.</summary>
    private void OnTitleFocusChanged()
    {
        if (!(_titleFocusNode?.HasFocus ?? true) && _isTitleEditing)
        {
            CommitTitleIfEditing();  // sets _isTitleEditing = false
            if (IsOpened()) _pendingTitleEditRebuild = true;
        }
    }

    protected void FlushIfDirty()
    {
        if (!isDirty || scratch is null) return;

        var bytes = ScribeDocumentCodec.Serialize(scratch);
        SendFlushPacket(bytes);
        isDirty = false;

        // Un-aliased fresh copy: scratch keeps mutating while editing continues.
        if (ScribeDocumentCodec.TryDeserialize(bytes, out var copy) && copy is not null)
        {
            host.ApplyLocalOptimisticEdit(copy);
        }
    }

    /// <summary>Builds the item-surface save packet shared by Notebook and Tablet. Stamps the host's bound slot
    /// identity when available so the server writes to that exact slot rather than the active hand; otherwise the
    /// server falls back to the active-hand slot.</summary>
    protected ScribeNotebookSaveMessage BuildItemSavePacket(byte[] documentBytes)
    {
        var msg = new ScribeNotebookSaveMessage
        {
            DocIdBytes = host.Document.DocId.ToByteArray(),
            DocumentBytes = documentBytes,
        };
        if (host is NotebookHost nb && nb.SlotInventoryId is { } invId && nb.SlotId >= 0)
        {
            msg.TargetInventoryId = invId;
            msg.TargetSlotId = nb.SlotId;
        }
        return msg;
    }

    /// <summary>Sends the serialized document bytes to the server for authoritative storage.
    /// Override in subclasses to use a different packet type (e.g. <see cref="ScribeNotebookSaveMessage"/>
    /// for the Notebook vs. <see cref="ScribeEditDocumentMessage"/> for the Lectern).</summary>
    protected virtual void SendFlushPacket(byte[] documentBytes)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeEditDocumentMessage
        {
            DocIdBytes = host.Document.DocId.ToByteArray(),
            DocumentBytes = documentBytes,
        });
    }

    // ---------------- Focus-node lifecycle ----------------

    /// <summary>Resizes <see cref="editorFocusNodes"/> to one node per scratch block, keeping existing nodes so
    /// other rows' focus survives an add. Each node's listener tracks the focused row and commits the row left on
    /// row-to-row moves; click-away-to-nothing is covered by the autosave tick and close/switch commit.</summary>
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

    /// <summary>Field focus nodes Tab / Shift+Tab may visit in the current view, in on-screen row order (consulted
    /// by <see cref="ScribeFieldOnlyTraversalPolicy"/>). Editor view: editor rows; Pinned view: pin rows in
    /// <see cref="OrderedPinsForDisplay"/> order; empty elsewhere. Only mounted nodes (live <c>Owner</c>) are
    /// returned. Rebuilt on every Tab press.</summary>
    private IReadOnlyList<FocusNode> EditorFieldTraversalNodes()
    {
        var result = new List<FocusNode>();
        switch (viewMode)
        {
            case ScribeLecternView.Editor:
                foreach (var node in editorFocusNodes)
                    if (node.Owner is not null) result.Add(node);
                break;
            case ScribeLecternView.Pinned:
                foreach (var pin in OrderedPinsForDisplay())
                    if (pinFocusNodes.TryGetValue(pin.TaskId, out var node) && node.Owner is not null)
                        result.Add(node);
                break;
        }
        return result;
    }
}
