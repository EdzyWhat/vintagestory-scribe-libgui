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

    /// <summary>Enter: commit the focused row, then insert a new EMPTY task directly beneath it and focus
    /// it (so the player types straight into the new row). Rebuilds because the row set changed.
    ///
    /// <para>Q5 (add-empty-task-lifecycle): Enter on a row that is itself empty/whitespace-only is a
    /// no-op on the row set — it does NOT stack a second empty task. Without this, empty-init would let
    /// Enter-Enter-Enter spam a column of empty rows; instead the caret just stays where it is.</para></summary>
    private void EditorInsertTaskBelow(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        TraceScroll($"insert-below {index}");

        // Q5: don't stack another empty task beneath an already-empty task row.
        var current = scratch.Blocks[index];
        if (current.IsTask && string.IsNullOrWhiteSpace(current.Text)) return;

        NormalizeRowOnCommit(index);
        FlushIfDirty();

        int insertAt = index + 1;
        if (scratch.InsertTask(insertAt, ""))
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

    /// <summary>A row's editable field lost focus (add-empty-task-lifecycle D3). If the block at
    /// <paramref name="index"/> is a TASK whose text is empty/whitespace-only, schedule its removal —
    /// this is the self-destruct that turns "clear the text and click away" (or Tab off an untyped new
    /// row) into a keyboard-only delete. A text section may be empty and is never auto-removed.
    ///
    /// <para>The actual delete is DEFERRED to <see cref="OnRenderGUI"/> (<see cref="pendingEmptyRowRemoval"/>)
    /// rather than run here: blur fires from inside the field's focus-notification, and on a row→row move
    /// the old node blurs mid-way through <c>FocusManager.RequestFocus</c> — deleting now (which disposes +
    /// recreates focus nodes and rebuilds the tree) would strand the in-flight focus of the row being
    /// entered. Reading the block from live scratch by index, and re-checking emptiness at removal time,
    /// keeps this idempotent with the <see cref="OnRowFocusChanged"/> commit that also fires on a
    /// row→row move.</para></summary>
    private void OnRowBlurred(int index)
    {
        if (!isEditorMode || scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        var block = scratch.Blocks[index];
        if (!block.IsTask || !string.IsNullOrWhiteSpace(block.Text)) return;
        pendingEmptyRowRemoval = index;
    }

    /// <summary>Editor checkbox toggle — completes a task under the player's completion policy so the
    /// editor behaves IDENTICALLY to the read/pinned views and the HUD (scribe-lectern-view-consistency
    /// §4). The row flips its own checkbox optimistically before this runs.
    ///
    /// <para><b>Why the policy is enacted in <c>scratch</c>, not via <see cref="ScribeCompleteTaskMessage"/>
    /// (design Decision 2 fallback, chosen):</b> the editor holds the edit lock and autosaves the WHOLE
    /// scratch document authoritatively (<see cref="FlushIfDirty"/> → lock-gated <c>ApplyEdit</c>). A
    /// lock-free completion message writes the shared document out-of-band, so the very next whole-doc
    /// flush would clobber its Sink reorder / Delete. Enacting the policy directly in scratch keeps
    /// everything on the one authoritative path the editor already owns, and the flush's
    /// <c>ReconcileActorPins</c> then carries the acting player's pin snapshot (and drops a pin whose task
    /// was deleted) for free — so the end state (done + Sink/Delete + pin effect) matches every other
    /// surface. The one pin effect the flush does NOT cover is <c>Unpin</c> (the task survives, so the
    /// snapshot reconcile keeps the pin); that is sent explicitly via the identity pin path.</para>
    ///
    /// <para>Focus preservation (8.5 "toggling a checkbox should NOT disturb the caret in another focused
    /// row"): clicking the checkbox blurs whatever field was focused via LibGUI's
    /// <c>DispatchPointerDown</c> focus-clear (same root cause as the delete/pin/reorder controls —
    /// a05caret1). (In gui@3.1.0 the checkbox IS focusable — <c>CheckboxState : FocusableState</c> — but it's
    /// deliberately excluded from Tab traversal by <see cref="ScribeFieldOnlyTraversalPolicy"/>, so it's
    /// still mouse-only from the keyboard's point of view.) Keep/Unpin do no rebuild (the checkbox flips
    /// optimistically in its own State, leaving
    /// the focused field's State mounted), so re-request focus directly on the row that held it; the
    /// Sink/Delete branches delegate to <see cref="ReorderEditorBlock"/>/<see cref="DeleteEditorBlock"/>,
    /// which own their own rebuild + focus fix-up.</para></summary>
    private void ToggleEditorTask(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        var block = scratch.Blocks[index];
        if (!block.IsTask) return;

        bool nowDone = !block.Done;
        Guid taskId = block.TaskId;

        if (!scratch.ToggleTask(index)) return;
        isDirty = true;
        TraceScroll($"complete {index} done={nowDone} policy={modSystem.MySettings.CompletionPolicy}");

        // Apply the policy only on a transition INTO done — unchecking never sinks/deletes/unpins.
        if (nowDone)
        {
            switch (modSystem.MySettings.CompletionPolicy)
            {
                case ScribeCompletionPolicy.Delete:
                    // Drop the row (with the collapse animation + focus fix-up); the flush's
                    // ReconcileActorPins then removes any pin on the now-gone task. DeleteEditorBlock
                    // owns the rebuild, so return without the focus re-home below.
                    DeleteEditorBlock(index);
                    return;
                case ScribeCompletionPolicy.Sink:
                    // Move the (now-done) task to the document bottom — a real reorder of the shared doc
                    // once flushed, matching every other surface. ReorderEditorBlock owns the rebuild and
                    // keeps the moved row focused; a task already last is a safe no-op there. anchorViewport:
                    // true holds the scroll where it was (design: Sink completion shouldn't yank the viewport
                    // to the bottom the row sinks to — see the ReorderEditorBlock remarks + Phase 2 trace).
                    ReorderEditorBlock(index, scratch.Blocks.Count - 1, anchorViewport: true);
                    return;
                case ScribeCompletionPolicy.Unpin:
                    // The task stays, so the flush's snapshot reconcile keeps the pin — unpin explicitly.
                    if (IsPinnedForMe(taskId)) SendSetPin(taskId, false);
                    break;
                case ScribeCompletionPolicy.UnpinSink:
                    // Unpin + sink: move the task to the bottom (like Sink), AND unpin it (like Unpin).
                    ReorderEditorBlock(index, scratch.Blocks.Count - 1, anchorViewport: true);
                    if (IsPinnedForMe(taskId)) SendSetPin(taskId, false);
                    return;
                case ScribeCompletionPolicy.Keep:
                default:
                    break;
            }
        }

        // Keep/Unpin (and any un-check): no rebuild happened, so re-home the caret to the row that held
        // focus (a05caret1). Re-request focus WITHOUT the scroll-into-view that FocusEditorRow schedules:
        // clicking a checkbox on some other (possibly off-screen) row must not yank the viewport to the
        // still-focused edit row. That pendingEnsureVisible was the "unchecking a task under Keep/Sink
        // jumps the view" race (trace: complete N done=False → ensure-visible → changed to the focused
        // row's offset). The caret is already where the player left it; only the focus token needs re-
        // granting after DispatchPointerDown's press-clear, so RequestFocus alone is right.
        if (focusedEditIndex is { } held && held < editorFocusNodes.Count)
        {
            editorFocusNodes[held].RequestFocus();
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
        TraceScroll($"delete {index}");

        // Snapshot the row BEFORE removing it, so it can keep rendering as a static, non-interactive ghost
        // while it collapses its height to zero (scribe-list-collapse). The scratch deletion still happens
        // immediately below, so the data model + autosave stay correct at once; only the visual removal is
        // deferred until the collapse completes. Its DISPLAY index (live scratch index offset by any rows
        // already departing above it) lets the ghost collapse in place rather than jumping.
        var deleted = scratch.Blocks[index];
        var snapshot = new ScribeEditRowData(index, deleted.IsTask, deleted.Done,
            IsPinnedForMe(deleted.TaskId), deleted.TaskId, deleted.Text);
        int displayIndex = index + departingEditorRows.Values.Count(d => d.Index <= index);
        departingEditorRows[deleted.TaskId] = (snapshot, displayIndex);

        if (!scratch.DeleteBlock(index))
        {
            departingEditorRows.Remove(deleted.TaskId); // deletion refused: don't leave a ghost behind
            return;
        }

        isDirty = true;

        // Fix up the focused index across the deletion: the focused row is gone (clear), or sat after
        // the deleted one (shift up by one), or before it (unchanged).
        if (focusedEditIndex is { } f)
        {
            if (f == index) focusedEditIndex = null;
            else if (f > index) focusedEditIndex = f - 1;
        }

        // Re-home the caret after the rebuild. Pressing the (non-IFocusable) delete button already blurred
        // the field via LibGUI's DispatchPointerDown focus-clear, so even a surviving edited row needs its
        // focus re-requested — nothing re-grants it otherwise, which is why the caret vanished (a05caret1).
        // If we deleted the focused row, land on the neighbor per design Q1: the row above (index-1), or the
        // new first row when the top row was deleted; an emptied document gets no focus (empty-state hint).
        if (scratch.Blocks.Count > 0)
        {
            int target = focusedEditIndex ?? Math.Max(0, index - 1);
            target = Math.Min(target, scratch.Blocks.Count - 1);
            focusedEditIndex = target;
            autoFocusRowOnRebuild = target;
        }
        else
        {
            focusedEditIndex = null;
        }

        SyncFocusNodesToScratch();
        pendingEnsureVisible = false;
        // The re-clamp to the shrunk extent is DEFERRED to when the collapse completes (see
        // OnEditorRowCollapsed): during the collapse the ghost still occupies (shrinking) height, so the
        // content extent doesn't actually shrink until the row reaches zero — clamping now would fight the
        // collapsing height (scribe-list-collapse). LibGUI's own clamp ignores a sub-50px overshoot, so the
        // deferred clamp is still needed once the row is gone (see pendingClampToExtent).
        ForceRebuild();
    }

    /// <summary>A deleted editor row finished collapsing to zero height (scribe-list-collapse): retire its
    /// ghost and, now that the content is genuinely shorter, re-clamp the scroll extent. Deferred out of the
    /// animation callback via <see cref="needsEditorCollapseCleanup"/> so we don't unmount + rebuild the tree
    /// re-entrantly from inside the ticker pump.
    ///
    /// <para>Scroll preservation (Phase 2 trace: delete was jumping the viewport to the TOP): the
    /// <see cref="needsEditorCollapseCleanup"/> <see cref="ForceRebuild"/> remounts the editor's
    /// <c>SingleChildScrollView</c>, which re-lays-out and (via LibGUI's <c>ClampOffset</c> against a
    /// transiently-zero content height) resets the offset to 0. The <see cref="RequestClampToExtent"/> loop
    /// can only clamp DOWN, so it can't recover from a reset-to-0. Capture the current offset so the
    /// <see cref="OnRenderGUI"/> restore loop re-applies it across that rebuild — the same "hold the offset"
    /// mechanism Pin uses. Restoring the pre-collapse offset and letting the natural clamp reduce it to the
    /// now-smaller max lands the viewport at the shortened list's bottom (the correct resting spot), not 0.</para></summary>
    private void OnEditorRowCollapsed(Guid taskId)
    {
        if (!departingEditorRows.Remove(taskId)) return;
        editorCollapseRegistry.Release(taskId.ToString("N"));
        CaptureScrollForRestore();
        RequestClampToExtent();
        needsEditorCollapseCleanup = true;
    }

    /// <summary>Per-row pin toggle (task rows only; the pin control is absent on text-section rows).
    /// Pinning is a per-player action now, NOT document state: fire a lock-free
    /// <see cref="ScribeSetPinMessage"/> keyed by the task's stable id, toggled against the client's
    /// own pin cache. It never touches the scratch document, the edit lock, or autosave. The server
    /// re-pushes this player's set, which lands in <see cref="OnMyPinsChanged"/> and repaints the row.</summary>
    private void TogglePinnedEditorTask(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        var block = scratch.Blocks[index];
        if (!block.IsTask) return; // no pin control on a text section
        SendSetPin(block.TaskId, !IsPinnedForMe(block.TaskId));
    }

    /// <summary>Fire-and-forget a pin/unpin for a task by stable identity. The document's DocId plus
    /// the task's TaskId fully address the pin; no block position is sent (so the same shape works
    /// from a future HUD). The server derives the snapshot from its own document.</summary>
    private void SendSetPin(Guid taskId, bool pinned)
    {
        var block = host.Document.FindByTaskId(taskId);
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeSetPinMessage
        {
            DocId = host.Document.DocId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Pinned = pinned,
            // Supply a client-side snapshot so the server can record it even when the host is not
            // registered server-side (e.g. Notebook items — only the client holds the document).
            SnapshotText = block?.Text ?? "",
            SnapshotDone = block?.Done ?? false,
        });
    }

    /// <summary>Drag-reorder drop: move the block from <paramref name="from"/> to <paramref name="to"/>
    /// in the scratch document and rebuild. A move-to-same index is a safe no-op
    /// (<see cref="ScribeDocument.MoveBlock"/> returns true without changing anything, so no edit is
    /// sent). Keeps the moved row focused across the rebuild.
    ///
    /// <para><paramref name="anchorViewport"/> chooses what the scroll offset does across the rebuild.
    /// A drag-reorder (false) chases the moved row with <see cref="pendingEnsureVisible"/> — the player
    /// dragged it, so following it into view is expected. A <b>Sink completion</b> (true) instead HOLDS
    /// the viewport where it was: completing a mid-list task should not yank the viewport to the bottom
    /// where the row sinks to (Phase 2 trace: Sink was scrolling to the end). We capture the offset before
    /// the rebuild and let the <see cref="OnRenderGUI"/> restore loop re-apply it — the same "hold still"
    /// mechanism Pin uses via <see cref="OnMyPinsChanged"/>. Note a <c>ForceRebuild</c> resets the editor's
    /// <c>SingleChildScrollView</c> offset to 0, so the capture+restore is required, not optional.</para></summary>
    private void ReorderEditorBlock(int from, int to, bool anchorViewport = false)
    {
        if (scratch is null) return;
        TraceScroll($"reorder {from}->{to} anchor={anchorViewport}");
        if (from == to)
        {
            // Released in place (or a grip click that never dragged): no edit — but the grip press already
            // blurred the focused field via LibGUI's DispatchPointerDown focus-clear (the grip isn't
            // IFocusable), so re-home the caret to the row that was being edited or nothing else will
            // (a05caret1). No rebuild is needed to move the doc; RequestFocus alone re-grants focus.
            if (focusedEditIndex is { } held && held < editorFocusNodes.Count) FocusEditorRow(held);
            return;
        }
        if (!scratch.MoveBlock(from, to)) return;

        isDirty = true;
        focusedEditIndex = to;
        SyncFocusNodesToScratch();
        autoFocusRowOnRebuild = to;
        if (anchorViewport)
        {
            // Sink: hold the viewport still across the rebuild instead of scrolling to the sunk row.
            CaptureScrollForRestore();
        }
        else
        {
            // Drag: follow the moved row into view.
            pendingEnsureVisible = true;
        }
        ForceRebuild();
    }

    /// <summary>"Add task" button: append an EMPTY task, grow the focus-node list, and rebuild with the
    /// new row auto-focused so the player types straight into it (no boilerplate to clear —
    /// add-empty-task-lifecycle). The field renders a dimmed "New task…" ghost hint while empty; if the
    /// row is abandoned without typing, its blur removes it (see <see cref="OnRowBlurred"/>).</summary>
    private void OnClickAddTask()
    {
        if (scratch is null) return;
        if (focusedEditIndex is { } leaving) NormalizeRowOnCommit(leaving);
        scratch.AddTask("");
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

    /// <summary>True when the currently-focused editor row is a task whose text is empty/whitespace-only —
    /// a transiently-empty row the player is (or just was) editing (add-empty-task-lifecycle). The autosave
    /// tick uses this to avoid serializing that transient empty task; leaving the row / closing removes it.</summary>
    private bool FocusedRowIsEmptyTask()
    {
        if (scratch is null || focusedEditIndex is not { } idx || idx < 0 || idx >= scratch.Blocks.Count) return false;
        var block = scratch.Blocks[idx];
        return block.IsTask && string.IsNullOrWhiteSpace(block.Text);
    }

    /// <summary>Removes every empty/whitespace-only TASK block from the scratch document, marking dirty if
    /// any were removed (add-empty-task-lifecycle D5). Text sections are left untouched — an empty note is
    /// valid. Called at the terminal commit paths (switch-to-read, close) so an abandoned empty task the
    /// blur-removal hadn't yet swept is never flushed or shown in the read view. No rebuild/collapse — the
    /// caller tears the editor down or rebuilds into the read view immediately after.</summary>
    private void PurgeEmptyTasksFromScratch()
    {
        if (scratch is null) return;
        for (int i = scratch.Blocks.Count - 1; i >= 0; i--)
        {
            var block = scratch.Blocks[i];
            if (block.IsTask && string.IsNullOrWhiteSpace(block.Text) && scratch.DeleteBlock(i))
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

    /// <summary>Autosave tick. Skips a flush while the focused row is a transiently-empty task
    /// (add-empty-task-lifecycle): the player is mid-typing an empty new row, and serializing it would
    /// round-trip an empty task into the shared document a beat before its blur removes it. Any OTHER
    /// dirty edit still flushes on the next tick once the focused row has content or focus has moved.</summary>
    private void OnAutosaveTick(float deltaTime)
    {
        if (FocusedRowIsEmptyTask()) return;
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

    /// <summary>FocusNode listener for the title input. Commits on focus loss and rebuilds only when the
    /// editing state actually changed (blur path). Focus-gain rebuilds are handled by the pencil onTap,
    /// so suppressing them here avoids a redundant rebuild that causes the editor footer to flicker.
    ///
    /// The commit itself (a scratch-state write) runs inline, but the ForceRebuild that swaps the title
    /// back out of inline-input mode is DEFERRED to OnRenderGUI via <see cref="_pendingTitleEditRebuild"/>.
    /// This listener fires from inside pointer dispatch when a click elsewhere steals focus, and a
    /// synchronous ForceRebuild there unmounts the tree mid-walk — orphaning the button that received the
    /// click and NPE-ing in ButtonState.PlaySound. Same hazard/fix as the pencil tap.</summary>
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

    /// <summary>The field focus nodes Tab / Shift+Tab may visit in the CURRENT view, in on-screen row
    /// order — the allow-list consulted by <see cref="ScribeFieldOnlyTraversalPolicy"/>
    /// (exclude-checkboxes-from-tab-focus). Returns the editor rows' nodes in the Editor view and the pin
    /// rows' nodes (ordered to match the visible rows via <see cref="OrderedPinsForDisplay"/>) in the
    /// Pinned view; empty in every other view, so Tab does nothing there rather than landing on a
    /// checkbox. Only nodes with a live <c>Owner</c> (currently mounted) are returned, so a node awaiting
    /// disposal after a row removal can't be focused. Rebuilt fresh on every Tab press (LibGUI re-reads the
    /// order each <c>FocusNext</c>/<c>FocusPrevious</c>), so it always tracks the live row set.</summary>
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
