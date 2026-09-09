## Why

A third-party mod that also handles player death (e.g. one that creates a corpse and empties the
player's inventory) can run its own `OnEntityDeath` handler before Scribe's, silently removing a
carried Notebook from the player's inventory before Scribe's own live scan ever gets to check for
it. Because C# multicast event subscriber order is not controlled by either mod, this turns
Death-entry recording into a race Scribe can lose — one the user has directly observed losing in
practice, silently dropping the entry with no error or warning at all.

## What Changes

- Widen the existing storm-detection tick from 5s to 10s and repurpose it into a shared per-player
  scan: alongside its existing storm rising-edge check, it now also refreshes an in-memory
  snapshot of which Notebook/Tablet documents (by document id) each online player carries on their
  person, and flushes any queued pending history entries into a document the moment it is next
  seen in a live carried slot.
- When a player dies, Scribe still tries its live carried-notebook scan first (unchanged — this
  remains the immediate, common-case path). For any document that was in that player's last
  snapshot but is no longer found in the live scan (evicted by something else during the same
  death dispatch), the Death entry is now queued against that document's own id instead of being
  silently dropped.
- A queued entry is written into its target document the next time that specific document is
  observed in any live carried slot — regardless of who is currently holding it (the original
  owner reclaiming it from a corpse, or someone else who found it) — matching the existing rule
  that history belongs to the notebook that was actually present, never a different or
  newly-created one.
- Queued entries persist across a server restart (a new save-game-backed store, following the same
  pattern as the mod's other per-player stores), since the gap between a death and the notebook
  resurfacing can outlast a single session.
- This fallback applies only to the Death entry of the player who died. The killer's side of a PvP
  kill is unaffected and keeps today's live-scan-only behavior — nothing evicts the killer's
  inventory, since they are not the one dying.
- No pruning or expiry: a queued entry that is genuinely never seen again (its notebook destroyed
  or permanently lost) persists in save data indefinitely rather than being time- or count-limited,
  since this path is expected to be rare.
- `HistoryStore` moves from pure append-order to insertion sorted by an in-game timestamp captured
  at write time — not just the currently-displayed date string, which stays exactly as it looks
  today. Multiple events on the same displayed in-game day (e.g. two deaths in one day) now keep
  their real relative order, and a Death entry queued by the fallback above lands in its correct
  chronological position when flushed later, instead of always appending at the end. This bumps
  the `HistoryStore` codec to a new version (`SHST v3`); existing entries (which never recorded
  this timestamp) migrate to a synthetic ordering that preserves their existing relative order and
  sorts before anything recorded after this change ships.

## Capabilities

### Modified Capabilities
- `notebook-history`: the Death-recording requirement gains a fallback path for when the live
  carried-notebook scan cannot find a notebook that really was present moments before death.

## Impact

- `src/Mod/ScribeModSystem.History.cs`: `OnStormTick` (extended into a shared periodic scan),
  `OnEntityDeath`'s Death branch (fallback queuing), a new document-id-keyed pending-entry store,
  and a new per-player last-known-carried-documents snapshot.
- `src/Mod/ScribeModSystem.ServerLifecycle.cs`: `OnSaveGameLoaded`/`OnGameWorldSave` gain a new
  store to persist/load, following the existing `assignmentStore`/`questDecisionStore`/
  `timerStores` pattern.
- `src/Core/HistoryEntry.cs`/`HistoryStore.cs`: a new sortable timestamp field per entry, a codec
  version bump (`SHST v2` → `v3`) with migration, and sorted (rather than append-only) insertion.
- Every existing Mod-layer write site that constructs a `HistoryEntry` (Crafted, PickedUp, Death,
  PvpKill, BossKill, TemporalStorm, Manual) starts also supplying the new timestamp alongside the
  existing formatted date string.
- No new mod dependencies.
