## Why

A dated task shows its date chip in the Notebook, but pinning it drops the date: the Pin Tab and the HUD
render from the pin's saved snapshot, and the snapshot carries only text, done state and the other
fields the rows needed before dates existed. The player pinned the task to keep it in view and loses the
one fact that made it time-sensitive.

## What Changes

- A pin's snapshot gains the task's date. It is saved and synced with the pin (the pin blob format gains a
  version) and captured when the task is pinned.
- The snapshot's date follows the task. It refreshes when the document is saved, when a date is set,
  changed or cleared from a read view, and when another player changes it, for every player who has the
  task pinned. Text and done state keep their existing rule (only the acting player's own edit
  refreshes them).
- Pin Tab rows and HUD rows of a plain, datable task show the same date chip the Notebook shows, in the
  player's Date order, with years counted from 1: status and date for an unchecked task, the date alone
  (dimmed) for a checked one. Rows of other kinds and assignment tasks show none.
- The HUD refreshes its chips when the in-game day changes, so "Due today" and "in 2 days" stay true on a
  HUD left open for days.
- The Pin Tab shows the chip only in dialogs that offer dates (not a Tablet or the Assignment Desk); the HUD
  always does. An orphaned pin shows none.
- Completion is not touched: the repeat flag does not change how a pinned task completes (see
  `add-notebook-task-dating`), so the snapshot does not carry it.
- Not included: reminders, chat alerts, reminder settings, the repeat rollover itself, the calendar block,
  and any change to pin ordering.

## Capabilities

### New Capabilities
None.

### Modified Capabilities
- `player-pins`: the pin snapshot carries and refreshes the date for every holder.
- `notebook-task-dating`: the chip is also shown on the Pin Tab and the HUD, and the HUD rebuilds on a day
  change.
- `pinned-task-tab`: Pin Tab rows show the date chip (gated on the dialog offering dates).
- `pinned-task-hud`: HUD rows show the date chip and keep it current across days and the Date order.

## Impact

- **Core:** `ScribePinnedRef` gains `Date`; `ScribePinCodec` writes and reads them in a
  new version (older blobs load without them; the codec doc and `docs/CODEC-MIGRATION.md` get a section);
  `ScribeDateRules` gains a classify overload that takes plain values so a pin can use it;
  Tests for each.
- **Server:** `ScribePinStore` (capture on pin, refresh on reconcile, a document-wide date refresh),
  `ScribeSetPinMessage` (the client fallback snapshot gains the date for documents the server cannot
  resolve), `ScribeModSystem` pin and date operations, the server paths that persist a document (the read-view date operation, the Lectern and Chalkboard editor
  save, the Notebook whole-document save, the external append; the tasks include an audit of every other
  persist site).
- **Client:** the shared chip builder moves out of `ScribeDialogBase` so the HUD and Pin Tab use it;
  `ScribePinRowData` and `HudPinRow` gain a chip; the Pin Tab row, the HUD row, their deleted-row
  snapshots, and the HUD's day-change rebuild.
- **Depends on** `add-notebook-task-dating` (the chip and the date operations) being
  archived first, because the deltas here restate requirements that change modifies and the
  `notebook-task-dating` capability does not exist in the main specs until it is. If it is archived with
  different wording, reconcile the headers and keep the superset bodies.
- No new mod dependencies. New lang keys only if the chip needs a string the Notebook chip lacks (none are
  expected).
