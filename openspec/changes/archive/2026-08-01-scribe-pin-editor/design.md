## Context

Scribe pins are per-player `ScribePinnedRef` (`OwnerDocId`, `TaskId`, snapshot `LastKnownText/Done`),
stored server-side in `ScribePinStore` (persisted under `scribe:pins:v1`, ordered per-player by
insertion), pushed to the client via `ScribePinnedSetMessage` → `modSystem.MyPins`. Everything is
addressed by `(DocId, TaskId)`, never block position. Complete and delete-on-complete by identity
across documents **already work server-authoritatively**: `ScribeModSystem.CompleteTaskForPlayer`
(ScribeModSystem.cs:440) resolves the source via `TryResolveLectern(docId)` (ScribeModSystem.cs:496,
live index → `GetBlockEntity(pos)`) and calls `SetTaskDoneFromReader` / `DeleteTaskFromReader` on the
block entity (BlockEntityScribeLectern.cs:268/286, both lock-free). The corner HUD already drives this
from off-screen pins. This is the template every new op mirrors.

Three gaps block a full pin editor: (a) **edit task text by identity** — no `SetTaskTextFromReader` and
no message exist; (b) **standalone delete/unpin** — delete exists only bundled in the Delete completion
policy (`DeleteTaskFromReader` + `pinStore.RemovePin` are directly reusable), unpin already never
touches a block; (c) **reorder** — pins are insertion-ordered only, so a reorder must permute the
per-player list in `ScribePinStore`. A hard limit frames all write-through: **no force-load of unloaded
chunks exists** — `TryResolvePos` misses when the owning doc is unloaded and mutation degrades to
store/snapshot only, exactly how the Delete completion policy behaves today (ScribeModSystem.cs:476
degrades gracefully). The pin editor adopts that same accepted best-effort behavior.

The UI side is a **Pin Tab** — a new central-region view in the Lectern dialog, a peer of the read and
editor views, selected from the right-column `scribepin` nav button (whose `onTap` is currently a stub at
`GuiDialogScribeLecternLibGui.cs:1123-1124`). It reuses the existing read/editor view-switch mechanism
(`BuildCentralRegion` chooses the body from a view-mode field) rather than any slide/overlay primitive,
and extends the editor's `ScribeEditRow` rendering (`Checkbox` + `Expanded(ScribeMultilineField)` +
hover-conditional delete/unpin/grip) but sourced from `modSystem.MyPins` instead of the document. The
HUD's undo delay is client-only (`HudScribePins.UndoWindowMs`); the Pin Tab simply omits the sink-timer
to get no-undo for free.

> **UI pivot (2026-07-26):** originally a slide-out `ScribePinTray` pagelet (`AnimatedSlide` + `Positioned`
> + `Clip`). Retargeted to a nav-column view swap to match the shipped `scribe-notebook-frame` vertical
> right-column nav; the slide-out overlay and the `scribe-animated-tabs` horizontal-tab-bar are both
> superseded. The server/store decisions below are unchanged by the pivot.

This composes visually with `scribe-themed-toggle` and `scribe-gui-backdrops` (the view renders under
whatever theme/backdrop the Lectern dialog has active), but the **sync extension in this change is
independent** of them
and can land without them.

## Goals / Non-Goals

**Goals:**

- Add identity-addressed edit-text, standalone delete/unpin, and per-player reorder to pins, mirroring
  the existing `CompleteTaskForPlayer` precedent exactly (resolve → best-effort write-through →
  snapshot/store update → re-push).
- Add a Pin Tab nav view listing all of a player's pins across documents, rows editable by default with
  the full edit treatment and no undo delay, plus the completion-policy control, complementing the HUD.
- Keep `src/Core/` free of the VS API — any reorder/edit convenience there is pure data.
- Keep the wire and persistence formats additive/unchanged.

**Non-Goals:**

- Reordering document block order across players (`MoveBlock` is cross-player and out of scope — reorder
  is a per-player pin-list concern only).
- Force-loading chunks to make writes to unloaded sources authoritative — none exists; snapshot-only
  degrade is the accepted behavior.
- Changing the HUD's behavior, including removing its undo window.
- The theme/backdrop/tab work (separate phases); this change only renders under them.
- Any change to the pin persistence format or existing message wire formats.

## Decisions

### New ops mirror the CompleteTaskForPlayer / TryResolveLectern / SetTaskDoneFromReader precedent

Every new server op follows the established template rather than inventing a new resolution path:
resolve the source with `TryResolveLectern(docId)`, write through to the block entity when resolvable,
always update the per-player pin store/snapshot, then re-push via the existing `ScribePinnedSetMessage`
path. Rationale: this is proven, lock-free, and already handles the unloaded-source degrade. Alternative
(a generic "mutate document by docId" RPC) was rejected — it would duplicate resolution logic and invite
lock/format drift from the done-flag path.

### `SetTaskTextFromReader(Guid taskId, string text)` on `BlockEntityScribeLectern`

New BE method mirroring `SetTaskDoneFromReader` (BlockEntityScribeLectern.cs:268): lock-free, mutate the
authoritative `Document`, `MarkDirty`. It reuses the document model's set-text path so the blank/
whitespace-only rejection invariant holds. Rationale: symmetry with the existing done-flag reader keeps
the two mutation paths identical in shape and lock semantics. Alternative (route edits through the full
`ApplyEdit` lock path) was rejected — it would require the edit lock the HUD/Pin Tab deliberately avoid.

### New identity-addressed messages appended to the frozen registration order

Add `ScribeEditPinnedTaskMessage { DocId:byte[16], TaskId:byte[16], Text:string }` (C→S) and
`ScribeReorderPinsMessage` (C→S; an ordered `(DocId, TaskId)` list, or a from/to permutation) — plus,
optionally, a standalone delete/unpin message (or an action enum on the existing complete message).
Their server handlers in `ScribeModSystem` mirror `CompleteTaskForPlayer`: resolve, write through if
loaded, update the snapshot/store (edit via `pinStore.ReconcileSnapshotsForActor`; delete via
`DeleteTaskFromReader` + `pinStore.RemovePin`; reorder via the new store permute), re-push. Message
registration order in `ScribeModSystem.Start` is **frozen** — the new messages MUST be appended after
the existing ones, never inserted. Rationale: additive registration preserves wire compatibility.
Alternative (overloading an existing message with a mode field) was considered for delete/unpin but
kept optional; edit and reorder are distinct enough to warrant their own messages.

### Reorder mutates the per-player pin list in `ScribePinStore`, not document order

Reorder permutes `_pins[uid]` in `ScribePinStore`, then persists (already saved under `scribe:pins:v1`)
and re-pushes. It does **not** call `MoveBlock` or touch any document's block order. Rationale: pin order
is inherently per-player; document block order is shared across all players and reordering it from one
player's pin list would be a cross-player side effect. The store already persists an ordered `List`, so
this is a format-free change.

### Pin Tab is a central-region view swap (not an overlay), wired from the `scribepin` nav stub

The Pin Tab is a new selectable state of the Lectern dialog's central region, exactly like the read and
editor views. Add a view-mode field (promote the existing `bool isEditorMode` to a small view enum, or add
a parallel flag) and a `BuildPinnedContent()` branch in `BuildCentralRegion()`; wire the `scribepin`
nav-button `onTap` (currently the stub at `GuiDialogScribeLecternLibGui.cs:1123-1124`) to switch to it.
Follow the nav discipline the `scribe-animated-tabs` design established — a nav button routes through a
real entry method (like `OnClickSwitchToRead` / `RequestEditorAccess`), it does not flip a mode flag
inline. Rationale: this reuses the proven read/editor view-switch and matches the shipped
`scribe-notebook-frame` vertical nav; no slide/overlay primitive is needed. Alternative (the original
slide-out `AnimatedSlide` tray) was rejected — the shipped nav model is a view column, not an overlay, so a
peer view is the consistent shape.

### Reuse the editor `ScribeEditRow` rendering, fed from `MyPins`; omit the HUD sink-timer for no-undo

Rows extend the editor view's `ScribeEditRow` rendering (`Checkbox` + `Expanded(ScribeMultilineField)` +
hover-conditional delete `scribeclose` / unpin `scribepin` / drag-grip `scribegrip`) — the user's intent to
"extend the editor view as much as we can" — but the row-data source is `modSystem.MyPins`
(`ScribePinnedRef`), not the document's `ScribeEditRowData`. Rows are editable by default (no separate edit
mode). The Pin Tab deliberately **omits** the HUD's client-side undo/sink-timer (`HudScribePins.UndoWindowMs`)
so completion and every action apply immediately. Rationale: reusing the editor row keeps the surface
consistent and is the least new code; no-undo is met by simply not wiring the timer (server completion is
already instant); the Pin Tab is a deliberate management surface where the undo glance the HUD needs is
unnecessary.

### The completion-policy control appears on the Pin Tab, sharing the one preference

Render the "on completing a task" `ScribeCompletionPolicy` picker (the same control the Scribe Settings
window offers) on the Pin Tab, because the tab is where a check-off's sink/keep/unpin/delete effect is most
directly observed. It reads/writes the single shared `ScribePlayerSettings.CompletionPolicy` (client-local,
carried on each completion request) via the existing `UpdateMySettings` write-through — the control on the
tab and the one in the Settings window edit the same value. Rationale: discoverability at the point of use;
no new preference, no duplicated state.

### Divergences from the editor view the Pin Tab must honor

Because the Pin Tab reuses editor rendering but acts on per-player pins, four things differ from the editor:
1. **Lock-free commit path.** Editor rows commit via the lock-gated document autosave
   (`ScribeEditDocumentMessage`). Pin Tab actions route through the lock-free identity-addressed pin
   messages (`ScribeSetPinMessage` / `ScribeCompleteTaskMessage` / the new edit + reorder messages), never
   the document edit lock.
2. **No max-row cap.** The Pin Tab shows every pin; the HUD's `HudMaxRows` / "+N more" bounding does not
   apply.
3. **Governed by Lectern-dialog settings** (`PixelArtDisplay`, `WindowFontScale`, `PixelArtSize`) since it
   lives inside the Lectern dialog — NOT the HUD-prefixed settings (anchor/offsets/`HudRowWidth`/
   `HudFontScale`).
4. **Orphaned pins.** Actioning an orphaned row removes the pin rather than attempting to complete a task
   (matches `player-pins`), keeping the surface's "checking an entry makes it leave the set" behavior
   uniform.

## Risks / Trade-offs

- **[Risk] Writes to an unloaded source are snapshot-only and lost at the source until loaded.** Because
  no chunk force-load exists (`TryResolvePos` degrades gracefully at ScribeModSystem.cs:476), editing or
  deleting a pin whose owning document is unloaded updates only the per-player pin snapshot/store; the
  source document is unchanged until it is next loaded. → **Mitigation / accepted:** this exactly matches
  how the Delete completion policy behaves today, so it introduces no new failure mode; surface a
  "changes apply when the page is loaded" hint in the Pin Tab only if it proves confusing in-game.

- **[Risk] A lock-free text write can race a concurrent whole-document `ApplyEdit`.** `SetTaskTextFromReader`
  is lock-free by design (like the done-flag path), so a player editing the whole document under the edit
  lock could clobber a pin-editor text change (and vice versa). → **Mitigation / accepted:** this is the
  same caveat already documented for the lock-free done-flag path; the window is small and the last write
  wins, consistent with existing behavior. Document it alongside the done-flag caveat.

- **[Risk] Message wire order is frozen.** Inserting a new message mid-list would shift packet ids and
  break compatibility. → **Mitigation:** append `ScribeEditPinnedTaskMessage` / `ScribeReorderPinsMessage`
  (and any standalone delete/unpin) strictly after the existing registrations in `ScribeModSystem.Start`.

- **[Risk] Editing-row State loss on the `MyPinsChanged` `ForceRebuild`.** A server pin resync fires
  `MyPinsChanged`, which rebuilds the view and tears down row State — mid-edit that could drop the
  `ScribeMultilineField`'s focus/caret, the same hazard the editor view already faces. → **Mitigation:**
  key each pin row by `ValueKey<Guid>(TaskId)` (the editor's existing pattern) so a row's element identity
  and its field State survive a rebuild when the row is still present; a resync that removes/reorders rows
  reconciles by key. Reuse the editor's established row-keying rather than inventing a new scheme.

## Migration Plan

Additive only. New messages are appended to the frozen registration order in `ScribeModSystem.Start`; no
existing message's wire format changes. The pin persistence format is unchanged — pin list order is
already a persisted `List` under `scribe:pins:v1`, so reorder needs no format bump and no migration.
Older clients/servers simply lack the new messages; there is no persisted-format break to migrate. Core
gains at most a pure-data `SetTaskText(Guid, string)` helper with no serialization impact. Verification
is in-game only (the Core suite cannot reach `src/Mod` GUI or the VS API).
