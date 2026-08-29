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
        TraceScroll($"text-changed {index}");
    }

    /// <summary>A Tracker row's inline +/- stepper changed its target quantity (add-tracker-link-tasks 5.2).
    /// Mirrors <see cref="NotifyTextChanged"/>: write straight through to the scratch block and mark dirty so
    /// the normal editor flush persists it — the codec already serializes <see cref="ScribeBlock.TargetQuantity"/>,
    /// so no dedicated packet is needed. The Core setter clamps the value to ≥ 1 (lowering the target no longer
    /// touches CurrentQuantity — the live carried count may exceed it, 7.14). Deliberately does NOT rebuild: the stepper is an uncontrolled field
    /// that already reflects its own value, and a rebuild would drop its focus mid-step; the target's only
    /// editor-side mutation is this stepper, so scratch stays consistent without one.</summary>
    private void SetEditorTrackerTargetQuantity(int index, int qty)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        var block = scratch.Blocks[index];
        if (!block.IsCarriedCountTracked) return; // Tracker OR Craft parent (both carry a target stepper)
        block.TargetQuantity = qty; // clamps ≥ 1 in the Core setter
        isDirty = true;

        // A Craft parent owns generated ingredient subtasks whose targets scale with its own (add-crafting-tasks
        // 6.4): re-heal the depth-1 run so the children rescale in place (and any deleted child is recreated),
        // then rebuild so the changed child rows are visible. Unlike a plain Tracker's stepper (which
        // deliberately avoids a rebuild to keep its caret), a Craft target change is an intentional, infrequent
        // action whose whole point is the visible rescaling of the ingredient list, so the rebuild is warranted.
        if (block.IsCraft)
        {
            RescaleCraftFromSignature(scratch, block);
            RebuildBody();
        }
    }

    /// <summary>A single tap on a row's grip toggles its subtask depth between 0 and 1 (task-subtasks 5.3),
    /// the one-level nesting the whole change is bounded to (<see cref="ScribeBlock.Depth"/> clamps to
    /// <c>[0, 1]</c>). Kind-agnostic — any row (Task/Note/Tracker/Link/Craft) can be indented or promoted.
    /// Mirrors the other editor mutations (mutate scratch → mark dirty → rebuild); the codec already
    /// round-trips <see cref="ScribeBlock.Depth"/>, so the normal editor flush persists it with no dedicated
    /// packet. A press-hold-drag reorder never routes here — the grip's <see cref="GestureDetector"/> fires
    /// its tap only on a genuine click (the dispatcher suppresses it during a drag). No focus change: the
    /// grip is not a text field, so there is no caret to re-home.</summary>
    private void OnGripTap(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        var block = scratch.Blocks[index];
        block.Depth = block.Depth == 0 ? 1 : 0; // Core clamps to [0, 1]; no depth-2 is reachable
        isDirty = true;
        RebuildBody();
    }

    /// <summary>A focused row's caret moved by KEYBOARD navigation (arrows / Home / End / word-jump) with no
    /// text change (scroll-follow-caret-in-editor issue #1). Text edits already arm the scroll-follow via
    /// <see cref="NotifyTextChanged"/>, but a bare caret move does not go through <c>OnChanged</c> — so without
    /// this an arrow key that carries the caret off-screen never scrolled it back. Arms the same
    /// <see cref="pendingEnsureVisible"/> flag the render loop services with <see cref="EnsureCaretVisible"/>.
    /// (No scratch write — the text is unchanged.)</summary>
    private void NotifyCaretMoved(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        pendingEnsureVisible = true;
        TraceScroll($"caret-moved {index}");
    }

    /// <summary>A row was focused by a MOUSE CLICK (scroll-follow-caret-in-editor issue #3). A click always
    /// lands on a visible pixel, so the caret is already in view where the player clicked — the view must not
    /// move at all. Two things could move it: (a) our own scroll-follow, which we cancel by clearing
    /// <see cref="pendingEnsureVisible"/>; and (b) the shipped gui's <c>FocusManager.RequestFocus</c>, which
    /// unconditionally calls <c>Scrollable.EnsureVisible(focusedElement)</c> and bounces a taller-than-viewport
    /// row to its top/bottom (confirmed via stack trace: JumpTo ← Scrollable.EnsureVisible ← FocusManager.RequestFocus).
    /// That focus-scroll already ran synchronously inside the field's <c>focusNode.RequestFocus()</c> before this
    /// callback fires, so we undo it by jumping straight back to the pre-click offset (<see cref="lastStableScrollOffset"/>).
    /// Both happen in the same input-phase call, before the next render, so the bounce is never painted.
    /// Programmatic focus (Tab / Enter via <see cref="FocusEditorRow"/>) SHOULD scroll and does not route here —
    /// the field fires this only from its pointer-press path, so the two focus sources stay distinguishable.</summary>
    private void NotifyPointerFocus(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        pendingEnsureVisible = false;
        // Undo the shipped FocusManager's focus-scroll: restore the view to where it was pre-click. No-op when
        // focus didn't actually change (clicking the already-focused row → RequestFocus early-returns, offset
        // unchanged → JumpTo to the same value is filtered by the controller's epsilon guard).
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

        // Tier cap (scribe-document-policy): the Enter=new-task gesture also stops at the tablet cap.
        if (!CanAddTaskUnderPolicy()) { NotifyTabletFull(); return; }

        NormalizeRowOnCommit(index);
        FlushIfDirty();

        int insertAt = index + 1;
        if (scratch.InsertTask(insertAt, ""))
        {
            isDirty = true;
            SyncFocusNodesToScratch();
            // A genuinely-new row MOUNTS this reconcile, so its field's mount-only autoFocus fires — keep
            // autoFocusRowOnRebuild rather than pendingFocusRow. Rows ABOVE the insertion keep their slots
            // and are reused (caret intact); rows below shift and remount (unfocused — no loss).
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

    /// <summary>Quick-add seam (add-unified-quick-add-interaction): insert a fresh EMPTY task at the
    /// player's New Task Insert edge (Top = index 0, Bottom = append) and focus its caret, so the
    /// Shift+right-click capture gesture drops the player straight into that new row. Called AFTER the
    /// editor view is already active (the lectern's server round-trip lands here via
    /// <see cref="EnterEditorMode"/> → <see cref="QuickAddTopTask"/>; the item hosts call it right after
    /// their immediate <see cref="RequestEditorAccess"/> grant). Reuses
    /// <see cref="ScribeDocument.InsertTask"/> + the focus/rebuild machinery
    /// <see cref="EditorInsertTaskBelow"/> uses rather than a new Core mutation.
    ///
    /// <para>Respects the tier cap exactly like the editor's own add controls: at the cap the editor still
    /// opens but no task is inserted and the same "document full" feedback (<see cref="NotifyTabletFull"/>)
    /// is surfaced. A no-op if the editor isn't active (defensive — every caller enters it first).</para></summary>
    public void QuickAddTopTask()
    {
        if (!isEditorMode || scratch is null) return;
        TraceScroll("quick-add");

        // Tier cap (scribe-document-policy): mirror OnClickAdd / EditorInsertTaskBelow — open the editor
        // but refuse the insert at the cap, surfacing the same transient "document full" notice.
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

    /// <summary>A row's editable field lost focus (add-empty-task-lifecycle D3; generalized to notes by
    /// add-note-kind-picker D3). If the block at <paramref name="index"/> — a task OR note — is
    /// empty/whitespace-only, schedule its removal: the self-destruct that turns "clear the text and click
    /// away" (or Tab off an untyped new row) into a keyboard-only delete. (This reverses the earlier
    /// "empty text section is never removed" behavior; empty notes were only creatable via dev tools, so no
    /// shipped player flow relied on a lingering blank note.)
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
        // add-note-kind-picker D3: an abandoned empty row of EITHER kind self-destructs (previously task-only).
        // add-tracker-link-tasks: Tracker/Link rows are excluded (they carry empty Text by design) — see
        // IsAbandonableEmptyBlock.
        if (!IsAbandonableEmptyBlock(scratch.Blocks[index])) return;
        pendingEmptyRowRemoval = index;
    }

    /// <summary>A row's editor field hit its per-kind character cap (add-note-kind-picker §8) — the field
    /// already clamped the input; here we just explain it. Surfaces the same transient in-game error path the
    /// tablet-full / lock notices use, with a per-kind message whose character count is pulled from the limit
    /// constant (so the message and the enforced cap can't drift). Tasks cap at
    /// <see cref="ScribeDocumentCodec.MaxTaskTextLength"/>, notes at the larger
    /// <see cref="ScribeDocumentCodec.MaxTextLength"/>.</summary>
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

        Guid taskId = scratch.Blocks[index].TaskId;
        var deleted = ScribeCompletion.ApplyDelete(scratch, taskId, modSystem.MySettings.SubtaskBehavior);
        if (deleted.Count == 0) return; // deletion refused: nothing removed, no departure
        int removedCount = deleted.Count;

        isDirty = true;

        // Fix up the focused index across the deletion: the focused row is gone (clear), or sat after
        // the deleted range (shift up by the number removed), or before it (unchanged).
        if (focusedEditIndex is { } f)
        {
            if (f >= index && f < index + removedCount) focusedEditIndex = null;
            else if (f >= index + removedCount) focusedEditIndex = f - removedCount;
        }

        // Re-home the caret after the reconcile. Pressing the (non-IFocusable) delete button already blurred
        // the field via LibGUI's DispatchPointerDown focus-clear, so even a surviving edited row needs its
        // focus re-requested — nothing re-grants it otherwise, which is why the caret vanished (a05caret1).
        // If we deleted the focused row, land on the neighbor per design Q1: the row above (index-1), or the
        // new first row when the top row was deleted; an emptied document gets no focus (empty-state hint).
        // Reconcile path (§3.1): a delete never MOUNTS a new row, so the focus target is always a REUSED row
        // whose field skips its mount-only autoFocus — re-home via pendingFocusRow (the deferred RequestFocus
        // on the persistent node), NOT autoFocusRowOnRebuild. Because the departing ghost holds the deleted
        // slot while it collapses, rows below keep their slots and stay reused with their caret intact FOR
        // THE COLLAPSE; the below rows only remount (losing caret POSITION, text preserved via write-through)
        // when the ghost retires and they shift up — the accepted positional caveat re-armed in the cleanup.
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
        // The re-clamp to the shrunk extent is DEFERRED to when the collapse completes: during the collapse
        // the container's ghost still occupies (shrinking) height, so the content extent doesn't actually
        // shrink until the row reaches zero — clamping now would fight the collapsing height
        // (scribe-list-collapse). The container fires OnDepartureSettled → RequestClampToExtent once the
        // ghost retires (D0), and §3.10's collapse-pin glides the viewport down meanwhile.
        RebuildBody();
    }

    /// <summary>Per-row pin toggle (any block kind, including Text notes). Pinning is a per-player
    /// action now, NOT document state: fire a lock-free <see cref="ScribeSetPinMessage"/> keyed by the
    /// task's stable id, toggled against the client's own pin cache. It never touches the scratch
    /// document, the edit lock, or autosave. The server re-pushes this player's set, which lands in
    /// <see cref="OnMyPinsChanged"/> and repaints the row.</summary>
    private void TogglePinnedEditorTask(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        var block = scratch.Blocks[index];
        // Tier cap (scribe-document-policy): the tablet tier allows only 1 pin. Unpinning is always
        // allowed; pinning a new task at the cap seamlessly SWAPS — the older pin is released so the
        // new one fits. Uncapped tiers (Lectern/Notebook) just pin.
        TogglePinWithPolicy(block.TaskId);
    }

    /// <summary>Fire-and-forget a pin/unpin for a task by stable identity. The document's DocId plus
    /// the task's TaskId fully address the pin; no block position is sent (so the same shape works
    /// from a future HUD). The server derives the snapshot from its own document.
    ///
    /// <para>Also records the SAME snapshot as an optimistic overlay entry on <see cref="modSystem"/>
    /// (update-pins-1-3-3) before the packet goes out, so the HUD/Pin Tab/dialog rows all show the
    /// pin/unpin at once rather than waiting for the server round-trip — which in singleplayer stalls for
    /// as long as the Handbook holds the embedded server paused.</para></summary>
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
            // Supply a client-side snapshot so the server can record it even when the host is not
            // registered server-side (e.g. Notebook items — only the client holds the document). Includes
            // the kind + a Link's target so a pinned Link reaches the HUD as a hyperlink (5.5).
            SnapshotText = block?.Text ?? "",
            SnapshotDone = block?.Done ?? false,
            SnapshotKind = (byte)(block?.Kind ?? Scribe.Core.ScribeBlockKind.Task),
            SnapshotLinkTarget = block?.LinkTarget,
            // A Tracker's target item + have/need counts, so a pinned Tracker renders its icon + name +
            // counter on the HUD/Pin Tab even for an item host the server can't resolve (7.8).
            SnapshotTargetItemCode = block?.TargetItemCode,
            SnapshotTargetQuantity = block?.TargetQuantity ?? 1,
            SnapshotCurrentQuantity = block?.CurrentQuantity ?? 0,
            // A guide-page Link's display title, so a pinned guide-page Link renders its name on the
            // HUD/Pin Tab (a "page:" target has no item to resolve a name from) (7.6).
            SnapshotLinkLabel = block?.LinkLabel,
            // The subtask depth, so a pinned subtask indents on the HUD/Pin Tab like the other surfaces
            // (add-crafting-tasks / task-subtasks 5.1).
            SnapshotDepth = block?.Depth ?? 0,
            // Where an unrelated (no pinned-parent) new pin lands — Top or Bottom — per the player's
            // client-local Pin Insert setting (update-pins-1-3-3). Only meaningful when pinned; the
            // server ignores it on an unpin.
            PinInsert = (byte)insertEdge,
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
    /// <c>SingleChildScrollView</c> offset to 0, so the capture+restore is required, not optional.</para>
    ///
    /// <para><paramref name="preserveFocusedRow"/> chooses what happens to keyboard focus. A LOCAL move
    /// (false) grabs focus onto the moved row, because the player just acted on that row (dragged it, or
    /// completed it under Sink). An EXTERNAL move (true) — a HUD Sink completion arriving via
    /// <see cref="RefreshReadView"/> while the player is editing a DIFFERENT row — must instead keep focus
    /// on whatever row is being edited, recomputing that row's index across the shift, so the external
    /// completion of one task doesn't yank the caret out of the task the player is typing in
    /// (sync-editor-view-on-external-completion). A row above the moved one is unshifted and stays reused
    /// with its caret intact; a row below inherits the same accepted positional caret caveat as any
    /// reorder (its slot shifts by one, so it remounts with text preserved).</para></summary>
    private void ReorderEditorBlock(int from, int to, bool anchorViewport = false, bool preserveFocusedRow = false)
    {
        if (scratch is null) return;
        TraceScroll($"reorder {from}->{to} anchor={anchorViewport} preserveFocus={preserveFocusedRow}");

        int start = from;
        int end = from + 1;
        if (from >= 0 && from < scratch.Blocks.Count && scratch.Blocks[from].Depth == 0)
            end = scratch.OwnedRun(from).End; // cluster [from, runEnd); empty run → from+1

        bool dropOnCluster = to >= start && to < end;
        // Same-depth-reorder: a depth-0 cluster may only land on another depth-0 row, and a depth-1
        // leaf only on another depth-1 row (both surfaces share this rule via ScribeReorderValidity so
        // the grip's drop-target arrow can never light up on a drop the commit here then refuses).
        // IsValidDropTarget already treats dropOnCluster as valid (a no-op), so a false result here
        // means specifically a cross-depth drop outside the dragged row's own cluster.
        var depths = scratch.Blocks.Select(b => b.Depth).ToList();
        bool crossDepth = !ScribeReorderValidity.IsValidDropTarget(depths, start, end, to);
        if (from == to || dropOnCluster || crossDepth)
        {
            // Released in place (or a grip click that never dragged, drop onto own children, or an
            // invalid cross-depth drop): no edit — but the grip press already blurred the focused field
            // via LibGUI's DispatchPointerDown focus-clear (the grip isn't IFocusable), so re-home the
            // caret to the row that was being edited or nothing else will (a05caret1). No rebuild is
            // needed to move the doc; RequestFocus alone re-grants focus.
            if (focusedEditIndex is { } held && held < editorFocusNodes.Count) FocusEditorRow(held);
            return;
        }

        Guid? preservedId = null;
        if (preserveFocusedRow && focusedEditIndex is { } pf && pf < scratch.Blocks.Count)
            preservedId = scratch.Blocks[pf].TaskId;

        // Resolve the actual destination so dropping forward onto a parent with its own children lands
        // AFTER that parent's whole cluster rather than wedging between it and its first child (the
        // upward direction already lands correctly with no adjustment — see ResolveDestination).
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
        // Re-home focus through the persistent node (§3.1). A reorder shifts every slot between from and to,
        // so a remounted row loses its granted focus under the positional reconciler — drive focus via
        // pendingFocusRow (the deferred RequestFocus) rather than the field's mount-only autoFocus, per the
        // finalized reconcile decision: robust whether the row is reused or remounted, one focus path for
        // delete/reorder. Skip the re-grant on an external move whose edited row did NOT change slot: that
        // row is reused and still focused, so re-requesting would needlessly reset its caret to the end.
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
            // Sink: reconcile preserves the shared controller's scroll offset in place (no ForceRebuild
            // resets it to 0 anymore), so the "hold the viewport still" behavior is now inherent — no
            // capture-restore needed. (§3.5 revisits whether the capture apparatus can be removed wholesale.)
        }
        else
        {
            // Drag: follow the moved row into view.
            pendingEnsureVisible = true;
        }
        RebuildBody();
    }

    /// <summary>Footer add-picker action (add-note-kind-picker D2): insert an EMPTY block of the chosen
    /// <paramref name="kind"/> (Task or Note) at the player's New Task Insert edge, grow the focus-node
    /// list, and rebuild with the new row auto-focused so the player types straight into it (no boilerplate
    /// to clear — add-empty-task-lifecycle). A task field renders a dimmed "New task…" ghost hint while
    /// empty, a note "New note…"; either row, if abandoned without typing, is removed on blur (see
    /// <see cref="OnRowBlurred"/>). The picker's primary button passes its current kind (defaults to Task,
    /// so one click still adds a task); picking from the inline kind list passes that kind. Kinds and their
    /// add delegates come from <see cref="ScribeAddKinds"/> — adding a future kind touches the registry,
    /// not this method.</summary>
    private void OnClickAdd(ScribeAddKind kind)
    {
        if (scratch is null) return;
        // Tier cap (scribe-document-policy): a finite tier (tablet, chalkboard) stops at 10 blocks of ANY kind
        // — tasks, notes, trackers, links, and craft parents all count equally (refine-chalkboard §12). Every
        // add kind is gated, so a note trips the cap just like a task. Uncapped tiers (Lectern, Notebook) never
        // trip this. Cap check runs FIRST so a footer tap on Add Link / Tracker / Craft at the cap surfaces
        // the "full" notice rather than the Handbook guide (those kinds can't be added from the footer anyway,
        // and 12.9 wants every Add affordance to explain the cap). The picker dims at the cap but stays
        // clickable so this path is reachable (zero-point-three-fixes §7.2).
        // add-tracker-link-tasks 3.7: Tracker/Link/Craft can't be created from a bare footer click — they need
        // a target item/recipe, which only a Handbook page's "Add to Scribe" link supplies. Below the cap, a
        // footer click on one of these is a GUIDE gesture, not an add: open the explainer entry (Handbook
        // closed) or point the player at the per-item link (Handbook open). Nothing is mutated. See
        // DispatchItemKindGuide.
        if (!CanAddTaskUnderPolicy()) { NotifyTabletFull(); return; }
        if (kind.RequiresItemContext) { DispatchItemKindGuide(kind); return; }
        if (focusedEditIndex is { } leaving) NormalizeRowOnCommit(leaving);
        // The second arg is the item code for item-bound kinds; Task/Note ignore it, so a footer add always
        // passes null (the item-bound kinds never reach here — they return above).
        int at = NewTaskInsertIndex();
        if (!kind.Add(scratch, null, at)) return;
        isDirty = true;
        SyncFocusNodesToScratch();
        // The new row MOUNTS at `at`, so its field's mount-only autoFocus fires; rows above keep their
        // slots (carets intact). Reconcile path — see EditorInsertTaskBelow.
        autoFocusRowOnRebuild = at;
        focusedEditIndex = at;
        pendingEnsureVisible = true;
        RebuildBody();
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

    /// <summary>Cmd/Ctrl+Up: commit the current row and jump focus to the FIRST row, caret at its start
    /// (macOS document-top navigation — scroll-follow-caret-in-editor Cmd/Ctrl row-nav). The scroll-follow
    /// then carries the top of the document into view (caret at offset 0 → scrolls to the very top).</summary>
    private void EditorJumpToFirstRow(int index) => JumpToEditorEdge(index, toFirst: true);

    /// <summary>Cmd/Ctrl+Down: mirror of <see cref="EditorJumpToFirstRow"/> — jump to the LAST row, caret
    /// at its end (document-bottom navigation).</summary>
    private void EditorJumpToLastRow(int index) => JumpToEditorEdge(index, toFirst: false);

    /// <summary>Shared first-/last-row jump. Commits the row being left (like advance/retreat), focuses the
    /// edge row, and snaps that row's caret to the document edge — start for the first row, end for the last —
    /// so the caret lands where a document-top/bottom nav is expected rather than wherever that field last
    /// left it. A single-row note is a safe no-op beyond re-homing the caret. The caret placement fires the
    /// field's scroll-follow, and <see cref="FocusEditorRow"/> arms <see cref="pendingEnsureVisible"/>, so the
    /// edge is scrolled fully into view.</summary>
    private void JumpToEditorEdge(int index, bool toFirst)
    {
        if (scratch is null || scratch.Blocks.Count == 0) return;
        NormalizeRowOnCommit(index);
        FlushIfDirty();
        int target = toFirst ? 0 : scratch.Blocks.Count - 1;
        FocusEditorRow(target);
        // Place the caret at the document edge in the row we just focused. The field's caret otherwise
        // persists wherever it last sat (it defaults to end-of-text on mount and focus-gain never resets it),
        // so without this Cmd/Ctrl+Up would land mid-row on a previously-visited first row.
        ResolveEditorFieldState(target)?.PlaceCaretAtEdge(atStart: toFirst);
    }

    /// <summary>Resolve the live <see cref="ScribeMultilineFieldState"/> backing editor row
    /// <paramref name="index"/> via its focus node's owning element (the field sets
    /// <c>focusNode.Owner = Element</c> in its <c>InitState</c>). Returns null if the row isn't mounted or
    /// isn't a plain/cuneiform multiline field. Used to drive an imperative caret placement that has no
    /// declarative prop (the row jump), the way the dialog already reaches the body State via its GlobalKey.</summary>
    private ScribeMultilineFieldState? ResolveEditorFieldState(int index)
    {
        if (index < 0 || index >= editorFocusNodes.Count) return null;
        return (editorFocusNodes[index].Owner as StatefulElement)?.State as ScribeMultilineFieldState;
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

    /// <summary>True when the currently-focused editor row is a task OR note whose text is
    /// empty/whitespace-only — a transiently-empty row the player is (or just was) editing
    /// (add-empty-task-lifecycle; generalized to notes by add-note-kind-picker D3). The autosave tick uses
    /// this to avoid serializing that transient empty row; leaving the row / closing removes it.</summary>
    private bool FocusedRowIsEmptyBlock()
    {
        if (scratch is null || focusedEditIndex is not { } idx || idx < 0 || idx >= scratch.Blocks.Count) return false;
        return IsAbandonableEmptyBlock(scratch.Blocks[idx]);
    }

    /// <summary>True for a row the editor may silently self-destruct when abandoned empty: a Task or a Note
    /// (<see cref="ScribeBlockKind.Text"/>) whose text is blank/whitespace-only. Tracker and Link rows are
    /// EXCLUDED — they legitimately carry empty <see cref="ScribeBlock.Text"/> (their content is the bound
    /// item plus a counter/link, not typed text), so the abandoned-empty-row sweep must never purge a
    /// freshly-added Tracker/Link the moment it loses focus (add-tracker-link-tasks). Centralizes the
    /// predicate shared by <see cref="OnRowBlurred"/>, <see cref="FocusedRowIsEmptyBlock"/>, and
    /// <see cref="PurgeEmptyRowsFromScratch"/>.</summary>
    private static bool IsAbandonableEmptyBlock(ScribeBlock b) =>
        (b.IsTask || b.Kind == ScribeBlockKind.Text) && string.IsNullOrWhiteSpace(b.Text);

    /// <summary>Removes every empty/whitespace-only block (task OR note) from the scratch document, marking
    /// dirty if any were removed (add-empty-task-lifecycle D5; generalized to notes by add-note-kind-picker
    /// D3 — an abandoned empty note self-destructs just like an empty task). Called at the terminal commit
    /// paths (switch-to-read, close) so an abandoned empty row the blur-removal hadn't yet swept is never
    /// flushed or shown in the read view. No rebuild/collapse — the caller tears the editor down or rebuilds
    /// into the read view immediately after.</summary>
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

    /// <summary>Autosave tick. Skips a flush while the focused row is a transiently-empty task OR note
    /// (add-empty-task-lifecycle; add-note-kind-picker D3): the player is mid-typing an empty new row, and
    /// serializing it would round-trip an empty block into the shared document a beat before its blur removes
    /// it. Any OTHER dirty edit still flushes on the next tick once the focused row has content or focus has
    /// moved.</summary>
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

    /// <summary>Build the item-surface save packet (<see cref="ScribeNotebookSaveMessage"/>) shared by the
    /// Notebook and Tablet dialogs. Stamps the host's bound slot identity (inventory id + slot index) onto
    /// the packet WHEN the host exposes one, so the server writes the document back to the exact slot the
    /// dialog is editing rather than re-deriving it from the player's active hand — the misroute that let a
    /// Handbook add land on a different in-hand item (add-tracker-link-tasks 7.16). A host with no resolvable
    /// slot identity sends none, and the server falls back to the active-hand slot (legacy behavior).</summary>
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
