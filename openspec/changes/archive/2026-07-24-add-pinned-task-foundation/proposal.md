## Why

Pinning a task today only tints its row inside the one lectern that owns it — a shared boolean
on the task with no reach beyond that block. The goal is for a player to pin tasks from any
lectern (and later notebooks/desk blocks) and see *their own* pinned tasks aggregated on a HUD
and in a dedicated Pinned tab. The stable identity those references need (`DocId`/`TaskId`) is
delivered by the prerequisite `add-document-task-identity` change; this change builds the
per-player layer on top of it: where pins live, how they sync, and how a player acts on a pin
whose source block may be broken, far away, or in an unloaded chunk.

Two design commitments shape this change:

- **Pins are addressed by identity, never by position.** The synced client-side pin record
  carries only `(DocId, TaskId)` — no block position — so a HUD or Pinned tab can pin, unpin,
  and complete a task without ever resolving its block. This is why the pin/complete network
  messages carry `(DocId, TaskId)` rather than a block position, and it is what lets a player
  clear a pin whose lectern is gone. (Consistent with ROADMAP Open Decision #4's move to a
  document handle on the wire.)
- **Checking a pinned task off makes it go away.** Completing a pinned task removes it from that
  player's pinned set (auto-unpin), so the primary way to clear the HUD is simply finishing the
  task. A per-player setting lets a player opt out (keep pins after completion); an orphaned
  pin, having no live task to complete, is simply removed when checked. The setting record also
  reserves room for a HUD collapse/minimize preference. The HUD, the Pinned tab, and the UI that
  toggles these settings are later changes; this change persists and syncs the settings and
  provides the server primitives they call.

## What Changes

- Introduce a **per-player pin store**: each player has their own set of pin references
  (`(DocId, TaskId)` + snapshot + pinned-time + orphaned flag), held server-side, persisted with
  the save game, and pushed to that player's client. A player only ever sees their own pins.
- **Address pins by identity on the wire.** The pin toggle message carries `(DocId, TaskId)`;
  the server resolves `DocId → BlockPos` via a live index **only to read a snapshot**, so
  unpinning needs no block resolution and works when the lectern is broken or unloaded.
- **Generalize task completion to identity.** The existing lock-free read-view task-toggle
  becomes addressed by `(DocId, TaskId)` (today it is block-position + list index), so the same
  server op a future HUD checkbox calls also drives the lectern's read-view checkbox now.
- **Complete-to-unpin + per-player settings.** Completing one's own pinned task removes that
  player's pin unless they opted out; add a small per-player settings record (default
  complete-to-unpin on, room for a HUD-collapsed flag) persisted and synced alongside the pins.
- Pinning/unpinning/completing a task is a **lock-free action** that never touches the
  document's edit lock or autosave (completion mutates shared document state via the existing
  lock-free path; pinning never touches the document at all).
- Make a lectern's document **survive break → re-place**: breaking the block carries the
  serialized document (with its ids) onto the dropped item and restores it when placed, so pins
  keep resolving. Content is only lost when the dropped item fully disappears (despawn).
- **Orphaning (soft):** when a pinned task is genuinely deleted (its block broken/removed, or
  the task removed from a saved edit), the pin is flagged orphaned and keeps a last-known
  text/done snapshot rather than vanishing. An unloaded chunk is never treated as a deletion.
- **Migrate v3 pins:** drain the previously-pinned task ids surfaced by the codec (from
  `add-document-task-identity`) into the current single player's pin store, one-time, on first
  load; MarkDirty a v3-detected lectern so it re-saves as v4.

## Capabilities

### New Capabilities
- `player-pins`: a per-player set of references to specific tasks `(DocId, TaskId)`, held
  server-side, persisted, and synced to the owning player only. Covers pin/unpin (by identity),
  identity-addressed task completion, complete-to-unpin with a per-player opt-out setting, the
  persistence format, the sync path, and the soft-orphan lifecycle when a referenced task is
  deleted.

### Modified Capabilities
- `task-note-document`: remove the shared per-task pin toggle and pin field (the identity/codec
  work it depends on lives in `add-document-task-identity`).
- `lectern-block`: the "pin a task from the GUI" behavior changes from mutating the shared
  document to recording a per-player pin (lock-free); the read-view checkbox completes a task by
  identity and honors complete-to-unpin; and the lectern gains break→re-place data retention so
  its document (and task ids) survive being picked up and set back down.

## Impact

- **Depends on `add-document-task-identity`** (stable `DocId`/`TaskId`, codec v4, `FindByTaskId`,
  delete-reports-id). The two changes are merged together; this one re-points `Pinned`'s former
  consumer and is what builds `src/Mod/`.
- **Core (`src/Core/`, stays API-free):** new `ScribePinnedRef` type, per-player `ScribePlayerSettings`
  type, and `ScribePinCodec` (list blob for the network message, store blob for the savegame,
  settings blob). All pure bytes/BCL, unit-testable.
- **Mod (`src/Mod/`):** new `ScribePinStore` (owns the per-player pins + settings + a live
  `DocId→BlockPos` index) + savegame persistence; new identity-addressed pin and complete
  messages on the existing `"scribe"` channel; `ScribeModSystem` registration + push triggers +
  client cache; `BlockScribeLectern` pick/drop overrides and
  `BlockEntityScribeLectern.OnBlockPlaced(ItemStack)`; generalize the read-view toggle to
  identity; re-wire the lectern pin button/tint and read-view checkbox to the store. Local Atlas
  integration scenarios (`tests/Integration.Tests`).
- **No new dependencies**; vanilla `VintagestoryAPI` only. CI still builds/tests Core only; the
  server-side pieces are gated by the local Atlas integration suite.
- **Depends on** the `add-lectern-row-affordances-libgui` branch (builds on its current GUI/codec
  code, not yet merged to `main`).

## Out of scope (later changes)

- The HUD element and its rendering/refresh; the Pinned tab; cross-block aggregation UI.
- The UI that toggles the per-player settings (the complete-to-unpin opt-out control, the HUD
  collapse/minimize button) — this change only persists and syncs the settings and provides the
  server primitives; no UI is built for them here.
- Read-view pinning, notebooks/desk blocks, copy/paste-between-blocks (roadmap; the break/replace
  helper is built with that reuse in mind but the feature is out of scope).
- The shared `"scribe:doc:<docId>"` document store (stays v2/notebook's introduction, ROADMAP
  Open Decision #4); the lectern's document stays in its block entity here.
