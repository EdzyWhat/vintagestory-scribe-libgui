## Context

Two facts block a durable reference to a specific task, which every pinned-task surface needs:

1. **Tasks have no stable identity.** A task is identified only by its integer index in the
   document's `List<ScribeBlock>`; the index is not persisted and shifts on move/insert/delete.
2. **Documents have no identity.** A document lives inline in its block entity's tree attributes
   with no id of its own, so nothing can name "this document's task X" across a relocation.

This change adds `DocId`/`TaskId` and the one codec bump they require, and nothing else. It is
carved out of the larger `add-pinned-task-foundation` work specifically to isolate the
save-format migration — the single place a mistake corrupts existing saves — as a small,
dependency-free, unit-tested Core change (per ROADMAP "Codec version-aware read migration").

Constraints: `src/Core/` must stay free of the Vintage Story API (`System.Guid` is BCL and is
fine); no new dependencies; the codec keeps its single global version line and append-only
discipline (`docs/specs/README.md` convention #1). CI builds/tests Core only.

## Goals / Non-Goals

**Goals:**
- Stable `Guid DocId` (per document) and `Guid TaskId` (per task), persisted and preserved
  across every mutation and through serialization.
- Codec v4: write v4, still read v3; drop the per-block `Pinned` flag; surface v3's
  previously-pinned task ids through a migration seam.
- Task lookup by id (`FindByTaskId`) and delete-reports-id (`DeleteBlock(int, out Guid?)`).
- Prove it all with Core unit tests; no game install required.

**Non-Goals (the follow-on `add-pinned-task-foundation` change):**
- The per-player pin store, its persistence, and its sync.
- Re-wiring the lectern GUI's pin button/tint away from the removed `Pinned` flag.
- Break→re-place document retention; the block-entity `MarkDirty`-on-v3-load re-save.
- Actually draining v3 legacy pins into a store (this change only *surfaces* them).

## Decisions

### D1 — Stable identity via `System.Guid`, stored inside the codec
Add get-only `Guid TaskId` to `ScribeBlock` (ctor param `Guid? taskId = null`, assigned
`taskId ?? Guid.NewGuid()`) and `Guid DocId` to `ScribeDocument` (assigned `Guid.NewGuid()` on
construction, with `internal void SetDocId(Guid)` for the codec's read path). Both serialize as
16 raw bytes, so they round-trip through persistence and — crucially for the follow-on change —
through the break→item→replace path, meaning a relocated lectern keeps the same ids. Ids are
stable across `MoveBlock`/`ToggleTask`/`SetBlockText`/`InsertTask` by construction (the same
`ScribeBlock` object is retained; a new insert gets a fresh Guid).
- *Why Guid over an int/long counter:* globally unique with no per-document counter to persist
  or collide, which the roadmap copy/paste-between-documents feature needs (ids must not collide
  across documents). Guid is BCL, so it does not violate the Core API-free rule.
- *Alternative rejected:* a per-document incrementing `long` — smaller, but needs a persisted
  "next id" per document and collides when tasks move between documents.

### D2 — Codec v4: write v4, read v3 and v4; drop `Pinned`
Bump `ScribeDocumentCodec.Version` to 4. v4 layout adds a 16-byte `DocId` after the header and a
16-byte `TaskId` per block, and **removes** the per-block `pinned` bool. The reader accepts
version 3 or 4 and rejects all others (fail-safe): the v4 path reads the persisted ids; the v3
path generates fresh ids and reads-then-discards the old `pinned` bool. All existing guards and
caps (`MaxBlocks`, `MaxTextLength`, malformed/short-read → return false) are preserved. This
keeps the single global version line (`docs/specs/README.md` #1); the next feature that needs a
field appends as v5 after v4's layout.
- *Why keep reading v3:* existing saved worlds must keep loading. The version byte was designed
  for exactly this evolution.
- *Migration seam:* add a companion `TryDeserialize(bytes, out ScribeDocument? doc, out
  IReadOnlyList<Guid> legacyPinnedTaskIds)`. On v4 the list is empty; on v3 it returns the
  (freshly-generated) `TaskId`s of blocks whose old `pinned` was true, so the follow-on change
  can seed the pin store. The existing two-arg `TryDeserialize` stays signature-stable (dozens
  of call sites) and routes through the three-arg overload, discarding the list.

### D3 — Delete reports the removed id; lookup by id
`DeleteBlock(int index, out Guid? deletedTaskId)` reports the removed block's `TaskId` (or
`null`/false for an invalid index), and a thin `DeleteBlock(int)` overload preserves existing
call sites. Add `ScribeBlock? FindByTaskId(Guid)` (returns the matching block or null). These
are the primitives the follow-on change's soft-orphan and pin-resolution paths call, but they
are pure Core operations testable here with no game install.

## Risks / Trade-offs

- **One-way version bump** → v4 saves can't be read by older builds. Mitigation: acceptable for
  a single-user/self-hosted mod; documented; no downgrade path offered.
- **Removing `Pinned`/`TogglePinned` breaks the Mod build in isolation** → the lectern GUI still
  reads `Pinned` today. Mitigation: this change is sequenced immediately before, and merged
  together with, `add-pinned-task-foundation`, which re-points that one consumer. Core tests
  pass standalone; the combined branch is what builds `src/Mod/`.
- **Append-only codec discipline** → a future field must append as v5 after v4's layout, never
  interleave. Mitigation: record the v4 field order in the codec and in `docs/specs/README.md`
  #1 when this lands.

## Migration Plan

- **Forward:** on loading a v3 world, each document deserializes via the v3 path (fresh ids
  generated) and the migration seam surfaces its previously-pinned task ids. This change stops
  there (surfacing only); the follow-on change marks the block entity dirty so it re-saves as v4
  and drains those ids into the current player's pin store.
- **Rollback:** none within a world once saved as v4 (one-way bump). Restore a pre-v4 world
  backup to roll back the build.

## Open Questions

- None blocking. The follow-on change owns everything that consumes this identity.
