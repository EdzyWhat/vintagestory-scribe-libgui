## 1. Chronological ordering in HistoryStore

- [x] 1.1 Add a sortable in-game timestamp field to `HistoryEntry` (`src/Core/HistoryEntry.cs`,
      e.g. `double InGameTimestamp`), distinct from the existing display-only `InGameDate` string.
      Verify by inspection the field carries no VS API type and keeps `src/Core/` API-free.
- [x] 1.2 Bump `HistoryStore`'s codec to `SHST v3` (`src/Core/HistoryStore.cs`): write/read the new
      field per entry, update `PriorVersion`/the accepted-version doc table, and add a named
      `ApplyV2ToV3Migrations` step (per `docs/CODEC-MIGRATION.md`'s pattern) that assigns each
      migrated entry a synthetic timestamp from a strictly-increasing negative sequence in its
      existing list order. Verify with a Core.Tests unit test that a v2 payload deserializes with
      every entry's original relative order preserved and every migrated timestamp negative
      (i.e. sorting before any timestamp a freshly-created entry can carry).
- [x] 1.3 Change `HistoryStore.TryAddEntry` to insert the new entry at its correct sorted position
      by `InGameTimestamp` (ties broken by write order) instead of appending to the end. Verify with
      a Core.Tests unit test: add entries out of chronological order (a later call with an earlier
      timestamp) and confirm `Entries` reflects them in timestamp order, not call order.
- [x] 1.4 Verify the per-kind cap logic (`DropOldestOfKindIfAtCap`) still drops the chronologically
      oldest entry of a kind now that insertion is sorted, not appended — add or extend a
      Core.Tests unit test covering a cap-drop where the newly-added entry's timestamp is not the
      largest of its kind.
- [x] 1.5 Update every existing Mod-layer call site that constructs a `HistoryEntry` (Crafted,
      PickedUp — both `NotebookHost.RecordPickedUpIfNew` and `NotebookHost
      .TryRecordPickedUpOnSlot` — BossKill, TemporalStorm, the Manual-entry creation path in
      `GuiDialogScribeNotebook.cs`, and the Death/PvpKill paths in `ScribeModSystem.History.cs`) to
      also supply `InGameTimestamp` from `sapi.World.Calendar.TotalDays` (or the client-side
      equivalent for the Manual draft preview), alongside the existing `InGameDate` string. Verify
      by inspection every construction site sets both fields; verify with an Atlas integration test
      that two Death entries recorded on the same displayed in-game day, at different real moments,
      appear in their true chronological order rather than call order.

## 2. Pending-entry store

- [x] 2.1 Add a new document-id-keyed pending-entry store type (`Dictionary<Guid, List<HistoryEntry>>`
      or equivalent) with `SerializeStore()`/`LoadFrom()` following the exact binary-writer/reader
      shape already used by `TimerStore`/`ScribeAssignmentStore`, storing each queued
      `HistoryEntry` (including its new `InGameTimestamp`) in full. Verify with a unit test that
      round-trips serialize → deserialize for zero, one, and multiple queued entries spread across
      multiple document ids, and that a missing/empty save key loads as an empty store (matching
      every other store's first-load behavior).
- [x] 2.2 Wire the new store into `OnSaveGameLoaded`/`OnGameWorldSave` (`ScribeModSystem
      .ServerLifecycle.cs`) alongside `assignmentStore`/`questDecisionStore`/`playerLocationStore`/
      `timerStores`, with its own `SaveGame.StoreData`/`GetData` key. Verify by an Atlas
      integration test: queue a pending entry, save and reload the world, confirm the entry is
      still present in the reloaded store.

## 3. Shared periodic scan (snapshot + flush)

- [x] 3.1 Add a per-player in-memory "last known carried document ids" snapshot
      (e.g. `Dictionary<string playerUID, HashSet<Guid>>`) to `ScribeModSystem`, populated only via
      the tick in 3.2 (no other write path). Verify by inspection that nothing else populates or
      reads it.
- [x] 3.2 Widen the storm tick's registration interval from 5000ms to 10000ms
      (`ScribeModSystem.cs`) and extend its per-online-player body
      (`ScribeModSystem.History.cs`, currently `OnStormTick`) to do three things per player each
      firing: (a) the existing storm rising-edge check/write, unchanged; (b) walk
      `FindCarriedNotebooks` (on-person only — do NOT use `FindAllCarriedNotebookRecords`, which
      would also poll the reflection-based CarryOn bridge every firing for every player) and
      replace that player's snapshot with the current set of document ids; (c) for every document
      id just seen, look it up in the pending store from Task 2 and, if it has queued entries,
      write them into that document's `HistoryStore` via `TryAddEntry` (which now inserts them in
      correct chronological order per Task 1) and `FlushHistory`, then clear them from the pending
      store. Verify with an Atlas integration test: seed a pending entry for a document, put that
      document into a live carried slot, fire the tick directly, and confirm the entry is now in
      the document's history (in the right chronological position) and no longer pending.
- [x] 3.3 Update the tick's name and doc comment to reflect its dual purpose (storm detection +
      carried-document snapshot/flush), since it is no longer storm-only. Verify existing storm
      Atlas tests still pass unmodified after the rename.

## 4. `OnEntityDeath` fallback queuing (victim only)

- [x] 4.1 In the Death branch of `OnEntityDeath`, after the existing live
      `FindAllCarriedNotebookRecords(sp)` scan and write: for each document id present in `sp`'s
      last-known snapshot (Task 3.1) but NOT among the document ids found by the live scan, build
      the same Death entry the live path would have written (including its `InGameTimestamp`
      captured at this actual moment of death) and queue it into the pending store under that
      document id. Do not queue a document id that WAS found live (it was already written; queuing
      it too would double-write). Verify with an Atlas integration test: seed the victim's snapshot
      with a document id, remove that document from the victim's live inventory (simulating another
      mod having evicted it first), trigger the death, and confirm (a) no entry is written
      immediately, and (b) the pending store now holds the queued entry for that document id with a
      timestamp matching the moment of death.
- [x] 4.2 Confirm no fallback queuing is added to the killer's side of the PvP branch — the
      killer's write stays exactly the live-scan-only path it is today. Verify with an Atlas
      integration test: reproduce the same "inventory evicted before Scribe observes it" setup for
      the killer instead of the victim during a PvP kill, and confirm the existing (unchanged)
      behavior holds — no PvpKill entry is written and none is queued.

## 5. Regression coverage

- [x] 5.1 Atlas integration test: a queued entry is written only to the exact document it was
      queued against — queue an entry against document A, then have the same player craft a new
      Notebook (document B) and carry only B when the shared tick next fires; confirm B's history
      gains nothing and the pending entry for A is still queued and unaffected.
- [x] 5.2 Atlas integration test: a queued entry writes correctly regardless of who is currently
      carrying the target document — queue an entry against a document that has since changed
      hands (a different player now carries it), fire the tick, and confirm the entry is written
      into that document exactly as if the original player had recovered it.
- [x] 5.3 Atlas integration test: the ordinary, no-interference case is unaffected — a player dies
      while carrying a Notebook with nothing else touching their inventory; confirm the Death entry
      is written immediately by the live scan (no queuing involved at all).
- [x] 5.4 Atlas integration test: a Death entry queued by the fallback and flushed after the
      notebook gained other entries during the delay (both chronologically before and after the
      actual death) lands in its correct chronological position among them, not at the end.

## 6. Verification

- [x] 6.1 Run the full Atlas suite locally (per `build/install-hooks.sh`'s pre-push gate) with
      `VINTAGE_STORY` pointed at the local install, and confirm all new and existing tests pass.
- [ ] 6.2 Manually play-test in-game: die while carrying a Notebook under normal conditions (no
      competing mod installed) and confirm the Death entry still appears immediately, exactly as
      before — no regression to the common path.
- [ ] 6.3 Manually play-test: trigger several history events (a couple of deaths, a manual entry,
      a storm) across more than one in-game day on the same notebook and confirm the History tab
      still shows them newest-first in the correct real order, including any same-day pair.
- [ ] 6.4 If practical, manually play-test with a corpse/keep-inventory-style mod installed
      alongside Scribe (e.g. the PlayerCorpse mod used during investigation): die while carrying a
      Notebook, confirm no entry appears immediately, then recover or relocate the Notebook and
      confirm the Death entry appears within one tick interval (~10s) of it next being carried, in
      its correct chronological position.
