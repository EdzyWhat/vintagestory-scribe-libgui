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
        var block = RowMenuBlock(view, taskId);
        var pin = view == ScribeRowMenuView.PinTab ? PinForRowMenu(taskId) : null;
        bool canDate = SupportsRowDates && (view == ScribeRowMenuView.PinTab
            ? pin is not null && ScribeDateRules.PinCanDate(pin) && !IsCarriedTabletDocument(pin.OwnerDocId)
            : block is not null && ScribeDateRules.IsDatable(block) && !(view == ScribeRowMenuView.Read && ReadViewIsReadOnly));
        bool hasDate = view == ScribeRowMenuView.PinTab ? pin?.Date is not null : block?.Date is not null;
        var actions = ScribeRowMenuEntries.For(view, ReadViewIsReadOnly, pinLive, isPinned,
            canDate, hasDate, allowRepeat: view != ScribeRowMenuView.PinTab);

        // Delete sits in its own group; Edit and the date group follow the document
        // while the menu is open (Refreshable), Pin or Unpin and Delete keep what the menu opened with.
        return actions
            .Select((action, i) =>
            {
                bool toggle = action == ScribeRowMenuAction.RepeatYearly;
                return new ScribeRowMenuItem(
                    RowMenuLabelKey(action),
                    () => RunRowMenuAction(view, action, taskId),
                    CloseOnChoose: !toggle,
                    SeparatorBefore: i > 0 && action == ScribeRowMenuAction.Delete,
                    Checked: toggle ? block?.RepeatYearly == true : null,
                    Refreshable: action == ScribeRowMenuAction.Edit || action is ScribeRowMenuAction.AddDate
                        or ScribeRowMenuAction.ChangeDate or ScribeRowMenuAction.ClearDate or ScribeRowMenuAction.RepeatYearly);
            })
            .ToList();
    }

    /// <summary>True when a Tablet the player carries holds the document <paramref name="docId"/>. Tablet tasks
    /// take no dates, so a pin from one offers no date entries.</summary>
    private bool IsCarriedTabletDocument(Guid docId)
    {
        foreach (var inv in capi.World.Player.InventoryManager.InventoriesOrdered)
        {
            IEnumerable<Vintagestory.API.Common.ItemSlot> slots;
            try { slots = new List<Vintagestory.API.Common.ItemSlot>(inv); }
            catch { continue; }
            foreach (var slot in slots)
            {
                if (slot?.Itemstack?.Collectible is not ItemScribeTablet) continue;
                if (ScribeDocumentAttributes.TryReadFrom(slot.Itemstack, out var doc) && doc?.DocId == docId) return true;
            }
        }
        return false;
    }

    /// <summary>The pin behind a Pin Tab row, by task id.</summary>
    private ScribePinnedRef? PinForRowMenu(Guid taskId) => modSystem.MyPins.FirstOrDefault(p => p.TaskId == taskId);

    /// <summary>The block a row menu in <paramref name="view"/> acts on: the live document's in the read view, the
    /// scratch copy's in the editor, none on the Pin Tab (its rows act on the pin snapshot).</summary>
    private ScribeBlock? RowMenuBlock(ScribeRowMenuView view, Guid taskId) => view switch
    {
        ScribeRowMenuView.Read => host.Document.FindByTaskId(taskId),
        ScribeRowMenuView.Editor => scratch?.FindByTaskId(taskId),
        _ => null,
    };

    private static string RowMenuLabelKey(ScribeRowMenuAction action) => action switch
    {
        ScribeRowMenuAction.Edit => "scribe:scribe-gui-rowmenu-edit",
        ScribeRowMenuAction.Pin => "scribe:scribe-gui-rowmenu-pin",
        ScribeRowMenuAction.Unpin => "scribe:scribe-gui-rowmenu-unpin",
        ScribeRowMenuAction.AddDate => "scribe:scribe-gui-rowmenu-adddate",
        ScribeRowMenuAction.ChangeDate => "scribe:scribe-gui-rowmenu-changedate",
        ScribeRowMenuAction.ClearDate => "scribe:scribe-gui-rowmenu-cleardate",
        ScribeRowMenuAction.RepeatYearly => "scribe:scribe-gui-rowmenu-repeatyearly",
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
            default:
                RunDateAction(ScribeRowMenuView.Read, action, taskId);
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
            default:
                RunDateAction(ScribeRowMenuView.Editor, action, taskId);
                break;
        }
    }

    // ---------------- Dates ----------------

    /// <summary>Runs a date entry: Add and Change date open the picker; Clear date and Repeat yearly apply at once.</summary>
    private void RunDateAction(ScribeRowMenuView view, ScribeRowMenuAction action, Guid taskId)
    {
        var block = RowMenuBlock(view, taskId);
        if (block is null || !ScribeDateRules.IsDatable(block)) return;

        switch (action)
        {
            case ScribeRowMenuAction.AddDate:
            case ScribeRowMenuAction.ChangeDate:
                OpenDatePicker(view, taskId, block);
                break;
            case ScribeRowMenuAction.ClearDate:
                ApplyRowDate(view, taskId, ScribeDateOperation.ClearDate, default, repeat: false);
                break;
            case ScribeRowMenuAction.RepeatYearly:
                ApplyRowDate(view, taskId, ScribeDateOperation.SetRepeat, default, repeat: !block.RepeatYearly);
                break;
        }
    }

    /// <summary>Opens the date picker on the row, starting at its date or today. A read view refuses first while another
    /// player holds the editor lock, so the picker never opens for a change the server would refuse.</summary>
    private void OpenDatePicker(ScribeRowMenuView view, Guid taskId, ScribeBlock block)
    {
        if (view == ScribeRowMenuView.Read && RefuseWhileLockedByOther()) return;
        var today = ScribeWorldCalendar.Today(capi.World);
        var model = new ScribeDatePickerModel(ScribeWorldCalendar.Shape(capi.World), block.Date ?? today);
        RowMenu.OpenPicker(taskId, model, today, date => ApplyRowDate(view, taskId, ScribeDateOperation.SetDate, date, repeat: false));
    }

    /// <summary>Shows the standard locked message and returns true when another player holds the editor lock.</summary>
    private bool RefuseWhileLockedByOther()
    {
        if (!host.IsLockedByOther(capi.World.Player.PlayerUID)) return false;
        capi.TriggerIngameError(this, "scribe-lectern-locked", Lang.Get("scribe:scribe-gui-locked"));
        return true;
    }

    /// <summary>Applies a date operation to a row. The editor changes the scratch copy, saved with the rest of the editor's
    /// edits. The read view applies it optimistically to a codec round-trip copy and sends it to the server, whose
    /// authoritative document (or its re-send after a refusal) replaces the copy.</summary>
    private void ApplyRowDate(ScribeRowMenuView view, Guid taskId, ScribeDateOperation operation, ScribeDate date, bool repeat)
    {
        if (view == ScribeRowMenuView.Editor)
        {
            if (scratch is null || !ApplyDateOperation(scratch, taskId, operation, date, repeat)) return;
            isDirty = true;
            // The row is reused by the rebuild, so re-home focus through pendingFocusRow rather than mount-only autoFocus.
            if (focusedEditIndex is { } idx && idx < editorFocusNodes.Count) pendingFocusRow = idx;
            RebuildBody();
            return;
        }

        if (RefuseWhileLockedByOther()) return;

        var bytes = ScribeDocumentCodec.Serialize(host.Document);
        if (ScribeDocumentCodec.TryDeserialize(bytes, out var copy) && copy is not null
            && ApplyDateOperation(copy, taskId, operation, date, repeat))
        {
            host.ApplyLocalOptimisticEdit(copy);
            RefreshReadView();
        }

        SendTaskDate(host.Document.DocId, taskId, operation, date, repeat);
    }

    /// <summary>Sends a date operation for the task <paramref name="taskId"/> in the document
    /// <paramref name="docId"/>. The server resolves the document, enforces the editor lock and answers with the
    /// authoritative document or pin set.</summary>
    private void SendTaskDate(Guid docId, Guid taskId, ScribeDateOperation operation, ScribeDate date, bool repeat)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeSetTaskDateMessage
        {
            DocId = docId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Operation = (byte)operation,
            Year = operation == ScribeDateOperation.SetDate ? date.Year : 0,
            Month = operation == ScribeDateOperation.SetDate ? date.Month : 0,
            Day = operation == ScribeDateOperation.SetDate ? date.Day : 0,
            Repeat = repeat,
        });
    }

    private static bool ApplyDateOperation(ScribeDocument doc, Guid taskId, ScribeDateOperation operation, ScribeDate date, bool repeat) =>
        operation switch
        {
            ScribeDateOperation.SetDate => doc.TrySetTaskDate(taskId, date),
            ScribeDateOperation.ClearDate => doc.TryClearTaskDate(taskId),
            ScribeDateOperation.SetRepeat => doc.TrySetTaskRepeat(taskId, repeat),
            _ => false,
        };

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
            case ScribeRowMenuAction.AddDate:
            case ScribeRowMenuAction.ChangeDate:
                if (!ScribeDateRules.PinCanDate(pin) || IsCarriedTabletDocument(pin.OwnerDocId)) break;
                var today = ScribeWorldCalendar.Today(capi.World);
                var model = new ScribeDatePickerModel(ScribeWorldCalendar.Shape(capi.World), pin.Date ?? today);
                RowMenu.OpenPicker(taskId, model, today,
                    date => SendTaskDate(pin.OwnerDocId, taskId, ScribeDateOperation.SetDate, date, repeat: false));
                break;
            case ScribeRowMenuAction.ClearDate:
                if (ScribeDateRules.PinCanDate(pin) && !IsCarriedTabletDocument(pin.OwnerDocId))
                    SendTaskDate(pin.OwnerDocId, taskId, ScribeDateOperation.ClearDate, default, repeat: false);
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
        if (rowMenuHost is { } menu)
        {
            if ((menu.IsOpen || menu.IsPickerOpen) && ScribeRowMenuRules.ClosesOnViewChange(rowMenuOpenedIn, RowMenuView)) menu.CloseAll();
            // A read-view picker whose Set would be refused (another player took the editor lock) closes.
            if (menu.IsPickerOpen && ScribeRowMenuRules.PickerShouldClose(true, false, rowMenuOpenedIn,
                    host.IsLockedByOther(capi.World.Player.PlayerUID))) menu.ClosePicker();
            menu.Tick();
        }

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

    /// <summary>Escape closes the open date picker or row menu and nothing else: the dialog stays open and the focused field keeps its text.</summary>
    private bool TryCloseRowMenuOnEscape() => rowMenuHost?.CloseTopmost() ?? false;
}
