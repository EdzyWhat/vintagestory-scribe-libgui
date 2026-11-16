## Why

Row actions are spread across hover buttons that differ by view: read rows offer pin, editor rows offer
pin and delete, and Pin Tab rows offer delete and unpin. Dated tasks (see `docs/calendar-roadmap.md`)
need several more per-task actions (add, change and clear a date, repeat yearly), and hover buttons have no
room for them. A right-click menu gives every document row one consistent place for its actions, and the
calendar's day cells will reuse it.

## What Changes

- Right-clicking a document row (any block kind: tasks, notes, trackers, links, crafts, quest objectives)
  in a Scribe dialog's read view, editor view or Pin Tab opens a small floating menu at the row.
- First menu entries: **Edit** (read view only: enter the editor with that row focused), **Pin** or
  **Unpin**, and **Delete**.
- An entry is shown only when the action is allowed for that row in that view (for example no Edit or
  Delete on a hard or fired Tablet, no Edit while already in the editor, no Pin where pinning is not live).
  A menu with no allowed entries does not open.
- Read-view **Delete** is new: the read view has no delete control today. It deletes by task identity, the
  way the Pin Tab does, but is refused with the existing "locked" message while another player holds that
  document's editor lock.
- Edit, Pin, Unpin and editor/Pin Tab Delete reuse the existing code paths and server messages.
- A right-click on a row never also performs a left-click action (it does not toggle a checkbox, open a
  link, or move the text caret).
- The hover buttons stay. The menu is an addition, not a replacement.
- The menu closes on choosing an entry, clicking outside it, pressing Escape (which then does not close the
  dialog), switching views, closing the dialog, or its row going away.
- Not included: **Assign** (the Create Assignments tab is specified as the only place assignments are
  created, so a menu entry needs its own design), date entries (added by the Notebook dating change), the
  HUD, the Task Notice, and Inbox previews.

## Capabilities

### New Capabilities
- `task-row-context-menu`: the right-click menu on document rows in Scribe dialogs, its entries, when each
  entry is shown, how right-clicks reach it, and how it opens and closes.

### Modified Capabilities
None. Existing hover controls and their requirements are unchanged.

## Impact

- Read rows (`ScribeReadRow` in `ScribeReadContent.cs`), editor rows (`ScribeEditRow`) and Pin Tab rows
  (`ScribePinnedContent.cs`), the row children that handle presses (checkbox, link name, collapse caret,
  hover buttons, the read-only tablet's text detector) and `ScribeMultilineField`, which forward a
  right-button press to the row instead of acting on it.
- A new shared menu widget built on the floating-menu pattern in `ScribeAddKindPicker`, owned once per
  dialog by `ScribeDialogBase`, which also intercepts Escape while a menu is open.
- `ScribeDeleteTaskMessage` gains an optional "respect the editor lock" field for read-view Delete; the Pin
  Tab keeps sending it unset, so its behavior is unchanged.
- Dialogs and the views they have:

  | Dialog | Read | Editor | Pin Tab |
  |---|---|---|---|
  | Notebook, Clockmaker's Notebook | yes | yes | yes |
  | Lectern, Chalkboard, Scriptorium | yes | yes | yes |
  | Tablet (wet) | no | yes | no |
  | Tablet (hard or fired) | yes, read-only | no | no |
  | Assignment Desk | no | yes | no |

- New lang keys for the entry labels in `en.json` and the 12 other locales, and an updated Editor Features
  handbook entry.
- No save-format or Core changes. A pure entry-gating helper gets unit tests.
