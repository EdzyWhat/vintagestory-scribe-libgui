## 1. Core identity & delete/lookup

- [x] 1.1 Add get-only `Guid TaskId` to `ScribeBlock` (ctor param `Guid? taskId = null`, assign `taskId ?? Guid.NewGuid()`); remove the `Pinned` field and its ctor parameter.
- [x] 1.2 Add `Guid DocId` to `ScribeDocument` (assigned `Guid.NewGuid()` on construction) + `internal void SetDocId(Guid)` for the codec; remove `TogglePinned`.
- [x] 1.3 Change delete to `DeleteBlock(int index, out Guid? deletedTaskId)` and keep a thin `DeleteBlock(int)` overload for existing callers; add `ScribeBlock? FindByTaskId(Guid)`.

## 2. Core codec v4

- [x] 2.1 Bump `ScribeDocumentCodec.Version` to 4; write v4 layout (16-byte DocId in header, 16-byte TaskId per block, no `pinned` byte); keep `MaxBlocks`/`MaxTextLength` and all fail-safe guards.
- [x] 2.2 Make the reader accept v3 and v4: v4 reads persisted ids; v3 generates fresh DocId/TaskId and reads-then-discards the old `pinned` bool; reject all other versions.
- [x] 2.3 Add the migration seam `TryDeserialize(byte[]?, out ScribeDocument?, out IReadOnlyList<Guid> legacyPinnedTaskIds)` (v4 → empty; v3 → the new TaskIds whose old `pinned` was true); route the two-arg overload through it, discarding the list.
- [x] 2.4 Record the v4 field order in a codec comment and in `docs/specs/README.md` convention #1 so the next migration appends as v5 rather than colliding.

## 3. Core tests

- [x] 3.1 Codec v4 round-trip preserves `DocId` + every `TaskId`; id stability across `MoveBlock`/`ToggleTask`/`SetBlockText`/`InsertTask` (ids unchanged; inserted block gets a distinct id).
- [x] 3.2 v3→v4 migration: hand-build v3 bytes (old layout incl. `pinned`), assert success, generated DocId, per-block TaskId, and `legacyPinnedTaskIds` equals exactly the ids of blocks whose `pinned` was true.
- [x] 3.3 Update `TryDeserialize_EarlierVersionBytes_FailsSafely` to target version 1/2 (v3 is now accepted) and add a positive `TryDeserialize_V3Bytes_Succeeds`; rename/adjust the pinned round-trip test to drop pinned assertions.
- [x] 3.4 `ScribeDocumentTests`: `DeleteBlock(index, out id)` returns the removed TaskId (and false/null on bad index); `FindByTaskId` hit/miss.

## 4. Verify

- [x] 4.1 `dotnet test tests/Core.Tests` green (no game DLL required).
- [x] 4.2 Sync the `task-note-document` delta to the main spec per the OpenSpec flow — but hold archiving until the follow-on `add-pinned-task-foundation` change (which re-points `Pinned`'s consumer and builds `src/Mod/`) is also green, since the two are merged together. (2026-07-24: delta synced to main; both changes archived together once pinned-task 7.1–7.8 were confirmed.)
