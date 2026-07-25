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

The UI side is a slide-out pagelet on the Lectern dialog. LibGUI ships the primitives it needs:
`AnimatedSlide` (translates in paint space and keeps hit-testing correct via inverted `RenderTransform`),
`Positioned` + `Stack` (already used for row overlays at GuiDialogScribeLecternLibGui.cs:1490+), `Clip`
(masks to the window edge), and `ScribeMultilineField` (reused for inline edit). `AnimatedOpacity` is
already proven in `HudScribePins.cs:555`. The HUD's undo delay is client-only
(`HudScribePins.UndoWindowMs`); the pagelet simply omits the sink-timer to get no-undo for free.

This is Phase 4 and the largest/riskiest phase. It composes visually with `scribe-themed-toggle`,
`scribe-gui-backdrops`, and `scribe-animated-tabs` (the pagelet renders under whatever theme, backdrop,
and tab shell those phases establish), but the **sync extension in this change is independent** of them
and can land without them.

## Goals / Non-Goals

**Goals:**

- Add identity-addressed edit-text, standalone delete/unpin, and per-player reorder to pins, mirroring
  the existing `CompleteTaskForPlayer` precedent exactly (resolve → best-effort write-through →
  snapshot/store update → re-push).
- Add a slide-out pin-editor pagelet listing all of a player's pins across documents, with the full
  edit treatment per row and no undo delay, complementing the HUD.
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
`ApplyEdit` lock path) was rejected — it would require the edit lock the HUD/pagelet deliberately avoid.

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

### `AnimatedSlide` for the pagelet (hit-testing stays correct)

The pagelet is a `Positioned` child in a `Stack`, wrapped in `AnimatedSlide` (offset toggled `Zero` ↔
off-screen X, an EaseOut curve) and `Clip`ped to the window edge; a `GestureDetector` handle toggles an
`isPinTrayOpen` field → `SetState`/rebuild drives the slide. Rationale: `AnimatedSlide` translates in
paint space and inverts the render transform, so controls remain hit-testable at their rendered position
throughout the slide — unlike a manual offset that would leave hit regions at the un-slid position.
Its animation State is stabilized with a `Key` so it survives `ForceRebuild` (dialog-owned
`ScrollController`s already survive; view State is torn down on rebuild).

### Reuse `ScribeMultilineField` for inline edit; omit the HUD sink-timer for no-undo

The row template is `HudPinsContent`/`HudPinRow`; inline text edit reuses `ScribeMultilineField`
(auto-recolors from the active theme). The pagelet deliberately **omits** the HUD's client-side
undo/sink-timer (`HudScribePins.UndoWindowMs`), so completion and all actions apply immediately.
Rationale: reuse keeps the editor consistent with existing surfaces; the no-undo requirement is met by
simply not wiring the timer — server completion is already instant.

## Risks / Trade-offs

- **[Risk] Writes to an unloaded source are snapshot-only and lost at the source until loaded.** Because
  no chunk force-load exists (`TryResolvePos` degrades gracefully at ScribeModSystem.cs:476), editing or
  deleting a pin whose owning document is unloaded updates only the per-player pin snapshot/store; the
  source document is unchanged until it is next loaded. → **Mitigation / accepted:** this exactly matches
  how the Delete completion policy behaves today, so it introduces no new failure mode; surface a
  "changes apply when the page is loaded" hint in the pagelet only if it proves confusing in-game.

- **[Risk] A lock-free text write can race a concurrent whole-document `ApplyEdit`.** `SetTaskTextFromReader`
  is lock-free by design (like the done-flag path), so a player editing the whole document under the edit
  lock could clobber a pin-editor text change (and vice versa). → **Mitigation / accepted:** this is the
  same caveat already documented for the lock-free done-flag path; the window is small and the last write
  wins, consistent with existing behavior. Document it alongside the done-flag caveat.

- **[Risk] Message wire order is frozen.** Inserting a new message mid-list would shift packet ids and
  break compatibility. → **Mitigation:** append `ScribeEditPinnedTaskMessage` / `ScribeReorderPinsMessage`
  (and any standalone delete/unpin) strictly after the existing registrations in `ScribeModSystem.Start`.

- **[Risk] Animation State loss on `ForceRebuild`.** The tray's slide State could reset when the dialog
  rebuilds (view State is torn down). → **Mitigation:** key the tray's Stateful node so its element
  identity is stable across rebuilds, as the plan requires.

## Migration Plan

Additive only. New messages are appended to the frozen registration order in `ScribeModSystem.Start`; no
existing message's wire format changes. The pin persistence format is unchanged — pin list order is
already a persisted `List` under `scribe:pins:v1`, so reorder needs no format bump and no migration.
Older clients/servers simply lack the new messages; there is no persisted-format break to migrate. Core
gains at most a pure-data `SetTaskText(Guid, string)` helper with no serialization impact. Verification
is in-game only (the Core suite cannot reach `src/Mod` GUI or the VS API).
