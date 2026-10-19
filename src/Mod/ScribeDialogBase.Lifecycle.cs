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
using Gui.Core.Framework;        // RenderObject (caret-follow content-Y walk)
using Gui.Core.Layout;           // MainAxisSize
using Gui.Core.Scroll;           // RenderViewport (caret-follow content-Y walk)
using OpenTK.Mathematics;        // Vector2
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;   // Lang, GlobalConstants
using Vintagestory.API.MathTools;  // BlockPos

namespace Scribe;

public abstract partial class ScribeDialogBase
{
    // ---------------- Lifecycle ----------------

    /// <summary>After layout, services pending deferred work (shade refresh, scroll-into-view for the focused editor row, focus re-homing). Deferred to here because <see cref="EnsureCaretVisible"/> reads post-layout geometry.</summary>
    public override void OnRenderGUI(float deltaTime)
    {
        base.OnRenderGUI(deltaTime);

        // Sample the light reaching the player (render thread only; block-accessor reads off the relight thread are unsafe) and shade the dialog to match.
        // The sampler eases and quantizes the shade and reports whether the bucket CHANGED; only then is the body reconciled so LibGUI re-records its SKPicture. RebuildBody reuses every row, so caret and scroll are undisturbed.
        currentShade = lightSampler.Sample(deltaTime);
        if (currentShade.Changed) RebuildBody();

        // Keep the window sized to the live Pixel Art Size: CreateWindowConfig sets Size once per open, so a live W change would leave _layoutSize stale and clamp the art Container. Done here (post-layout) rather than in Build() to avoid mutating size mid-rebuild; SyncLayoutSize no-ops when unchanged.
        var liveLayout = host.GetLayout(modSystem.MySettings.PixelArtSize);
        var wantSize = new Vector2(liveLayout.W, liveLayout.H);
        if (WindowSize != wantSize)
        {
            WindowSize = wantSize;
            SyncLayoutSize();
        }

        // Editor-row collapse cleanup belongs to ScribeAnimatedList (it retires ghosts itself); its OnDepartureSettled -> RequestClampToExtent re-clamps the scroll extent.

        // Keep hover self-healing under a STATIONARY cursor (LibGUI only recomputes hover on real mouse motion): a collapse reflowing the list, or any ForceRebuild mounting a fresh tree with hovered=false, leaves the wrong element hovered. The latch re-dispatches a synthetic pointer-move for a few frames past either trigger. The registries are host-owned, so this loop lives here.
        if (AnyRowAnimating) hoverRefreshLatch.Arm();
        hoverRefreshLatch.ArmIfRebuilt(RootElement);
        if (hoverRefreshLatch.Tick()) RefreshHoverAtCursor();

        // Keep the viewport pinned to the bottom WHILE a row collapses (editor, Pin Tab, read view; only one is mounted): reconcile holds the offset fixed, so without clamping to the shrinking MaxScrollExtent dead space opens and the post-collapse re-clamp (pendingClampToExtent) snaps.
        // Acts only when the offset is stranded past the new max. Read after base.OnRenderGUI so MaxScrollExtent reflects THIS frame.
        if (AnyRowAnimating)
        {
            float collapseMax = sharedScrollController.MaxScrollExtent;
            if (sharedScrollController.Offset > collapseMax)
            {
                TraceScroll("collapse-pin");
                sharedScrollController.JumpTo(collapseMax);
            }
        }

        // A row lost focus while empty: remove it now, deferred out of the blur notification so focus nodes aren't disposed mid-transition. Re-reads live scratch and re-checks emptiness, so a stale index or a row that gained text is a no-op.
        if (pendingEmptyRowRemoval is { } emptyIdx)
        {
            pendingEmptyRowRemoval = null;
            if (isEditorMode && scratch is not null && emptyIdx >= 0 && emptyIdx < scratch.Blocks.Count)
            {
                var block = scratch.Blocks[emptyIdx];
                // Never sweep a row that holds focus or is about to: a freshly inserted empty row catches a TRANSIENT blur when the ensure-visible SetState remounts its field, and would otherwise vanish a few frames after Enter. PurgeEmptyRowsFromScratch on switch/close still guarantees no empty row persists.
                bool stillFocused =
                    (focusedEditIndex == emptyIdx)
                    || (emptyIdx < editorFocusNodes.Count && editorFocusNodes[emptyIdx].HasFocus);
                if (string.IsNullOrWhiteSpace(block.Text) && !stillFocused)
                {
                    DeleteEditorBlock(emptyIdx);
                }
            }
        }

        // Swap the title between input and display from this post-dispatch point; the pencil tap and the blur listener arm the flag instead of calling ForceRebuild inside pointer dispatch. _pendingTitleFocus then focuses the freshly mounted field.
        if (_pendingTitleEditRebuild)
        {
            _pendingTitleEditRebuild = false;
            // In-place reconcile: the different widget types mount the new one fresh (so the Owner check below still fires) while reusing the editor rows beneath.
            if (IsOpened()) RebuildBody();
        }

        // Focus the title field once mounted (the stock TextField sets FocusNode.Owner in InitState); deferred out of the pencil tap, see _pendingTitleFocus.
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

        // Re-home focus onto a REUSED editor row after an in-place reconcile. Deferred because RequestFocus mid-pointer-dispatch (delete/reorder/pin press) races the clicked element; unlike autoFocusRowOnRebuild it works for reused or remounted rows. Waits for a live Owner so a late reconcile still works.
        if (pendingFocusRow is { } focusRow && isEditorMode
            && focusRow < editorFocusNodes.Count && editorFocusNodes[focusRow].Owner is not null)
        {
            editorFocusNodes[focusRow].RequestFocus();
            pendingFocusRow = null;
        }

        // Pin Tab twin of the block above (nodes keyed by TaskId); one-shot, only while in the Pinned view.
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
            EnsureCaretVisible(element);
            pendingEnsureVisible = false;
        }

        // Re-apply a scroll offset captured across a view switch (pendingRestoreScrollOffset); the read view's ListView settles its height over a few frames, so retry until it sticks or the budget runs out.
        if (pendingRestoreScrollOffset is { } want)
        {
            // Skip frames where max extent hasn't settled: JumpTo past max is clamped back immediately, causing a one-frame bounce.
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
                // Max not yet settled; still count the frame so a genuinely shorter list can't spin forever.
                TraceScroll("restore-wait");
                scrollRestoreFrames++;
                if (scrollRestoreFrames >= 5) pendingRestoreScrollOffset = null;
            }
        }

        // Re-clamp after a delete once layout reports the shrunk content height (pendingClampToExtent). Runs the full settling window since ContentSize may still be the larger pre-delete value on the first frame; clamping in bounds is a no-op. Skipped while a restore is pending.
        if (pendingClampToExtent && pendingRestoreScrollOffset is null)
        {
            TraceScroll("clamp");
            float max = sharedScrollController.MaxScrollExtent;
            if (sharedScrollController.Offset > max) sharedScrollController.JumpTo(max);
            if (++clampToExtentFrames >= 5) pendingClampToExtent = false;
        }

        // Snapshot the settled offset (last, after all servicing) for NotifyPointerFocus to restore after a click's focus-scroll (lastStableScrollOffset).
        lastStableScrollOffset = sharedScrollController.Offset;
    }

    /// <summary>Scrolls so the focused field's CARET stays visible rather than the whole row: targeting the row (<c>Scrollable.EnsureVisible</c>) would ping-pong a row taller than the viewport. Single winning outcome: bottom-align if below, top-align if above, else no move.</summary>
    private void EnsureCaretVisible(Element fieldElement)
    {
        var proxyRo = fieldElement.RenderObject;
        if (proxyRo is null) return;

        // Resolve the text render object (proxy or its child[0]); works for plain and cuneiform fields via IScribeEditableTextRender.
        var textRender = proxyRo as IScribeEditableTextRender
            ?? (proxyRo.Children.Count > 0 ? proxyRo.Children[0] as IScribeEditableTextRender : null);
        if (textRender is null || textRender is not RenderObject textRo) return;

        if (!textRender.TryGetCaretRect(out float caretLocalTop, out float caretHeight)) return;

        // Content-space Y: sum Y offsets up to the RenderViewport, the same walk as Scrollable.ComputeContentSpaceY.
        float contentY = 0f;
        RenderObject? ro = textRo;
        while (ro is not null && ro is not RenderViewport)
        {
            contentY += ro.Y;
            ro = ro.Parent;
        }
        if (ro is not RenderViewport) return;   // no scrollable ancestor found — nothing to do

        float caretTop = contentY + caretLocalTop;
        float caretBottom = caretTop + caretHeight;

        float offset = sharedScrollController.Offset;
        float viewportHeight = sharedScrollController.ViewportSize;
        if (viewportHeight <= 0f) return;
        float max = sharedScrollController.MaxScrollExtent;

        // Margin from the viewport edge so first/last-row input borders aren't clipped flush; the final Clamp to [0, max] keeps it from scrolling past the content.
        const float caretEdgeMargin = 8f;

        // Single winning outcome — never both guards (that is what oscillated for a tall row).
        float newOffset = offset;
        if (caretBottom + caretEdgeMargin > offset + viewportHeight)
        {
            newOffset = caretBottom + caretEdgeMargin - viewportHeight;   // caret below the view → bottom-align (+ margin)
        }
        else if (caretTop - caretEdgeMargin < offset)
        {
            newOffset = caretTop - caretEdgeMargin;                       // caret above the view → top-align (+ margin)
        }

        newOffset = Math.Clamp(newOffset, 0f, max);
        if (Math.Abs(newOffset - offset) < 0.5f) return;   // matches Scrollable's no-op guard
        sharedScrollController.JumpTo(newOffset);
    }

    /// <summary>Asks <see cref="OnRenderGUI"/> to clamp the shared scroll offset to the new MaxScrollExtent after the next layout, after a row removal shrinks the content.</summary>
    private void RequestClampToExtent()
    {
        pendingClampToExtent = true;
        clampToExtentFrames = 0;
        TraceScroll("request-clamp");
    }

    public override void OnGuiClosed()
    {
        // Workaround for a gui@3.1.0 bug: ButtonState.PlaySound() calls Element.Owner.GetSoundPlayer() in a SetState callback that can fire after unmount (e.g. the close click), when Owner is null -> NRE. A silent player keeps it non-null.
        BuildOwner.SetSoundPlayer(new SilentSoundPlayer(capi));

        CommitTitleIfEditing();
        if (isEditorMode)
        {
            if (focusedEditIndex is { } closeIdx) NormalizeRowOnCommit(closeIdx);
            // Same empty-task cleanup as switch-to-read.
            PurgeEmptyRowsFromScratch();
            pendingEmptyRowRemoval = null;
            FlushIfDirty();
            StopAutosaveTick();
            DisposeFocusNodes();
        }
        // Release on EVERY close, not only in editor mode: a player who switched tabs before closing would otherwise leak the server's lock holder. ReleaseLock is idempotent and UID-guarded server-side.
        SendReleaseLockPacket();
        modSystem.MyPinsChanged -= OnMyPinsChanged;
        modSystem.SettingsVisibilityChanged -= OnSettingsVisibilityChanged;
        modSystem.MyAssignmentsChanged -= OnMyAssignmentsChanged;
        modSystem.KnownPlayersChanged -= OnKnownPlayersChanged;
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
        // The dialog owns the shared scroll controller, not either view's State (which remount per view switch).
        sharedScrollController.Dispose();
        // The dialog owns the collapse registries (the container owns its ghost cache).
        editorCollapseRegistry.Dispose();
        pinCollapseRegistry.Dispose();
        readCollapseRegistry.Dispose();
        // Unregister the tracker engine's poll/callback and carried-inventory events.
        TeardownTrackerEngine();
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
