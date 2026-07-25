## Why

Scribe pins are addressed by stable identity `(DocId, TaskId)` and can already be completed and
delete-on-completed across documents server-authoritatively (`CompleteTaskForPlayer` →
`TryResolveLectern` → `SetTaskDoneFromReader`/`DeleteTaskFromReader`), driven today from the
always-on corner HUD. But the HUD is a tiny glanceable view with a client-side undo delay, and the
only cross-document mutations that exist are *complete* and *delete-bundled-in-the-Delete-policy*.
There is no way to **edit a pinned task's text**, to **delete or unpin a pin as a standalone
action**, or to **reorder** a player's pins — pins are insertion-ordered only. Players who track
goals across many lecterns need one place to manage every pin they hold, with full edit affordances
and no undo lag.

This change (Phase 4, the largest and riskiest) adds a slide-out **pin-editor pagelet** on the
Lectern that lists all of a player's pins across every document, with per-row complete, inline text
edit, delete, unpin, and reorder, and **no undo delay**. It complements — does not replace — the
corner HUD (the HUD keeps its glanceable role and its undo window; both read the same pin set). The
UI needs a thin, precedent-following server-authoritative sync extension: new identity-addressed
messages for edit-text and reorder (and standalone delete/unpin), mirroring the existing
`CompleteTaskForPlayer` template, with the same accepted best-effort behavior on unloaded sources.

## What Changes

- **Pin edit-text by identity (new):** a client can request that a pinned task's text be changed,
  addressed by `(DocId, TaskId)`. Best-effort write-through — the source document is updated when its
  chunk is loaded/resolvable and the pin snapshot is always updated; when the source is unresolvable
  it degrades to snapshot-only (mirroring how the Delete completion policy already behaves). Lock-free,
  mirroring `SetTaskDoneFromReader`. Requires a new BE method `SetTaskTextFromReader(Guid, string)`
  (no such method exists today).
- **Standalone delete / unpin by identity (new as actions):** delete-a-task and unpin-a-pin become
  first-class actions addressed by `(DocId, TaskId)`, reusing the existing lock-free
  `DeleteTaskFromReader` + `ScribePinStore.RemovePin`. (Delete exists today only bundled inside the
  Delete completion policy; unpin already never touches a block.)
- **Reorder the per-player pin list (new):** a client can reorder its own pin list; the server
  permutes that player's list in `ScribePinStore`, persists it (already saved under `scribe:pins:v1`),
  and re-pushes. This reorders the **per-player pin list only** — it does **not** reorder document
  block order.
- **New wire messages (additive, not BREAKING):** `ScribeEditPinnedTaskMessage` and
  `ScribeReorderPinsMessage` (plus optional standalone delete/unpin), appended to the frozen message
  registration order in `ScribeModSystem.Start`. New messages are additive; no existing message's wire
  format changes, and the persisted pin format is unchanged (order is already a persisted list).
- **New slide-out pin-editor pagelet (`ScribePinTray`):** a `Positioned` + `AnimatedSlide` + `Clip`
  pagelet on the Lectern, opened via a handle, listing all of the player's pins with the full edit
  treatment and **no undo timer**. Reuses `ScribeMultilineField` for inline edit and the
  `HudPinsContent`/`HudPinRow` row template.
- **Optional Core convenience:** a tiny pure-data `ScribeDocument.SetTaskText(Guid, string)` over the
  existing `FindByTaskId` + `SetBlockText` — no VS API. Kept minimal.

No **BREAKING** changes: the new messages are additive, the pin persistence format is unchanged, and
the HUD's behavior (including its undo window) is untouched.

## Capabilities

### New Capabilities

- `pin-editor-tray` (`specs/pin-editor-tray/spec.md`): a slide-out pin-editor pagelet on the Lectern
  that lists all of the player's pins across every document, with per-row complete / inline text edit /
  delete / unpin / reorder and no undo delay; complements the corner HUD (both read the same pin set,
  HUD behavior unchanged); slides in/out via a handle and stays interactive and hit-testable while
  sliding.

### Modified Capabilities

- `player-pins` (`specs/player-pins/spec.md`, ADDED requirements only): adds edit-a-pinned-task's-text
  by identity (best-effort write-through, lock-free, snapshot-only degrade), standalone delete-by-identity
  as an action, reorder of the per-player pin list (persisted per-player, re-synced, not document block
  order), and an explicit requirement that mutating an unloaded document's source is best-effort /
  snapshot-only because no chunk force-load exists.
- `task-note-document`: gains at most an optional pure-data `SetTaskText(Guid, string)` convenience over
  the existing lookup + set-text operations. This is a Core-only data helper (no VS API, no format
  change) and adds no new normative requirement to the capability; it is called out here only because it
  touches `src/Core/ScribeDocument.cs`.

## Impact

- **Core (`src/Core/`)**: optional `ScribeDocument.SetTaskText(Guid, string)` over existing
  `FindByTaskId`/`SetBlockText` — pure data, unit-testable, no VS API.
- **Mod (`src/Mod/`)**: new `SetTaskTextFromReader(Guid, string)` on `BlockEntityScribeLectern`
  (mirrors `SetTaskDoneFromReader`); new `ScribeEditPinnedTaskMessage` / `ScribeReorderPinsMessage`
  (+ optional standalone delete/unpin) and their server handlers in `ScribeModSystem` mirroring
  `CompleteTaskForPlayer`; reorder + persist support in `ScribePinStore`; new `ScribePinTray.cs`
  slide-out widget; the Lectern dialog (`GuiDialogScribeLecternLibGui`) wires in the tray + handle
  and the edit/delete/unpin/reorder senders.
- **Assets**: `assets/scribe/lang/en.json` gains pin-tray / handle / row-action labels; pin-tray art
  PNGs are progressive swaps (flat placeholders until then), not required by this change.
- **Persistence / wire**: additive only — no persisted-format break (pin list order is already a
  persisted `List`), new messages appended to the frozen registration order.
- **Dependencies / composition**: LibGUI (`gui`) is the existing hard dep; `AnimatedSlide`,
  `Positioned`, `Clip`, `ScribeMultilineField` already ship. This change composes visually with
  `scribe-themed-toggle` / `scribe-gui-backdrops` / `scribe-animated-tabs` (the pagelet renders under
  whichever theme/backdrop/tab shell is active) but the **sync extension is independent** of all three
  and does not require them to land first.
- **Verification**: in-game only — the Core suite cannot reach `src/Mod` GUI code or the VS API.
