## Why

A pinned task shows its date on the Pin Tab, but right-clicking the row offers only Unpin and Delete. A
player who sees a due date on a pin has to go back to the task's Notebook to add, change or clear it. The
Pin Tab is where players work from pinned tasks, so it should date them too.

## What Changes

- The Pin Tab row menu gains **Add date** (undated task) or **Change date** and **Clear date** (dated task)
  for a live pin of a plain Task, in a dialog that offers dates. The date picker opens at the pin's date or
  today, exactly as in the Notebook.
- The Pin Tab sends the existing date message by `(DocId, TaskId)`, with no editor lock held by the sender.
  The server applies the same rules as for a read view: it refuses while another player holds the editor
  lock, on a read-only document, for a task that is not datable, and when it cannot find the document.
- The pin's chip changes when the server's date refresh reaches the player. The Pin Tab makes no optimistic
  change.
- **Repeat yearly** is not offered on the Pin Tab, because a pin does not carry the repeat flag.
- Orphaned pins, non-Task pins, and Pin Tabs in the Tablet and the Assignment Desk get no date entries.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `pinned-task-tab`: Pin Tab rows can be dated from their row menu.
- `notebook-task-dating`: "Date entries in the row menu" no longer excludes the Pin Tab. This delta restates
  the requirement from `add-notebook-task-dating`, which archives first, followed by
  `add-pinned-task-dates`.

## Impact

- Core: `ScribeRowMenuEntries.For` offers the date group on the Pin Tab when `canDate` is true.
  `ScribeDateRules` gains a pin-side predicate for "this pin can be dated".
- Mod: `ScribeDialogBase.RowMenu.cs` builds the Pin Tab date entries from the pin snapshot and sends the
  date message with the pin's `OwnerDocId`.
- No save-format, message-format or lang changes. The existing date, picker and menu strings are reused.
- Depends on the committed `add-notebook-task-dating` and `add-pinned-task-dates`.
