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
    // ---------------- Lifecycle ----------------

    /// <summary>After layout, honor a pending scroll-into-view for the focused editor row (a focus
    /// move or a row that grew while typing). Deferred to here because
    /// <see cref="Scrollable.EnsureVisible"/> reads the target's live post-layout geometry.</summary>
    public override void OnRenderGUI(float deltaTime)
    {
        base.OnRenderGUI(deltaTime);

        // Keep the window (hence the OuterArtBox art canvas) sized to the live Pixel Art Size (task 7.1).
        // The base only sets the window Size in CreateWindowConfig, which TryOpen runs ONCE per open — so a
        // live W change re-lays-out the content tree (via ForceRebuild) but leaves the window's _layoutSize
        // at the opened W, clamping the art Container to the stale size while the inner SizedBoxes grow past
        // it. Re-apply WindowSize from the current W and call the base SyncLayoutSize() (documented for a
        // programmatic WindowSize change) so the root re-lays-out at the new tight constraints. Done here (a
        // safe post-layout point) rather than in Build() to avoid mutating size mid-rebuild. No-op when W is
        // unchanged (SyncLayoutSize early-returns if _layoutSize already equals WindowSize).
        var liveLayout = host.GetLayout(modSystem.MySettings.PixelArtSize);
        var wantSize = new Vector2(liveLayout.W, liveLayout.H);
        if (WindowSize != wantSize)
        {
            WindowSize = wantSize;
            SyncLayoutSize();
        }

        // An editor row's collapse completed (its callback fired from inside the animation pump, where
        // mutating the tree would be re-entrant); retire the ghost now with an in-place reconcile
        // (scribe-list-collapse). RebuildBody re-runs BuildEditorContent, which drops the retired ghost from
        // the departing list so its slot closes; the live rows below then shift up one slot and remount
        // (the positional caveat — their text survives via the scratch write-through). Reconcile REUSES the
        // SingleChildScrollView (unlike the old ForceRebuild, which remounted it and reset the offset to 0),
        // so the offset is preserved inherently; the deferred clamp still trims it to the now-shorter extent.
        if (needsEditorCollapseCleanup)
        {
            needsEditorCollapseCleanup = false;
            if (IsOpened()) RebuildBody();
        }

        // Keep hover self-healing under a STATIONARY cursor, because LibGUI only recomputes hover on real
        // mouse motion (EventDispatcher.DispatchPointerMove is called only from GuiBase.OnMouseMove). Two
        // things leave the wrong element hovered: (1) a collapse reflowing the list every frame so a
        // different row slides under the cursor, and (2) ANY ForceRebuild — collapse cleanup, new-row
        // insert, title-edit toggle — mounting a fresh tree where every element is hovered=false. The latch
        // re-dispatches a synthetic pointer-move for a few frames past either trigger (long enough for the
        // rebuilt tree to lay out on a later frame), so the row under the cursor regains its hover-gated
        // delete/pin controls without a mouse wiggle (fix-list-collapse-stale-hover). No-op when idle.
        if (editorCollapseRegistry.AnyAnimating) hoverRefreshLatch.Arm();
        hoverRefreshLatch.ArmIfRebuilt(RootElement);
        if (hoverRefreshLatch.Tick()) RefreshHoverAtCursor();

        // Keep the viewport pinned to the bottom WHILE an editor row collapses, so deleting the last row
        // (or any row while scrolled to the bottom) closes the list up smoothly instead of snapping upward
        // (reconcile-animating-surfaces §3.10). The collapse shrinks the content height a little each frame;
        // reconcile holds the scroll offset fixed across the repaint, so without this the offset stays put
        // and dead space opens below the last row, which the post-collapse re-clamp (pendingClampToExtent)
        // then removes in one instant JumpTo — the jarring snap that was reported. Clamping the offset down
        // to the shrinking MaxScrollExtent every frame of the collapse makes it glide in lockstep with the
        // content, so the bottom edge tracks smoothly (the collapse's own EaseInOutCubic timing drives it —
        // no separate scroll animation needed). Acts ONLY when the offset is genuinely stranded past the
        // now-smaller max: a delete that leaves the viewport within bounds (not scrolled to the bottom) has
        // Offset <= max, so this is a no-op and that view is left undisturbed. Read here (after
        // base.OnRenderGUI ran BuildDirtyElements + layout above) so MaxScrollExtent reflects THIS frame's
        // collapsed height. pendingClampToExtent below remains as the final settle for the rare shrink not
        // covered by a live collapse (e.g. LibGUI's >50px wheel-slop clamp firing mid-collapse).
        if (editorCollapseRegistry.AnyAnimating)
        {
            float collapseMax = sharedScrollController.MaxScrollExtent;
            if (sharedScrollController.Offset > collapseMax)
            {
                TraceScroll("collapse-pin");
                sharedScrollController.JumpTo(collapseMax);
            }
        }

        // A task row lost focus while empty (add-empty-task-lifecycle): remove it now, deferred out of the
        // blur notification so we don't dispose focus nodes mid focus-transition. Re-read from live scratch
        // and re-check emptiness so a stale index or a row that gained text in the meantime is a safe no-op
        // (idempotent with the OnRowFocusChanged commit that also fires on a row→row move). DeleteEditorBlock
        // handles the focus-to-above fixup (Q1) and the collapse animation.
        if (pendingEmptyRowRemoval is { } emptyIdx)
        {
            pendingEmptyRowRemoval = null;
            if (isEditorMode && scratch is not null && emptyIdx >= 0 && emptyIdx < scratch.Blocks.Count)
            {
                var block = scratch.Blocks[emptyIdx];
                // Never sweep a row that CURRENTLY holds keyboard focus, or is the row we still intend to
                // focus. The empty-task self-destruct is for rows the user genuinely LEFT — but a
                // freshly-inserted empty row (Enter / New Task) catches a TRANSIENT blur when the
                // ensure-visible SetState churns the subtree and its field remounts (the one-shot auto-focus
                // is already consumed), so OnRowBlurred scheduled it here even though the user never left it.
                // The trace signature was: insert-below N -> focus=N+1 -> delete N+1, the new row vanishing
                // "a few frames" after Enter. Guard on live focus so only a real leave removes the row; the
                // terminal PurgeEmptyTasksFromScratch on switch/close still guarantees no empty task persists.
                bool stillFocused =
                    (focusedEditIndex == emptyIdx)
                    || (emptyIdx < editorFocusNodes.Count && editorFocusNodes[emptyIdx].HasFocus);
                if (block.IsTask && string.IsNullOrWhiteSpace(block.Text) && !stillFocused)
                {
                    DeleteEditorBlock(emptyIdx);
                }
            }
        }

        // Swap the title between inline-input and display mode from the safe post-dispatch point. BOTH the
        // pencil tap (enter edit: _isTitleEditing = true) and the blur listener (commit + exit edit:
        // _isTitleEditing = false) arm this flag rather than calling ForceRebuild inside pointer dispatch.
        // The rebuild re-derives the title slot from the current _isTitleEditing either way, so it runs
        // unconditionally when armed. On the enter-edit path _pendingTitleFocus (below) then focuses the
        // freshly mounted TextField on a later frame. Deferred out of dispatch — see _pendingTitleEditRebuild.
        if (_pendingTitleEditRebuild)
        {
            _pendingTitleEditRebuild = false;
            // In-place reconcile (§3.1): swaps the title slot between display Text and inline TextField
            // (different widget types → the reconcile mounts the new one fresh, so _pendingTitleFocus's
            // Owner-check below still fires once it's live) while REUSING the editor rows beneath. The
            // deferral out of pointer dispatch is retained since this flag is armed from the pencil tap /
            // blur listener; RebuildBody itself is also dispatch-safe (it only marks the body dirty).
            if (IsOpened()) RebuildBody();
        }

        // Focus the title field once its post-pencil rebuild has mounted (the stock TextField sets
        // FocusNode.Owner = Element in InitState, so a non-null Owner means it's live). Deferred out of
        // the pencil tap to avoid unmounting the tree mid-pointer-dispatch — see _pendingTitleFocus.
        if (_pendingTitleFocus)
        {
            if (_isTitleEditing && _titleFocusNode?.Owner is not null)
            {
                _titleFocusNode.RequestFocus();
                _pendingTitleFocus = false;
            }
            else if (!_isTitleEditing)
            {
                _pendingTitleFocus = false; // editing was cancelled before the field mounted; drop it
            }
        }

        // Re-home keyboard focus onto a REUSED editor row after an in-place reconcile
        // (reconcile-animating-surfaces §3.1). base.OnRenderGUI above already ran BuildDirtyElements (the
        // reconcile) + layout this frame, so the target row's field element is mounted and its focus node
        // has a live Owner. Deferred here rather than fired inside the mutation handler for the same reason
        // as pendingEnsureVisible: RequestFocus mid-pointer-dispatch (a delete/reorder/pin press) races the
        // element the click landed on. Unlike autoFocusRowOnRebuild (which rides the field's mount-only
        // autoFocus and so only re-homes a genuinely-new row), this works whether the row was reused or
        // remounted — it drives the persistent dialog-owned node directly. Waits (like the block below) for
        // a live Owner so it also survives a reconcile that arrives a frame late.
        if (pendingFocusRow is { } focusRow && isEditorMode
            && focusRow < editorFocusNodes.Count && editorFocusNodes[focusRow].Owner is not null)
        {
            editorFocusNodes[focusRow].RequestFocus();
            pendingFocusRow = null;
        }

        // Re-home keyboard focus onto a REUSED Pin Tab row after an in-place pin reconcile
        // (reconcile-animating-surfaces §4.3) — the Pin Tab twin of the editor block above. base.OnRenderGUI
        // already ran the reconcile + layout this frame, so the target row's field element is mounted and its
        // dialog-owned node (keyed by TaskId) has a live Owner. Waits for a live Owner so it survives a
        // reconcile that arrives a frame late; one-shot. Only fires while still in the Pinned view.
        if (pendingFocusPinTaskId is { } pinFocusId && viewMode == ScribeLecternView.Pinned
            && pinFocusNodes.TryGetValue(pinFocusId, out var pinNode) && pinNode.Owner is not null)
        {
            pinNode.RequestFocus();
            pendingFocusPinTaskId = null;
        }

        if (pendingEnsureVisible && isEditorMode && focusedEditIndex is { } idx
            && idx < editorFocusNodes.Count && editorFocusNodes[idx].Owner is { } element)
        {
            TraceScroll("ensure-visible");
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
            // Skip the JumpTo on frames where the ListView content height (max extent) hasn't
            // settled yet — attempting it when max < want causes a visible bounce: JumpTo sets
            // offset to want, LibGUI immediately clamps it back to max, and the flicker shows for
            // one frame. Wait until max ≥ want (or the frame budget expires), so the first JumpTo
            // that actually fires will stick.
            if (sharedScrollController.MaxScrollExtent >= want - 0.5f)
            {
                TraceScroll("restore");
                sharedScrollController.JumpTo(want);
                scrollRestoreFrames++;
                if (Math.Abs(sharedScrollController.Offset - want) < 0.5f || scrollRestoreFrames >= 5)
                {
                    pendingRestoreScrollOffset = null;
                }
            }
            else
            {
                // Max not yet settled — count the frame toward the budget anyway so we don't spin
                // forever if the list genuinely got shorter than the captured offset.
                TraceScroll("restore-wait");
                scrollRestoreFrames++;
                if (scrollRestoreFrames >= 5) pendingRestoreScrollOffset = null;
            }
        }

        // Re-clamp after a delete once layout has reported the shrunk content height (see
        // pendingClampToExtent). We run the FULL settling window rather than stopping as soon as the
        // offset is within bounds: ContentSize is only reported on the layout pass(es) after the rebuild
        // and may still be the pre-delete (larger) value on the first frame — so an early "already within
        // max" check would terminate before the shrink is ever reflected. Clamping while already in
        // bounds is a harmless no-op (JumpTo only moves when Offset > max), so running all frames is safe.
        // Skipped while a restore is still pending so the two don't fight.
        if (pendingClampToExtent && pendingRestoreScrollOffset is null)
        {
            TraceScroll("clamp");
            float max = sharedScrollController.MaxScrollExtent;
            if (sharedScrollController.Offset > max) sharedScrollController.JumpTo(max);
            if (++clampToExtentFrames >= 5) pendingClampToExtent = false;
        }
    }

    /// <summary>Ask <see cref="OnRenderGUI"/> to clamp the shared scroll offset down to the new
    /// <c>MaxScrollExtent</c> after the next layout — used when a row removal shrinks the content so the
    /// viewport can't be left stranded past the (now smaller) bottom.</summary>
    private void RequestClampToExtent()
    {
        pendingClampToExtent = true;
        clampToExtentFrames = 0;
        TraceScroll("request-clamp");
    }

    public override void OnGuiClosed()
    {
        // Workaround for a gui@3.1.0 bug: ButtonState.PlaySound() calls
        // Element.Owner.GetSoundPlayer() inside a SetState callback that may fire
        // after the element is unmounted (e.g. the close button click that caused
        // this close). Owner is null at that point → NullReferenceException. Swapping
        // to a silent no-op player before teardown ensures GetSoundPlayer() returns
        // non-null and the deferred callback completes harmlessly.
        BuildOwner.SetSoundPlayer(new SilentSoundPlayer(capi));

        CommitTitleIfEditing();
        if (isEditorMode)
        {
            if (focusedEditIndex is { } closeIdx) NormalizeRowOnCommit(closeIdx);
            // Same empty-task cleanup as switch-to-read: closing with an abandoned empty task must not
            // persist it (add-empty-task-lifecycle D5).
            PurgeEmptyTasksFromScratch();
            pendingEmptyRowRemoval = null;
            FlushIfDirty();
            StopAutosaveTick();
            DisposeFocusNodes();
        }
        // Release the editor lock on EVERY close, not only when the client is still in editor mode
        // (fix-transient-lectern-editor-lock 1.2). A player who acquired the lock and then switched to
        // read/pins/history before closing would otherwise skip the release, leaking the server's
        // authoritative holder and locking the lectern out until block reload/restart. ReleaseLock is
        // idempotent + UID-guarded server-side, so this is a harmless no-op when we hold no lock.
        SendReleaseLockPacket();
        modSystem.MyPinsChanged -= OnMyPinsChanged;
        modSystem.SettingsVisibilityChanged -= OnSettingsVisibilityChanged;
        DisposePinState();
#if DEBUG
        sharedScrollController.OnChanged -= OnScrollControllerChanged;
#endif
        _titleFocusNode?.RemoveListener(OnTitleFocusChanged);
        _titleFocusNode?.Dispose();
        _titleController?.Dispose();
        foreach (var node in _guestbookNoteFocusNodes.Values)
            node.Dispose();
        _guestbookNoteFocusNodes.Clear();
        // The dialog owns the shared scroll controller (see its field); dispose it once here rather
        // than in either view's State, which come and go with each view-switch ForceRebuild.
        sharedScrollController.Dispose();
        // Drop any in-flight collapse ghosts + their controllers so a reopen starts clean (scribe-list-collapse).
        departingEditorRows.Clear();
        editorCollapseRegistry.Dispose();
        base.OnGuiClosed();
    }

    protected virtual void SendReleaseLockPacket()
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeReleaseLockMessage
        {
            DocIdBytes = host.Document.DocId.ToByteArray(),
        });
    }
}
