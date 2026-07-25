## Context

Pinning is currently a shared boolean (`ScribeBlock.Pinned`) inside the server-authoritative
per-lectern document, toggled only in the editor view and re-synced through the whole-document
autosave path. The stable identity a durable per-player pin needs (`DocId`/`TaskId`, codec v4)
is delivered by the prerequisite `add-document-task-identity` change. This change builds the
per-player layer: the store, its sync, identity-addressed pin/complete actions, lectern
break/replace retention, and the soft-orphan lifecycle.

Two commitments shape the design and are the reason this is a firm foundation for later surfaces
(HUD, Pinned tab) sourced from multiple block/item types:

1. **Identity-addressed, not position-addressed.** The synced client pin record carries only
   `(DocId, TaskId)`; it has no block position. So every action a pin-listing surface performs —
   pin, unpin, complete — is addressed by `(DocId, TaskId)`. The server resolves `DocId →
   BlockPos` through a live index *only* to read a text/done snapshot; unpin needs no
   resolution. This is what makes a pin actionable when its lectern is broken, far away, or
   unloaded, and it is forward-compatible with the shared `docId` document store (that store
   would replace the live index without changing the reference model). Aligns with ROADMAP Open
   Decision #4's move to a document handle on the wire.
2. **Check-to-remove.** Completing a pinned task removes it from the completing player's set
   (auto-unpin) unless they opted out; an orphaned pin (no live task) is simply removed when
   actioned. This gives the future HUD one uniform gesture — check it and it leaves your list —
   whether or not the source still exists.

Constraints: `src/Core/` must stay free of the Vintage Story API (BCL like `System.Guid` is
fine); no new mod/NuGet dependencies; persistence/sync follows the vanilla Sign pattern already
in use; CI builds/tests Core only (server-side behavior is gated by the local Atlas suite).

## Goals / Non-Goals

**Goals:**
- A per-player pin store: server-authoritative, save-persisted, pushed to the owning player,
  never visible to other players.
- Identity-addressed pin/unpin — unpin resolvable by `(DocId, TaskId)` alone, so it works when
  the source block is gone or unloaded.
- Identity-addressed task completion (generalizing the existing lock-free read-view toggle), and
  complete-to-unpin gated by a per-player setting with a safe opt-out; orphaned pins removed when
  actioned.
- A small per-player settings record (default complete-to-unpin on, room for a HUD-collapsed
  flag), persisted and synced.
- Lock-free pin/unpin that never touches document bytes, the edit lock, or autosave.
- A lectern's document (with its ids) survives break → re-place; only despawn of the dropped
  item truly loses it.
- Soft-orphan a pin when its task is genuinely deleted, keeping a last-known snapshot; never
  confuse an unloaded chunk with a deletion.
- Prove it all without the HUD: Core unit tests + local Atlas integration + a minimal in-game
  exercise (the lectern pin button + read-view checkbox re-wired to the store).

**Non-Goals (later changes):**
- The HUD element and its rendering/refresh.
- The lectern "Pinned" tab, cross-block aggregation UI, tombstone/cascade orphan UI.
- The UI that toggles the per-player settings — the complete-to-unpin opt-out control and the
  HUD collapse/minimize button. This change persists/syncs the settings and provides the server
  primitives; no settings UI is built here.
- Notebooks/desk blocks, copy/paste-between-blocks (roadmap; the break/replace helper is built
  with that reuse in mind but the feature is out of scope).
- The shared `"scribe:doc:<docId>"` document store (stays v2/notebook's job); the lectern's
  document stays in its block entity here.
- Multiplayer migration of pre-existing v3 pins (see Migration Plan).

## Decisions

### D1 — Stable identity comes from `add-document-task-identity`
The `Guid DocId`/`Guid TaskId` on `ScribeDocument`/`ScribeBlock`, codec v4 (write v4, read
v3+v4), the migration seam surfacing v3 legacy-pinned ids, `FindByTaskId`, and delete-reports-id
are all delivered by the prerequisite change and are not re-specified here. This change consumes
them: pins reference `(DocId, TaskId)`; the snapshot/orphan paths call `FindByTaskId` and the
delete-reports-id seam; the v3-migration path consumes the surfaced legacy-pinned ids (D8).

### D3 — Per-player pin store as a savegame blob (the `WaypointMapLayer` pattern)
A ModSystem-owned `Dictionary<playerUid, List<ScribePinnedRef>>`, persisted as one blob via
`sapi.Event.GameWorldSave` → `SaveGame.StoreData("scribe:pins:v1", …)` and loaded on
`SaveGameLoaded` → `SaveGame.GetData`. `ScribePinnedRef` = `{ OwnerDocId, TaskId,
PinnedAtTotalHours, Orphaned, LastKnownText, LastKnownDone }`. The type and its codec
(`ScribePinCodec`, `SPIN` list blob for the network message + `SPST` store blob for the save)
live in Core (pure bytes/BCL, unit-testable); only the store that owns `sapi` is Mod-side. Cap
`MaxPinsPerPlayer = 500`.
- *Why the savegame blob over `IServerPlayer.SetModData`:* both persist per-player and neither
  auto-syncs, but the global blob lets the server enumerate *all* players' pins for orphan
  handling without loading each player. Decisive for the deletion-sweep path.
- *Why not `Entity.WatchedAttributes`:* it auto-syncs but broadcasts to all nearby clients (not
  just the owner) and is entity-load-gated — wrong for private, always-available data.

### D3b — Per-player settings alongside the pins
A companion per-player `ScribePlayerSettings` record — initially `{ bool CompleteUnpins = true }`
with reserved room for a `bool HudCollapsed` — stored in the same store/save family and synced
to its owner. It is a Core type with its own `ScribePinCodec` settings blob (`SPSE`), versioned
and fail-safe like the pin blobs. Defaults are applied when a player has no stored settings.
- *Why store it server-side with the pins rather than client-only:* the complete-to-unpin
  decision is enforced **on the server** (the server owns both the document mutation and the pin
  removal), so the server must know the setting; keeping it beside the pins reuses one
  persistence + push path. The later settings UI just sends a "set my settings" message.
- *Why reserve `HudCollapsed` now:* the HUD's collapse/minimize (a later change) is per-player
  display state with the same persist+sync needs; reserving the field avoids a second format
  bump. The foundation persists/syncs it but nothing reads it yet.

### D4 — Explicit per-player push over the existing `"scribe"` channel
Nothing in D3/D3b auto-syncs, so add ProtoContract messages **appended** to the existing
`RegisterMessageType` chain in the same order on both sides (never reorder the existing four):
- `ScribeSetPinMessage` (client→server): 16-byte `DocId` + 16-byte `TaskId` + `bool Pinned`.
- `ScribeCompleteTaskMessage` (client→server): 16-byte `DocId` + 16-byte `TaskId` — the
  identity-addressed completion (D5b). (The existing positional `ScribeToggleTaskMessage` is
  retired in favor of this; see D5b.)
- `ScribePinnedSetMessage` (server→client): one player's `SPIN` blob.
- `ScribePlayerSettingsMessage` (server→client): one player's `SPSE` settings blob.
The server pushes a player their own filtered set + settings on `sapi.Event.PlayerNowPlaying`
and re-pushes that player after any mutation/snapshot change (the waypoints `ResendWaypoints`
discipline). The client caches the set + settings in ModSystem fields and exposes
`IsPinnedForMe(DocId, TaskId)` and the settings.
- *Why `byte[]` not `Guid` in messages:* protobuf-net's `Guid` handling is version-fragile; raw
  16-byte arrays are unambiguous.
- *Why the server derives the snapshot:* the client sends only `(DocId, TaskId)` + desired
  state; the server reads text/done from its own authoritative document — never trust a client
  snapshot.

### D5 — Identity-addressed resolution; lock-free
`OnServerReceivedSetPin` takes `(DocId, TaskId, Pinned)`. For a **pin add**, it resolves the
block entity via the store's live `Dictionary<Guid DocId, BlockPos>` index (no lock check,
mirroring the existing lock-free `ToggleTaskFromReader`), `FindByTaskId` on its document to
capture a text/done snapshot, then `SetPin`. For a **pin remove**, it goes straight to
`store.RemovePin(uid, DocId, TaskId)` — **no block resolution at all**, so an unpin succeeds when
the lectern is broken (its `DocId` is no longer in the index) or its chunk is unloaded. Either
way it re-pushes that player and touches no document bytes, edit lock, or autosave dirty flag.
- *Why a live `DocId→BlockPos` index instead of scanning:* a pin add needs the current position
  of the owning block to snapshot it; the index is maintained by the block entity lifecycle
  (D6/tasks) and is the runtime resolver. It is intentionally *not* the durable reference — the
  durable reference is `(DocId, TaskId)` in the store — so an index miss just means "unresolvable
  right now," never "deleted."

### D5b — Identity-addressed completion + complete-to-unpin coupling
Generalize the existing lock-free read-view completion so it is addressed by `(DocId, TaskId)`
rather than block-position + list index: retire `ScribeToggleTaskMessage` (PosX/Y/Z + BlockIndex)
in favor of `ScribeCompleteTaskMessage` (DocId + TaskId). `OnServerReceivedCompleteTask` resolves
the document via the live index (lock-free), `FindByTaskId`, toggles `Done` on the authoritative
document via the existing `ToggleTaskFromReader` path (`MarkDirty` + `RefreshSnapshots`), and
**then** — if the completing player pinned this task and their `CompleteUnpins` setting is on —
calls `store.RemovePin(uid, DocId, TaskId)` and re-pushes that player.
- *Completion is shared, unpin is per-player.* Toggling `Done` changes the document for everyone;
  the auto-unpin removes only the completing player's pin. Another player who pinned the same
  task keeps their pin, whose snapshot `RefreshSnapshots` updates to `Done`.
- *Orphaned pins have no live task.* Because an orphaned pin's `DocId` isn't resolvable, a
  "complete" gesture on it can't toggle a document; the server (and the later HUD) treats
  actioning an orphaned pin as `RemovePin` only. This keeps the surface gesture uniform without a
  document mutation.
- *Why retire rather than keep both toggle messages:* two parallel completion paths (positional +
  identity) would double the server handlers and the wire surface for one behavior; the read view
  is the only current caller and it is being re-wired anyway (D8). This is DRYer and it is the
  path the HUD will reuse. (The edit-view whole-document autosave path, `ScribeEditDocumentMessage`,
  is unaffected — it already carries the full document.)

### D6 — Break vs. disappear via `stack.Attributes`
A shared `ScribeDocumentAttributes` helper (`WriteTo`/`TryReadFrom` over
`stack.Attributes["scribeDocument"]`, reusing the existing codec + key). `BlockScribeLectern`
overrides `OnPickBlock` and `GetDrops` to write the serialized document onto the drop (the block
entity is still alive during drops — they run before `RemoveBlockEntity`);
`BlockEntityScribeLectern.OnBlockPlaced(ItemStack)` restores it (empty doc fallback). Because the
`DocId` rides inside the bytes, the same document identity survives break→replace and pins
reattach automatically. Precedent: `EntityBlockFalling` / `BEBehaviorClutterBookshelfWithLore`.
The dropped item despawning is the only true-loss point. This same helper is the roadmap
copy/paste-between-blocks mechanism.

### D7 — Soft-orphan, on permanent-deletion signals only
Default lifecycle: when a pinned task is genuinely gone, flag the pin `Orphaned = true` and keep
`LastKnownText`/`LastKnownDone`; it stays on the player's UI until they remove it (a check-off
`RemovePin`s it — D5b). Triggers, and *only* these: `OnBlockRemoved`/`OnBlockBroken`
(→ `OrphanAll(DocId)`) and a task disappearing from a saved edit (detected by diffing surviving
`TaskId`s in `RefreshSnapshots`, called after `ApplyEdit`/completion). **Never** orphan on
`OnBlockUnloaded` or a resolution miss — the store is decoupled from chunk load, so an unloaded
target is simply "unresolvable," not deleted.
- *Why soft over cascade:* a mis-flag is recoverable; auto-deleting a pin destroys user data on a
  signal that is easy to get subtly wrong. Vanilla waypoints also do manual/soft cleanup.
  Tombstone-tab (a UI filter over `Orphaned`) and opt-in cascade are later, over the same state.

### D8 — Change surface: full data layer + re-wire the lectern pin button and read-view checkbox
Ship the data layer AND re-point the lectern's existing pin button/tint and its read-view
checkbox to the per-player store (lock-free, identity-addressed), as the visible in-game proof.
Rationale: dropping `Pinned` cleanly *requires* re-pointing its one consumer, generalizing the
read-view toggle to identity *requires* re-pointing the checkbox, and a store nothing writes to
is untestable in-game — so this is the minimum, not scope-creep. The row records carry `TaskId`
instead of `Pinned`; the pin button sends `ScribeSetPinMessage`; the read-view checkbox sends
`ScribeCompleteTaskMessage`; the resting tint and pin-glyph color query `IsPinnedForMe`; the
dialog repaints on a pinned-set/settings push. The v3 legacy-pin drain (D8-migration) consumes
the ids the codec surfaces (D1).

## Risks / Trade-offs

- **v3 lectern re-save on load** → If a v3 document loads and is never marked dirty, its
  generated `DocId`/`TaskId`s regenerate every load and pins can't stick. Mitigation: MarkDirty a
  v3-detected lectern on first load so it persists as v4. This is the single most important
  sequencing detail.
- **Retiring `ScribeToggleTaskMessage`** → any code/tests referencing the positional toggle break.
  Mitigation: the read view is its only caller (re-wired in D8) and the integration `PinScenarios`
  are updated in the same change; call it out in tasks.
- **Complete-to-unpin surprising a user** → auto-removing a pin on completion could feel like data
  loss to someone who wanted to keep it. Mitigation: it only removes the *pin* (the task and its
  done-state remain in the document), it is per-player, and the opt-out setting (default on, but
  overridable) is persisted from day one so the later UI can expose it.
- **One-way version bump** (inherited from `add-document-task-identity`) → v4 saves can't be read
  by older builds. Mitigation: acceptable for a single-user/self-hosted mod; documented.
- **Coarse v3 pin migration** → v3 `pinned` was shared, not per-player; migrating those flags to
  the local single player is a lossy approximation on a multiplayer world. Mitigation: scope the
  migration to single-player only and document it; multiplayer v3 pin migration is a non-goal.
- **Channel message-order coupling** → reordering the `RegisterMessageType` chain silently
  corrupts all `"scribe"` traffic. Mitigation: append the new messages to the end, identically on
  both sides; and when retiring `ScribeToggleTaskMessage`, remove it in the *same* position on
  both sides. Call it out in tasks.
- **Orphan-on-unload bug class** → treating an unresolvable target as deleted would destroy pins
  when chunks unload. Mitigation: orphan only on the explicit permanent-deletion signals (D7);
  never on unload/resolution failure. Covered by an integration scenario.
- **Not breaking the current edit/sync flow** → pinning must stay off the document/lock/autosave
  path; completion uses the existing lock-free toggle path only. Mitigation: `RefreshSnapshots`
  piggybacks *after* the existing `MarkDirty` calls; the pin toggle is a separate lock-free
  handler.

## Migration Plan

- **Forward:** on loading a v3 world, each lectern deserializes via the v3 path (new ids
  generated, surfaced through the codec seam), is marked dirty so it re-saves as v4, and its
  previously-pinned tasks are drained into the current player's pin store on first
  `PlayerNowPlaying` (single-player scope). The `scribe:pins:v1` savegame key starts empty and is
  written on first `GameWorldSave`. Per-player settings default (complete-to-unpin on) when absent.
- **Rollback:** none within a world once saved as v4 (one-way bump). To roll back the mod build,
  restore a pre-v4 world backup. No in-place downgrade.

## Open Questions

- None blocking. The HUD's exact anchoring/refresh, the Pinned-tab tab-state model, and the
  settings UI (the complete-to-unpin opt-out control and the HUD collapse button) are deferred to
  later changes and don't affect this foundation — which reserves the settings fields and the
  server primitives they need.
