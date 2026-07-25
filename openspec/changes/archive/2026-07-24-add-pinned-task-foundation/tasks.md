> **Prerequisite:** the Core identity + codec work (`DocId`/`TaskId`, codec v4 write / v3+v4 read
> + migration seam, `FindByTaskId`, delete-reports-id, and their tests) lives in the
> `add-document-task-identity` change and is assumed complete here. The two changes are merged
> together; this one re-points `Pinned`'s former consumer and builds `src/Mod/`.

## 1. Core pin & settings types

- [x] 1.1 Add `ScribePinnedRef` (API-free): `OwnerDocId`, `TaskId`, `PinnedAtTotalHours`, `Orphaned`, `LastKnownText`, `LastKnownDone`.
- [x] 1.2 Add `ScribePlayerSettings` (API-free): `bool CompleteUnpins = true` (+ reserved `bool HudCollapsed`), with a default-construct that reflects the defaults.
- [x] 1.3 Add `ScribePinCodec`: a list codec (magic `SPIN`, for the network message), a store codec (magic `SPST`, `Dictionary<uid, List<ScribePinnedRef>>`, for the savegame blob), and a settings codec (magic `SPSE`, per-player `ScribePlayerSettings`), each fail-safe and versioned; add `MaxPinsPerPlayer = 500` and enforce it on deserialize.

## 2. Core tests

- [x] 2.1 `ScribePinCodecTests`: list + store + settings round-trip (all fields), fail-safe on empty/garbage/truncated, reject over `MaxPinsPerPlayer`, reject over-long `LastKnownText`, unknown magic/version; settings absent → defaults (CompleteUnpins true).

## 3. Mod store & persistence

- [x] 3.1 Add `ScribePinStore` (ModSystem-owned): `Dictionary<uid, List<ScribePinnedRef>>` + `Dictionary<uid, ScribePlayerSettings>` + `Dictionary<Guid DocId, BlockPos>` live index; methods Get / GetSettings / SetSettings / SetPin / RemovePin / PlayersPinning / RefreshSnapshots (soft-orphan on vanished TaskId) / OrphanAll / RegisterDoc / UnregisterDoc / MigrateLegacyPins.
- [x] 3.2 Wire persistence: load on `sapi.Event.SaveGameLoaded` from `SaveGame.GetData("scribe:pins:v1")` (pins + settings; empty/defaults on null/failure); save on `sapi.Event.GameWorldSave` via `StoreData` using `ScribePinCodec` (store + settings blobs).

## 4. Mod networking

- [x] 4.1 Add `ScribeSetPinMessage` (C→S: `byte[]` DocId, `byte[]` TaskId, `bool Pinned`), `ScribeCompleteTaskMessage` (C→S: `byte[]` DocId, `byte[]` TaskId), `ScribePinnedSetMessage` (S→C: `byte[]` PinnedRefBytes), and `ScribePlayerSettingsMessage` (S→C: `byte[]` settings blob) as ProtoContract types.
- [x] 4.2 Update the `"scribe"` channel `RegisterMessageType` chain in `ScribeModSystem.Start`: **remove** `ScribeToggleTaskMessage` (retired — D5b) and **append** the four new messages — identical order on both sides, never reordering the surviving originals; add the server handlers (`ScribeSetPinMessage`, `ScribeCompleteTaskMessage`) and client handlers (`ScribePinnedSetMessage`, `ScribePlayerSettingsMessage`).
- [x] 4.3 Add client-side pin cache + settings cache fields on `ScribeModSystem` populated by `OnClientReceivedPinnedSet` / `OnClientReceivedPlayerSettings`; expose `bool IsPinnedForMe(Guid docId, Guid taskId)` and the current settings.
- [x] 4.4 Push triggers: initial per-player filtered pins + settings push on `sapi.Event.PlayerNowPlaying`; a re-push-affected-player helper called after any store mutation/snapshot/settings change.
- [x] 4.5 `OnServerReceivedSetPin`: **pin add** → resolve BE via the live index (no lock check, like `ToggleTaskFromReader`), `FindByTaskId`, snapshot text/done from the server's own document, `SetPin`; **pin remove** → `RemovePin` by `(DocId, TaskId)` with no block resolution; re-push that player.
- [x] 4.6 `OnServerReceivedCompleteTask`: resolve the document via the live index (lock-free), `FindByTaskId`, toggle `Done` via the existing `ToggleTaskFromReader`/`MarkDirty`/`RefreshSnapshots` path; then if the player pinned this task and their `CompleteUnpins` is on, `RemovePin` and re-push. An unresolvable/orphaned `(DocId, TaskId)` → `RemovePin` only (no document mutation).

## 5. Mod document lifecycle

- [x] 5.1 Add `ScribeDocumentAttributes` helper (`WriteTo(ItemStack, ScribeDocument)` / `TryReadFrom(ItemStack)` over `stack.Attributes["scribeDocument"]` reusing the codec + key).
- [x] 5.2 Override `BlockScribeLectern.OnPickBlock` and `GetDrops` to write the document onto the drop stack (BE is still alive during drops); override `BlockEntityScribeLectern.OnBlockPlaced(ItemStack)` to restore it (empty-doc fallback).
- [x] 5.3 Live index: register `DocId→Pos` in the BE `Initialize` (server) and unregister + `OrphanAll(DocId)` in `OnBlockRemoved`; do NOT touch the store in `OnBlockUnloaded`.
- [x] 5.4 Call `RefreshSnapshots(DocId, Document)` after `ApplyEdit` and after completion (soft-orphaning any vanished TaskId), re-pushing each affected player.
- [x] 5.5 MarkDirty a v3-detected lectern on first load so the generated DocId/TaskIds persist as v4 (else ids regenerate each load and pins can't stick).
- [x] 5.6 Drain legacy pins: on first `PlayerNowPlaying` (single-player scope), for each loaded lectern migrate its `legacyPinnedTaskIds` (surfaced by the codec seam) into the current player's store via `MigrateLegacyPins`.

## 6. Mod GUI re-wire

- [x] 6.1 Change the row-data records (`ScribeReadRowData`/`ScribeEditRowData`) to carry `Guid TaskId` instead of `bool Pinned`; update all constructors/call sites.
- [x] 6.2 `TogglePinnedEditorTask(index)` sends a `ScribeSetPinMessage` for `Blocks[index].TaskId` (toggled against the client cache) instead of mutating `scratch`/autosaving.
- [x] 6.3 Re-wire the read-view checkbox to send `ScribeCompleteTaskMessage` for the row's `(DocId, TaskId)` instead of the retired positional toggle; the server applies completion + conditional unpin (task 4.6).
- [x] 6.4 Drive the resting pinned tint + pin-glyph accent color from `IsPinnedForMe(lectern.Document.DocId, row.TaskId)` in both views; repaint the dialog when a `ScribePinnedSetMessage` (or `ScribePlayerSettingsMessage`) arrives.

## 7. Verify

- [x] 7.1 `dotnet test tests/Core.Tests` green; `dotnet build src/Mod/Mod.csproj` clean (0/0). (Assumes the `add-document-task-identity` Core work is present on the branch.)
- [x] 7.2 Atlas integration (`tests/Integration.Tests`, local-only): pin is server-observable in the store; pins survive a server restart (`RestartWorld = true`); per-player isolation (two players, disjoint sets); settings default (CompleteUnpins true) persist across restart. *(Restart-persistence for pin + non-default setting: `PersistenceScenarios`. Observability/isolation: `PinScenarios`.)*
- [x] 7.3 Atlas integration: unpin by `(DocId, TaskId)` succeeds with the owning lectern **removed** (orphaned) and with its chunk **unloaded** (no block resolution required). *(`PinScenarios.Unpin_works_after_the_lectern_is_removed` + `Unpin_works_while_the_owning_chunk_is_unloaded`.)*
- [x] 7.4 Atlas integration: complete-by-identity marks Done and, with `CompleteUnpins` on, removes only the completing player's pin (a second pinner retains, snapshot Done); with the opt-out set, complete does NOT unpin; actioning an orphaned pin removes it with no document mutation. *(`PinScenarios` completion group + `Completing_an_orphaned_pin_removes_it_with_no_document`.)*
- [x] 7.5 Atlas integration: break→replace keeps `DocId` and the pin still resolves; soft-orphan on editor-deleted task (snapshot retained); an unload-style non-event does NOT orphan. *(`PinScenarios.Breaking_then_replacing_keeps_the_docid_and_the_pin_resolves`, `Deleting_a_pinned_task_in_an_edit_soft_orphans_the_pin`; unload≠orphan asserted in the 7.3 unload scenario.)*
- [x] 7.6 Atlas integration: load a v3 fixture world → the previously-pinned tasks appear in the single player's store and the lectern re-saves as v4. *(`MigrationScenarios`, booting the irreplaceable `fixtures/lectern-v3.vcdbs`.)*
- [x] 7.7 Update `PinScenarios`/`FixtureBuilders` in the integration suite to stop using `TogglePinned`/`Blocks[i].Pinned`/`ScribeToggleTaskMessage` and drive the new store/handler API instead.
- [x] 7.8 In-game (restage + relaunch): pin/unpin a task → resting tint reflects per-player state, persists across relog and across break→replace; checking a task in the read view completes it and (default) removes the pin; record verdicts in `TESTING.md`. (Confirmed 2026-07-24: parts a–d all work; part d verified server-side via `[scribe]` trace + persist-on-reopen + pin repaint. Verdicts recorded in TESTING.md `7f3826e7`.)
- [x] 7.9 Sync deltas to main specs and archive both changes per the OpenSpec flow once 7.1–7.8 are confirmed. (2026-07-24: synced add-document-task-identity's task-note-document delta, then this change's task-note-document/lectern-block/player-pins deltas — creating the new player-pins main spec — all validating strict; archived both to openspec/changes/archive/2026-07-24-*.)
