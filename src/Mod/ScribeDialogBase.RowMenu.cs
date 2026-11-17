using System;
using System.Collections.Generic;
using System.Linq;
using Gui.Core.Framework;        // RenderObject
using Gui.Core.Scroll;           // RenderViewport
using Scribe.Core;
using Vintagestory.API.Config;   // Lang

namespace Scribe;

public abstract partial class ScribeDialogBase
{
    // ---------------- Row context menu ----------------

    private ScribeRowMenuHost? rowMenuHost;

    /// <summary>The view the open (or last opened) row menu was requested in; the menu closes when the dialog leaves it.</summary>
    private ScribeRowMenuView? rowMenuOpenedIn;

    /// <summary>The dialog's one row context menu, handed to the read, editor and Pin Tab rows. Closed when the
    /// dialog closes.</summary>
    private ScribeRowMenuHost RowMenu =>
        rowMenuHost ??= new ScribeRowMenuHost(RowMenuItems, () => RowStyle, () => currentShade);

    /// <summary>The task a read-view "Edit" entry asked the editor to focus. Set before the editor access request
    /// and consumed when access is granted; cleared when the request is refused, the dialog closes, or the task
    /// is gone.</summary>
    private Guid? pendingEditFocusTaskId;

    /// <summary>An item row (no text field) that the next editor entry scrolls into view, since it has no caret to follow.</summary>
    private Guid? pendingEditScrollTaskId;
    private int editScrollFramesLeft;

    /// <summary>The view a row menu belongs to, or null in a view whose rows have no menu.</summary>
    private ScribeRowMenuView? RowMenuView => viewMode switch
    {
        ScribeLecternView.Read => ScribeRowMenuView.Read,
        ScribeLecternView.Editor => ScribeRowMenuView.Editor,
        ScribeLecternView.Pinned => ScribeRowMenuView.PinTab,
        _ => null,
    };

    /// <summary>The menu entries for the row with <paramref name="taskId"/> in the current view. Each entry acts on the
    /// row's state when chosen, found again by its <c>TaskId</c>.</summary>
    private IReadOnlyList<ScribeRowMenuItem> RowMenuItems(Guid taskId)
    {
        if (RowMenuView is not { } view) return Array.Empty<ScribeRowMenuItem>();
        rowMenuOpenedIn = view;

        bool pinLive = view != ScribeRowMenuView.Read || !ReadViewIsReadOnly || ReadViewCompletionAndPinLive;
        bool isPinned = view == ScribeRowMenuView.PinTab || IsPinnedForMe(taskId);
        var actions = ScribeRowMenuEntries.For(view, ReadViewIsReadOnly, pinLive, isPinned);

        // Delete sits in its own group below the other entries.
        return actions
            .Select((action, i) => new ScribeRowMenuItem(
                RowMenuLabelKey(action),
                () => RunRowMenuAction(view, action, taskId),
                SeparatorBefore: i > 0 && action == ScribeRowMenuAction.Delete))
            .ToList();
    }

    private static string RowMenuLabelKey(ScribeRowMenuAction action) => action switch
    {
        ScribeRowMenuAction.Edit => "scribe:scribe-gui-rowmenu-edit",
        ScribeRowMenuAction.Pin => "scribe:scribe-gui-rowmenu-pin",
        ScribeRowMenuAction.Unpin => "scribe:scribe-gui-rowmenu-unpin",
        _ => "scribe:scribe-gui-rowmenu-delete",
    };

    private void RunRowMenuAction(ScribeRowMenuView view, ScribeRowMenuAction action, Guid taskId)
    {
        switch (view)
        {
            case ScribeRowMenuView.Read:
                RunReadRowAction(action, taskId);
                break;
            case ScribeRowMenuView.Editor:
                RunEditorRowAction(action, taskId);
                break;
            case ScribeRowMenuView.PinTab:
                RunPinRowAction(action, taskId);
                break;
        }
    }

    private void RunReadRowAction(ScribeRowMenuAction action, Guid taskId)
    {
        switch (action)
        {
            case ScribeRowMenuAction.Edit:
                EditRowFromReadView(taskId);
                break;
            case ScribeRowMenuAction.Pin:
                SetPinnedFromMenu(taskId, pinned: true);
                break;
            case ScribeRowMenuAction.Unpin:
                SetPinnedFromMenu(taskId, pinned: false);
                break;
            case ScribeRowMenuAction.Delete:
                DeleteRowFromReadView(taskId);
                break;
        }
    }

    private void RunEditorRowAction(ScribeRowMenuAction action, Guid taskId)
    {
        if (scratch is null) return;
        int index = scratch.Blocks.ToList().FindIndex(b => b.TaskId == taskId);
        if (index < 0) return;

        switch (action)
        {
            case ScribeRowMenuAction.Pin:
                if (!IsPinnedForMe(taskId)) TogglePinnedEditorTask(index);
                break;
            case ScribeRowMenuAction.Unpin:
                if (IsPinnedForMe(taskId)) TogglePinnedEditorTask(index);
                break;
            case ScribeRowMenuAction.Delete:
                DeleteEditorBlock(index);
                break;
        }
    }

    private void RunPinRowAction(ScribeRowMenuAction action, Guid taskId)
    {
        var pin = modSystem.MyPins.FirstOrDefault(p => p.TaskId == taskId);
        if (pin is null) return;

        switch (action)
        {
            case ScribeRowMenuAction.Unpin:
                OnPinUnpinTask(pin.OwnerDocId, taskId);
                break;
            case ScribeRowMenuAction.Delete:
                OnPinDeleteTask(pin.OwnerDocId, taskId);
                break;
        }
    }

    /// <summary>Pins or unpins to an explicit state, so an entry whose label went stale (the row was pinned elsewhere
    /// while the menu was open) never flips the wrong way.</summary>
    private void SetPinnedFromMenu(Guid taskId, bool pinned)
    {
        if (IsPinnedForMe(taskId) == pinned) return;
        TogglePinWithPolicy(taskId);
    }

    /// <summary>Read-view Delete: deletes by identity like the Pin Tab, but is refused while another player holds the
    /// editor lock (the server enforces it too).</summary>
    private void DeleteRowFromReadView(Guid taskId)
    {
        if (host.IsLockedByOther(capi.World.Player.PlayerUID))
        {
            capi.TriggerIngameError(this, "scribe-lectern-locked", Lang.Get("scribe:scribe-gui-locked"));
            return;
        }

        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeDeleteTaskMessage
        {
            DocId = host.Document.DocId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            SubtaskBehavior = (byte)modSystem.MySettings.SubtaskBehavior,
            RespectEditorLock = true,
        });
    }

    /// <summary>Read-view Edit: enters the editor through the usual entry and carries the row to focus through the
    /// asynchronous access grant.</summary>
    private void EditRowFromReadView(Guid taskId)
    {
        pendingEditFocusTaskId = taskId;
        TryEnterEditor();
    }

    /// <summary>Drops a pending read-view Edit focus (the access request was refused or the dialog is closing).</summary>
    public void ClearPendingEditFocus() => pendingEditFocusTaskId = null;

    /// <summary>Applies a pending read-view Edit once the editor is live: focuses the row's text field, or scrolls an
    /// item row (no text field) into view. Called from <see cref="EnterEditorMode"/> after the scratch is seeded.</summary>
    private void ApplyPendingEditFocus()
    {
        if (pendingEditFocusTaskId is not { } taskId) return;
        pendingEditFocusTaskId = null;
        if (scratch is null) return;

        int index = scratch.Blocks.ToList().FindIndex(b => b.TaskId == taskId);
        if (index < 0) return;

        if (scratch.Blocks[index].Kind is not (ScribeBlockKind.Task or ScribeBlockKind.Text))
        {
            pendingEditScrollTaskId = taskId;
            editScrollFramesLeft = 30;
        }
        else
        {
            autoFocusRowOnRebuild = index;
        }
    }

    /// <summary>Per-frame menu upkeep and the item-row scroll an Edit entry asked for. Runs after layout.</summary>
    private void ServiceRowMenu()
    {
        if (rowMenuHost is { IsOpen: true } open && rowMenuOpenedIn != RowMenuView) open.Close();
        rowMenuHost?.Tick();

        if (pendingEditScrollTaskId is not { } scrollId) return;
        if (!isEditorMode || editScrollFramesLeft-- <= 0)
        {
            pendingEditScrollTaskId = null;
            return;
        }
        if (RowMenu.RenderOf(scrollId) is not { } row) return;

        // Content-space top of the row, summed up to the scroll viewport as EnsureCaretVisible does.
        float top = 0f;
        RenderObject? ro = row;
        while (ro is not null && ro is not RenderViewport)
        {
            top += ro.Y;
            ro = ro.Parent;
        }
        if (ro is not RenderViewport) return;

        float viewport = sharedScrollController.ViewportSize;
        if (viewport <= 0f) return;
        float bottom = top + row.Size.Y;
        float offset = sharedScrollController.Offset;
        const float margin = 8f;
        float newOffset = offset;
        if (bottom + margin > offset + viewport) newOffset = bottom + margin - viewport;
        else if (top - margin < offset) newOffset = top - margin;
        newOffset = Math.Clamp(newOffset, 0f, sharedScrollController.MaxScrollExtent);
        if (Math.Abs(newOffset - offset) >= 0.5f) sharedScrollController.JumpTo(newOffset);
        pendingEditScrollTaskId = null;
    }

    /// <summary>Escape closes an open row menu and nothing else: the dialog stays open and the focused field keeps its text.</summary>
    private bool TryCloseRowMenuOnEscape()
    {
        if (rowMenuHost is not { IsOpen: true } menu) return false;
        menu.Close();
        return true;
    }
}
