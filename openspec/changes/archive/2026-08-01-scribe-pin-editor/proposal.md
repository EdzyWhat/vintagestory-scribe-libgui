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

This change adds a **Pin Tab** — a nav-column view in the Lectern dialog, reached from the existing
`scribepin` navigation button — that lists all of a player's pins across every document, with rows
**editable by default** offering per-row complete, edit-text, delete, unpin, and reorder, and **no undo
delay**. It also surfaces the "on completing a task" completion-policy control directly on the tab
(where that choice's effect is most visible). It complements — does not replace — the corner HUD (the
HUD keeps its glanceable role, automatic ordering, and undo window; both read the same pin set). The UI
extends the editor view's row rendering but sourced from the player's pin set instead of the current
document, and needs a thin, precedent-following server-authoritative sync extension: new
identity-addressed messages for edit-text and reorder (and standalone delete/unpin), mirroring the
existing `CompleteTaskForPlayer` template, with the same accepted best-effort behavior on unloaded
sources.

> **UI pivot (2026-07-26):** this change originally designed a slide-out `ScribePinTray` pagelet. It is
> retargeted to a **nav-column Pin Tab view** (a peer of the read/editor views selected from the
> `scribepin` nav button), matching the shipped `scribe-notebook-frame` vertical right-column nav — the
> horizontal-tab and slide-out-tray concepts are superseded. The server/store sync plumbing below is
> unchanged by the pivot; only the UI shell changed (a central-region view swap rather than an
> `AnimatedSlide` overlay).

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
- **New Pin Tab nav view:** a new central-region view in `GuiDialogScribeLecternLibGui`, selected from
  the existing `scribepin` nav button (its `onTap` is currently a stub), a peer of the read/editor views
  (`BuildCentralRegion` gains a pinned branch). It lists all of the player's pins with rows **editable by
  default** and **no undo timer**, extending the editor view's row rendering (`ScribeEditRow` =
  `Checkbox` + `Expanded(ScribeMultilineField)` + hover delete/unpin/grip) but fed from `modSystem.MyPins`
  instead of the document. Rows show all pins with **no max-row cap** (unlike the HUD).
- **Completion-policy control on the tab:** the Scribe Settings "on completing a task"
  (`ScribeCompletionPolicy`) picker is also rendered on the Pin Tab, editing the same shared
  `ScribePlayerSettings.CompletionPolicy` preference (one value, two hosts).
- **Optional Core convenience:** a tiny pure-data `ScribeDocument.SetTaskText(Guid, string)` over the
  existing `FindByTaskId` + `SetBlockText` — no VS API. Kept minimal.

No **BREAKING** changes: the new messages are additive, the pin persistence format is unchanged, and
the HUD's behavior (including its undo window) is untouched.

## Capabilities

### New Capabilities

- `pinned-task-tab` (`specs/pinned-task-tab/spec.md`): a Pin Tab nav view in the Lectern (a peer of the
  read/editor views, opened from the `scribepin` nav button) that lists all of the player's pins across
  every document with no max-row cap, rows editable by default with per-row complete / edit text / delete
  / unpin / reorder and no undo delay, plus the completion-policy control; fulfills the manual reorder /
  manual unpin the HUD defers to it; complements the corner HUD (both read the same pin set, HUD behavior
  unchanged).

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
  `CompleteTaskForPlayer`; reorder + persist support in `ScribePinStore`; the Lectern dialog
  (`GuiDialogScribeLecternLibGui`) gains a Pin Tab view — a new view-mode field + a `BuildPinnedContent()`
  branch in `BuildCentralRegion` that adapts the editor `ScribeEditRow` rendering to source from
  `modSystem.MyPins`, the `scribepin` nav-button `onTap` wired to switch to it, the completion-policy
  picker, and the edit/delete/unpin/reorder senders.
- **Assets**: `assets/scribe/lang/en.json` gains Pin Tab / row-action / policy-picker labels (the
  `scribe-gui-nav-pinned` nav tooltip already exists).
- **Persistence / wire**: additive only — no persisted-format break (pin list order is already a
  persisted `List`), new messages appended to the frozen registration order.
- **Dependencies / composition**: LibGUI (`gui`) is the existing hard dep; `ScribeMultilineField` and the
  editor `ScribeEditRow` rendering already ship, and the `scribepin` nav button + its tooltip already exist
  (shipped `scribe-notebook-frame`). The Pin Tab is a new central-region view swap, reusing the existing
  read/editor view-switch mechanism — no slide/overlay primitives needed. It renders under whichever theme
  (`scribe-themed-toggle`) / backdrop (`scribe-gui-backdrops`) the Lectern dialog has active, and — being an
  in-dialog view — is governed by the Lectern-dialog settings (`PixelArtDisplay`, `WindowFontScale`,
  `PixelArtSize`), NOT the HUD-prefixed settings. The **sync extension is independent** of the theme/backdrop
  work and does not require it to land first. Supersedes the stale `scribe-animated-tabs` horizontal-tab-bar
  nav concept (the shipped vertical right-column nav won).
- **Verification**: in-game only — the Core suite cannot reach `src/Mod` GUI code or the VS API.
