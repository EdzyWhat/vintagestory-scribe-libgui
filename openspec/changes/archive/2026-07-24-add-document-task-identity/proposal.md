## Why

The pinned-task feature (and every later surface that references a specific task — a HUD, a
Pinned tab, cross-block aggregation, copy/paste between documents) needs a **durable way to
name a task**. Today a task is identified only by its integer index in the document's
`List<ScribeBlock>`; that index is not persisted (it is just list position) and shifts on
move/insert/delete, so nothing can hold a stable reference to a specific task. The document
itself likewise has no identity of its own.

This change adds that identity — a stable `DocId` per document and `TaskId` per task — and
performs the one save-format bump it requires, **in isolation**. It deliberately carries no
game hooks, no networking, and no feature logic: it is pure `src/Core/` plus unit tests, so
the one place where a mistake corrupts existing saves (the codec version migration) lands as
a small, dependency-free, fully-unit-tested change before anything builds on it. This mirrors
the roadmap's "Codec version-aware read migration (standalone Core change)" guidance.

The per-player pin store, its sync, break/replace retention, and the lectern GUI re-wire all
live in the follow-on `add-pinned-task-foundation` change, which depends on this one.

## What Changes

- Add a stable, persisted `DocId` to each document and a stable `TaskId` to each task block,
  generated on creation and preserved across every mutation (reorder, insert, delete other
  blocks, edit text, toggle completion).
- **BREAKING (serialization):** bump `ScribeDocumentCodec` to version 4 — write v4, still read
  v3. v4 carries the new ids and **drops the per-block `Pinned` flag entirely** (pinning stops
  being document state; the follow-on change relocates it to a per-player store).
- **BREAKING (model):** remove `ScribeBlock.Pinned` and `ScribeDocument.TogglePinned`. Provide a
  migration seam so a v3 document's previously-pinned tasks are surfaced (by their freshly
  generated `TaskId`s) for the follow-on change to drain into the per-player store.
- Add `ScribeDocument.FindByTaskId(Guid)` and change delete to report the removed task's id
  (`DeleteBlock(int, out Guid? deletedTaskId)`, with a thin `DeleteBlock(int)` overload for
  existing callers), so callers can react to a specific task's removal.

## Capabilities

### Modified Capabilities
- `task-note-document`: add stable `DocId`/`TaskId` identity preserved across mutations and
  serialization; add task lookup by id and delete-reports-id; remove the shared per-task pin
  toggle and the pin field from serialization; codec v4 writes the new format and still reads
  v3 (surfacing which tasks were pinned so a caller can migrate them).

## Impact

- **Core (`src/Core/`, stays API-free):** `ScribeBlock` (add `Guid TaskId`, drop `Pinned`),
  `ScribeDocument` (add `Guid DocId`, drop `TogglePinned`, add `FindByTaskId`, `DeleteBlock`
  reports the removed id), `ScribeDocumentCodec` (v4 write / v3+v4 read + migration seam).
  `System.Guid` is BCL, so the Core API-free invariant is preserved.
- **Tests (`tests/Core.Tests`):** v4 round-trip, id stability across mutations, v3→v4 migration
  surfacing legacy-pinned ids, version fail-safe, `FindByTaskId`, delete-reports-id.
- **Serialization compatibility:** existing v3 saved worlds keep loading (read-v3, write-v4 on
  next save); the version bump is one-way (v4 is not readable by older builds).
- **No game hooks in this change.** Callers that toggled `Pinned` (the lectern GUI) and the
  block-entity persistence are updated in the follow-on `add-pinned-task-foundation` change,
  which is where `Pinned`'s one consumer is re-pointed. Until then this change is Core-only and
  will not compile against `src/Mod/` on its own — it is sequenced immediately before, and
  merged together with, the follow-on change.
- **No new dependencies**; CI still builds/tests Core only.
- **Depends on** the `add-lectern-row-affordances-libgui` branch (builds on its current
  GUI/codec code, not yet merged to `main`).
