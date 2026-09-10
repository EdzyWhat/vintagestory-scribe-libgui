## Context

See `proposal.md` - Why for motivation. Relevant existing code:

- `ScribeModSystem.History.cs`:
  - `OnEntityDeath` (Death/PvP branch) resolves the victim (`sp`) and, for PvP, the killer, then
    calls `FindAllCarriedNotebookRecords(sp)` — a *live* scan of `sp`'s current inventory slots —
    and writes a Death entry to each notebook found. If a third-party mod's own `OnEntityDeath`
    subscriber runs first and empties those slots (e.g. moving items into a corpse), this live
    scan finds nothing and no entry is written. C# multicast event order between two unrelated
    mods is not something either one controls.
  - `FindCarriedNotebooks(IServerPlayer)` walks `player.InventoryManager.InventoriesOrdered`,
    yielding a `NotebookHost`/`TabletHost` for every slot whose item is an `IScribeDocumentItem`,
    excluding `DeniedInventoryClasses` (creative, ground staging). This is on-person only.
  - `FindAllCarriedNotebookRecords(IServerPlayer)` adds CarryOn-carried-container notebooks on top
    of `FindCarriedNotebooks`, via `CarryOnBridge` (reflection-based, since CarryOn is an optional
    soft dependency).
  - `OnStormTick(float)` (registered at 5000ms in `ScribeModSystem.cs`) is a rising-edge detector:
    two field reads on `SystemTemporalStability.StormData` every tick, and the expensive
    per-online-player `FindAllCarriedNotebookRecords` walk only on the transition into a storm.
- `ScribeModSystem.ServerLifecycle.cs`: `OnSaveGameLoaded`/`OnGameWorldSave` already persist several
  per-world stores (`pinStore`, `assignmentStore`, `questDecisionStore`, `playerLocationStore`,
  `timerStores`) via `sapi.WorldManager.SaveGame.StoreData`/`GetData`, each with its own
  `SerializeStore()`/`LoadFrom()` pair.
- `src/Core/HistoryStore.cs`: `TryAddEntry` always appends to `_entries` (a plain list, oldest
  first); there is no date-ordered insertion — entries are trusted to already arrive in
  chronological order, which the fallback above breaks (a queued entry can be flushed well after
  entries that happened chronologically after it).
- `src/Core/HistoryEntry.cs` currently carries only a pre-formatted `InGameDate` display string,
  supplied by the Mod layer — no separate sortable value. Every Mod-layer call site that builds one
  gets that string from `NotebookHost.FormatDate(sapi)`, which internally reads
  `sapi.World.Calendar.TotalDays` (a monotonically increasing `double` for the life of the save)
  purely to compute day/month/year for display, then discards it. That discarded value is exactly
  the sortable timestamp this change needs — it does not need to be invented or reverse-parsed
  from the display string.
- PlayerCorpse-style mods only sweep named, `InventoryBasePlayer`-classed inventories (hotbar,
  backpack, craftinggrid, mouse, character) on player death — never a CarryOn-carried container's
  inventory. Confirmed by decompiling a concrete example (`PlayerCorpse.Systems.DeathContentManager
  .TakeContentFromPlayer`). This bounds where the race can actually happen.

## Goals / Non-Goals

**Goals:**
- Never silently lose a Death entry for a Notebook that was genuinely on the dying player's person,
  even when another mod's death handling empties that inventory slot first.
- Keep the existing live-scan path as the primary, immediate write for the common case (no other
  mod interferes) — this fallback only engages when that scan comes up short.
- Reuse existing periodic-tick and save-game-persistence infrastructure rather than adding new
  listeners or a new persistence mechanism.
- Keep entries in their true chronological order — by actual in-game moment, not by when they
  happened to be written — so a queued/delayed Death entry lands where it really occurred, and two
  same-day events keep their real relative order too.

**Non-Goals:**
- Zero staleness. The fallback's accuracy is bounded by the snapshot's refresh interval (10s); a
  Notebook picked up or given away in the few seconds before death can be mis-attributed either
  way. This was explicitly accepted as a reasonable trade for avoiding a Harmony patch on the
  entity damage path (the only way to close that window to zero, and disproportionate to this fix).
- Any change to the killer's side of a PvP kill. Nothing evicts a killer's inventory during their
  own kill (they are not dying), so there is no race to guard there.
- Any change to BossKill or TemporalStorm's own write behavior beyond the tick they share.
- Pruning/expiry of queued entries. Explicitly decided against (see Decisions).

## Decisions

### Fallback keyed by document id, not by player
A queued entry targets the specific document (`ScribeDocument.DocId`) that was actually present at
death, not "whatever Notebook this player is next seen carrying." An earlier alternative — keying
by `PlayerUID` and flushing into any notebook the player next carries — was rejected: it would
write a death event into a notebook crafted or received *after* the death, which never held it and
has no business recording it. Keying by document id preserves the existing rule that a notebook's
history reflects what happened while it was actually on someone, not its current owner's biography.

### A periodic snapshot, not a live re-check, feeds the fallback
The fallback cannot depend on re-reading the player's live inventory at death time — that is
exactly the read that is racing. Instead, a per-player, in-memory "last known carried document ids"
snapshot is refreshed on its own schedule (the shared tick below), independent of when any given
`OnEntityDeath` dispatch happens to run. Because the snapshot is written on a separate cadence, its
value going into a death is fixed *before* that death's own event dispatch begins, so it cannot be
invalidated by a sibling `OnEntityDeath` subscriber that runs earlier in the same dispatch.

At death: the live scan runs first (unchanged, primary path). Any document id present in the
player's last snapshot but absent from that live scan is queued — this is exactly "something
evicted it since the last refresh." A document id found in both is not queued (it was already
written by the live scan; queuing it too would double-write).

### The 10s storm tick is repurposed into the shared scan and flush trigger
`OnStormTick` already walks every online player once per firing (today, only usefully on a storm's
rising edge). Its interval widens from 5s to 10s (explicitly accepted — this is a fallback path,
not the primary write path, and it halves an already-cheap detection cadence, not a correctness
guarantee) and its per-player body gains two responsibilities alongside the existing storm check:
refresh that player's snapshot, and — for every document id currently seen — check the pending
store for queued entries and flush them right there. This makes the periodic scan double as the
"was this document just picked back up" signal; no separate event hook is needed for that.

The scan is scoped to `FindCarriedNotebooks` (on-person only), not the fuller
`FindAllCarriedNotebookRecords`. The corpse-mod race can only evict on-person slots (see Context) —
CarryOn-carried containers were never at risk — so there is no reason to add an unconditional,
reflection-based CarryOn walk for every online player every 10s. A Notebook only ever reachable via
CarryOn keeps relying on the live scan exactly as it does today.

`OnTaskNoticeProximityTick` and `OnTimerTick` were both reviewed and are not folded into this tick:
the former scans a different item type under its own gate (outstanding notice count), and the
latter needs true 1Hz resolution for countdown accuracy and is already cheap (a dictionary walk,
no inventory scanning) — neither shares a real cost or purpose with this change.

### Chronological ordering via a captured timestamp, not display-string reparsing
`HistoryEntry` gains a new sortable field (an in-game timestamp) alongside the existing
display-only `InGameDate` string. Every write site already computes `sapi.World.Calendar
.TotalDays` internally (inside `NotebookHost.FormatDate`) to build the display string and then
discards it; that same value becomes the new field, so no new game-state read is introduced — the
existing computation is just no longer thrown away. `HistoryStore.TryAddEntry` inserts the new
entry at its correct sorted position (by that timestamp, ties broken by write order) instead of
blind-appending, so the per-kind "drop oldest" cap logic continues to mean exactly what it already
means (smallest timestamp of that kind) as long as the store stays sorted.

This is the only design considered that fixes the ordering problem rather than working around it:
the alternative (accept append-order and document the late-flush-displays-late quirk) was the
original plan, but is exactly what motivated this revision — reparsing the existing localized
`InGameDate` display string back into a sortable value was also considered and rejected, since it
would be a fragile round-trip through player-facing, translatable prose for something the raw
calendar value already gives for free.

**Codec impact**: this is a new per-entry field, so `HistoryStore` bumps from `SHST v2` to `v3`,
following the append-only, named-migration-step pattern in `docs/CODEC-MIGRATION.md`
(`ApplyV2ToV3Migrations`). Existing v2 (and, transitively, v1) entries never recorded a timestamp,
so they cannot be genuinely re-derived from their display string alone (that string has no
time-of-day component — exactly the information needed to disambiguate same-day entries).
Migration instead assigns each existing entry a synthetic timestamp from a strictly-increasing
negative sequence in its existing list order (e.g. `-N, -N+1, ..., -1` for `N` legacy entries).
Since `Calendar.TotalDays` can never be negative, this guarantees every migrated entry sorts before
every entry recorded after this change ships — which is simply true, since they really were
recorded before it shipped — while exactly preserving their existing relative order via the
sequence index. No special-cased "legacy" comparison logic is needed anywhere else; every
comparison is a plain numeric sort.

### Pending store: no pruning
An unflushed entry for a document that is genuinely lost forever (destroyed, abandoned in an
unreachable corpse) stays in save data indefinitely. Rejected alternatives: a count cap (mirroring
`HistoryStore`'s per-kind sliding windows) or an age-based expiry. Both add real bookkeeping for a
path that is expected to be rare (it only engages when another mod actually wins the race), and an
unbounded but rare set of small entries is judged an acceptable cost against that complexity. This
can be revisited if real-world save-file growth ever shows otherwise.

### Persistence follows the existing per-world store pattern exactly
The pending store gets its own `SerializeStore()`/`LoadFrom()` pair and a new
`SaveGame.StoreData`/`GetData` key, wired into `OnSaveGameLoaded`/`OnGameWorldSave` alongside
`assignmentStore`/`questDecisionStore`/`playerLocationStore`/`timerStores`. No new persistence
mechanism is introduced.

## Risks / Trade-offs

- **[Risk] A Notebook acquired or given away within the last snapshot interval can be
  mis-attributed** (recorded as carried when it was not, or vice versa) → **Mitigation**: accepted
  per explicit user decision; the window is bounded by the tick interval (10s) and only matters in
  the already-narrow case where a competing mod is also racing the same death.
- **[Risk] Unbounded, never-pruned pending entries could accumulate on a server with many
  permanently-lost notebooks** → **Mitigation**: accepted as low-likelihood given the rarity of the
  triggering race; revisit if observed in practice.
- **[Risk] A codec version bump (v2 → v3) always carries some risk of mis-migrating existing player
  saves** → **Mitigation**: follows the project's already-proven append-only migration pattern
  exactly (same shape as the v1 → v2 bump this same store already went through); the synthetic
  negative-sequence timestamp for legacy entries is a pure function of existing list position, so
  it cannot disturb any entry's existing relative order, only add a previously-absent field.
- **[Risk] The new per-entry 8-byte timestamp field adds to `HistoryStore.MaxHistoryBytes`
  pressure** → **Mitigation**: 8 bytes per entry against a 64KB budget and the existing per-kind
  caps (≤30ish entries per kind) is a small, bounded increase; no change to the budget itself is
  needed.
- **[Risk] Widening `OnStormTick` to 10s delays storm-entry recording by up to 10s instead of 5s**
  → **Mitigation**: accepted; storm-onset detection latency was never a correctness requirement,
  and this was explicitly the user's own suggestion for the shared tick.

## Migration Plan

Two independent pieces:

- **The new pending store** is purely additive: it starts empty (`LoadFrom` on a missing save key
  behaves like every other store's first-load case, per the established pattern in
  `OnSaveGameLoaded`). No data migration involved.
- **The `HistoryStore` codec bump (v2 → v3)** does touch every existing Notebook's saved data, the
  first time it is deserialized after this change ships: each existing entry gains a synthetic
  timestamp per the Decisions section above. This follows the same tested, append-only migration
  shape the store already used for its v1 → v2 bump, so it carries no new *kind* of risk, only the
  ordinary care any codec version bump requires (covered by the Core.Tests migration coverage in
  `tasks.md`).

Rollback: reverting the code change is sufficient. A `v3` payload is never written until this
change ships, and nothing existing is altered in place beyond gaining the new field on next load —
there is no separate data-migration step to undo.
