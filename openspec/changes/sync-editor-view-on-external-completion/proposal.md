## Why

When the editor view is open, it renders from a private `scratch` copy of the document that is seeded
once at `EnterEditorMode` and never re-synced for content while editing. If the same player completes
one of that document's tasks from the **HUD** (or any external path) under a policy that keeps the task
in the document (`Keep`, `Sink`), two things go wrong:

1. **Stale render** — the open editor's checkbox never reflects the completion, even though the live
   document changed server-side and the Read/Pinned views update correctly.
2. **Lost update (data loss)** — the completion is applied to the live document lock-free
   (`SetTaskDoneFromReader`), but the editor's next autosave flush does a whole-document replace
   (`ApplyEdit`) from the stale `scratch` (still `done=false`, original order), silently reverting the
   HUD's completion and any sink reorder. Last-write-wins, and the stale editor wins.

The reverse direction (editor/read completion → HUD) already works, and the Pinned/Read views were just
fixed for the render half. The editor is the remaining surface, and it is the only one that also carries
the silent revert.

## What Changes

- The editor's inbound resync handler (`RefreshReadView`, in editor mode) SHALL propagate an external
  **completion** into the open `scratch` document for tasks that still exist server-side — updating the
  task's done-state **and** applying the completion policy's document effect (the `Sink`/`UnpinSink`
  move-to-bottom reorder) live in the editor — while continuing to never overwrite a row's in-progress
  unsaved **text**.
- Because `scratch` is then consistent with the live document, the subsequent autosave flush no longer
  reverts the external completion — the lost-update conflict is closed as a direct consequence, not via
  a separate lock.
- The live reorder SHALL preserve the actively-edited row's caret/in-progress text and cross-row focus,
  reusing the editor's existing reconcile-with-stable-identity machinery (the same guarantees that a
  local insert/delete/reorder already provides).
- The existing guard is retained: a legitimately-local, not-yet-persisted in-flight row is never pruned
  by the resync; the focused row and empty tasks are never dropped.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `scribe-dialog-base`: the "external resync landing mid-edit" requirement is broadened from "don't drop
  a local in-flight row" to also require propagating an external completion (done-state + policy reorder)
  into the open editor's scratch, so the open editor reflects it live and a later flush does not revert
  it.

## Impact

- **Code:** `src/Mod/ScribeDialogBase.ViewSwitching.cs` (`RefreshReadView` editor-mode branch — today it
  only *deletes* server-dropped tasks; it will also sync done-state + reorder for surviving tasks);
  companion render resync in `src/Mod/ScribeEditorContent.cs` (`ScribeEditRowState` needs an
  `UpdateWidget` done-resync so the reused row repaints when scratch's done flips — mirroring
  `ScribeReadRowState`/`ScribePinRowState`). Reuses the existing `ReorderEditorBlock` /
  reconcile path for the live move.
- **Core:** no `src/Core/` change expected — the completion-policy document semantics already live in the
  shared Core function; this change applies that same result into scratch on the client.
- **Persistence/sync:** no new packet or lock; relies on the existing block-entity resync
  (`MarkDirty` → `FromTreeAttributes` → `RefreshReadView`) and the existing `ApplyEdit` flush.
- **Multiplayer:** narrows an existing last-write-wins window for the completion case; the general
  concurrent whole-document edit race documented on `SetTaskTextFromReader` is unchanged and out of scope.
- **Tests:** GUI-layer behavior gated by the in-game parity checklist; any pure ordering/merge logic that
  can be expressed against Core is covered in `tests/Core.Tests`.
