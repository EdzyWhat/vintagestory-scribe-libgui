## Why

The dated-task core stores a date and repeat flag on blocks, but no screen lets a player set, see or clear
one. This change is the first player-facing slice of the calendar roadmap (`docs/calendar-roadmap.md`,
change 3): dating a plain task from its row menu, a chip that shows the date and where the task stands,
and a date picker. Reminders, HUD badges and rollover follow in later changes.

## What Changes

- The right-click row menu gains a date group for **datable tasks** (plain Tasks not placed by an
  assignment) in the read and editor views of the Notebook, Clockmaker's Notebook, Lectern, Chalkboard and
  Scriptorium:
  - **Add date** on an undated task, **Change date** and **Clear date** on a dated one.
  - **Repeat yearly**, a checkbox entry shown only on a dated task. It stays in the menu when chosen and
    shows its new state.
- Choosing Add date or Change date opens a **date picker** at the row: a month stepper, a day stepper and a
  year stepper, starting at the task's date or today, with Set and Cancel.
- A dated task shows a **date chip** under its text in the read and editor views: the formatted date (the
  player's Date order, years from 1) and where the task stands: "Due today", "in 3 days", "Overdue (2 days)".
  The text is steady, with warm colors for due and overdue, and a completed task shows only its date.
- Setting, changing, clearing and repeating a date work in the editor view through the existing editor
  save, and in the read view through a new identity-addressed message, one operation per message, that the
  server refuses while another player holds the document's editor lock. Every server refusal re-sends the
  authoritative document to the sender, so an optimistic read-view change reverts.
- A task that repeats yearly is never **deleted** by a completion: under the Delete completion policy it is
  completed and sunk like the Sink policy, and every other policy acts as the player chose, so the task can
  roll over in a later change. A parent group containing one is protected as a whole, and the Discard
  children behavior keeps the children of such a group.
- Dates are not offered on Tablets, the Assignment Desk, the Pin Tab, the HUD, the Task Notice or Inbox
  previews. Other block kinds keep any date they carry (from an import) in the data, but show no chip and
  get no date entries.
- **Not included:** reminders, due and overdue behavior outside the notebook rows, HUD and Pin Tab badges
  (the pin snapshot has no date yet), repeat rollover, the calendar block and its dialog, and a month
  dropdown in the picker.
- **Order:** `add-dated-task-core` and `add-task-row-context-menu` must be archived before this change,
  because this change modifies requirements they introduce.

## Capabilities

### New Capabilities
- `notebook-task-dating`: dating datable tasks from the row menu, the date picker, the date chip, who may
  change a date, and how repeating tasks complete.

### Modified Capabilities
- `player-pins`: "Complete a pinned task by stable identity" gains the repeating-task exception.
- `pinned-task-tab`: "The Pin Tab exposes the completion-policy control" notes that a repeating task is
  never deleted.
- `scribe-dialog-base`: "Read-view completion applies the completion policy locally and immediately"
  states that the shared Core completion function treats Delete as Sink for a repeating task.

The `task-row-context-menu` requirements from change 1 are not yet in `openspec/specs/`; that change's own
spec is amended in place to allow the date entries.

## Impact

- Core: the row-menu entry gating gains date actions; a date picker model (field stepping and day
  clamping), the chip's status-to-text mapping and `ScribeDocument` date operations are pure and tested; the
  completion rules honor a repeating task (`ScribeCompletion.EffectivePolicy`).
- Mod: the row menu host gains a toggle entry, `RefreshItems` and ownership of the picker; a new date picker
  overlay; a chip on read and editor rows; a new `ScribeSetTaskDateMessage` (client to server) handled next
  to the delete handler; dialogs override a "dates supported" switch (off for Tablets and the Assignment
  Desk); the read and editor row snapshots carry the chip data.
- 16 new lang keys in `en.json` and the 12 other locales (menu entries, picker labels, chip templates and
  status), listed in `design.md`.
- Handbook text (Editor Features) mentions dates.
- No save-format changes: dates are already in the v14 binary, JSON v3 and TSV formats.
