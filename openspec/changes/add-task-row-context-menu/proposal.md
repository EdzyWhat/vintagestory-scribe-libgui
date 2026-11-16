## Why

Row actions are spread across hover buttons that differ by view: read rows offer pin, editor rows offer
pin and delete, and Pin Tab rows offer delete and unpin. Dated tasks (see `docs/calendar-roadmap.md`)
need several more per-task actions (add, change and clear a date, repeat yearly), and hover buttons have no
room for them. A right-click menu gives every task row one consistent place for its actions, and the
calendar's day cells will reuse it.

## What Changes

- Right-clicking a task row in a Scribe dialog opens a small floating menu anchored at the row.
- First menu entries: **Edit** (read view only: enter the editor focused on that row), **Pin** or
  **Unpin**, and **Delete**.
- An entry is shown only when the action is allowed for that row in that view (for example no Delete on a
  read-only Lectern, no Edit while already in the editor, no Pin where pinning is not live). A menu with no
  allowed entries does not open.
- Each entry runs the same code path as the existing control for that action, so behavior, permissions and
  server messages are unchanged.
- The hover buttons stay. The menu is an addition, not a replacement.
- The menu closes on choosing an entry, clicking outside it, pressing Escape, or the row going away.
- Not included: **Assign** (the Create Assignments tab is specified as the only place assignments are
  created, so a menu entry needs its own design), date entries (added by the Notebook dating change), and
  the HUD (the menu exists only inside dialogs).

## Capabilities

### New Capabilities
- `task-row-context-menu`: the right-click menu on task rows in Scribe dialogs, its entries, when each
  entry is shown, and how it opens and closes.

### Modified Capabilities
None. Existing hover controls and their requirements are unchanged.

## Impact

- Read rows (`ScribeReadContent`), editor rows (`ScribeEditRow`) and Pin Tab rows (`ScribePinnedContent`),
  plus a new shared menu widget built on the floating-menu pattern in `ScribeAddKindPicker`.
- Applies to every dialog that shows those rows: Notebook, Clockmaker's Notebook, Lectern, Chalkboard,
  Scriptorium, Assignment Desk and Tablet.
- No save-format, network or Core changes.
