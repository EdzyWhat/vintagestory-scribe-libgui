# Tasks — sync-editor-view-on-external-completion

> View-layer / client-merge only. No `src/Core/` model/policy change and no new packet or lock — the
> completion is already applied server-side lock-free and pushed via the existing block-entity resync.
> Two coupled halves: the **data** half (propagate done-state + reorder into scratch in `RefreshReadView`,
> design D1/D2/D4) and the **render** half (`ScribeEditRowState.UpdateWidget` done-resync, design D3).

## 1. Baseline

- [x] 1.1 Re-confirm the current `RefreshReadView` editor-mode branch behavior
      (`ScribeDialogBase.ViewSwitching.cs:341-401`): it walks the authoritative server task list, deletes
      scratch tasks the server dropped, and skips the focused row and empty tasks. Note the exact loop and
      the `serverTaskIds.Contains(...)` survival check that the done/reorder sync will hook into.
- [x] 1.2 Confirm build + Core suite green before changes (`dotnet build src/Mod/Mod.csproj`,
      `dotnet test tests/Core.Tests`).

## 2. Data half — propagate completion into scratch (D1, D2, D4)

- [x] 2.1 In the `RefreshReadView` editor-mode branch, for each task present in BOTH the server snapshot
      and scratch, if `server.Done != scratch.Done`, set the scratch block's done-state to the server
      value. Do NOT touch the block's text (scratch stays the source of truth for in-progress text, D-nongoal).
      *(Implemented via `ToggleTask(i)` called only on a mismatch — flips to match — and `isDirty=true`.)*
- [x] 2.2 When the completion policy moved the task within the live document (the `Sink`/`UnpinSink`
      move-to-bottom), apply the same move in scratch via the existing `ReorderEditorBlock` so the
      reconcile path's caret/text/focus/scroll guarantees are reused (D2). A move-to-same-index is a no-op.
      *(Detected as "just-completed task is now the last server task"; new `preserveFocusedRow` param keeps
      the edited row's focus rather than grabbing it onto the sunk row.)*
- [x] 2.3 Ensure the new done/reorder sync applies only to surviving tasks and does not double-handle a
      task also covered by the existing delete branch in the same pass; keep the in-flight/focused/empty-row
      guards intact (D5). *(Delete pass runs first over absent tasks; done/reorder pass keys on
      `serverDone.TryGetValue` so only surviving tasks match; delete guards untouched.)*
- [x] 2.4 Verify (by reasoning + a debug trace if needed) that after the sync, a subsequent
      `FlushIfDirty`/`ApplyEdit` serializes the corrected scratch, so the flush carries the external
      completion rather than reverting it (D4 — the lost-update fix falls out). *(Reasoned: `isDirty=true`
      is set on each synced toggle and by `ReorderEditorBlock`; in-game §4.4 confirms.)*

## 3. Render half — repaint the reused editor row (D3)

- [x] 3.1 Add an `UpdateWidget(ScribeEditRow oldWidget)` done-resync to `ScribeEditRowState`
      (`ScribeEditorContent.cs`) mirroring `ScribeReadRowState`/`ScribePinRowState`: after the existing
      focus-node migration, `if (oldWidget.Data.Done != Widget.Data.Done) done = Widget.Data.Done;` so the
      reused (TaskId-keyed) row repaints its checkbox when scratch's done flips. The gate prevents stomping
      an in-flight local optimistic tick.

## 4. Verify (in-game parity gate — do not skip)

- [x] 4.1 Build clean (0 errors, no new warnings); Core suite green (339/339); restage Debug (103 files).
- [x] 4.2 Manually test in-game — **Keep policy**: open the editor on a document, complete one of its tasks
      from the HUD → the editor's checkbox for that task checks live (no reopen), and no other row's
      in-progress text or caret is disturbed.
- [x] 4.3 Manually test in-game — **Sink / UnpinSink**: complete a task from the HUD while the editor is
      open → the row marks done AND moves to the bottom live in the editor, matching the Read and Pinned
      views; if the row being sunk is the one actively being edited, its caret and in-progress text survive
      and focus is not lost/leaked.
- [x] 4.4 Manually test in-game — **lost-update**: complete from the HUD (Keep or Sink), then make an
      unrelated edit in the editor to trigger a flush; reopen/observe → the completion (and sink order) is
      NOT reverted by the flush.
- [x] 4.5 Manually test in-game — **no double-application jump**: complete a task from the editor itself
      (its own optimistic path) and let the server resync round-trip back → no visible re-tick or jump.
- [x] 4.6 Regression-check the in-flight guard: create a new not-yet-persisted row, then trigger an
      external resync that lacks it → the local row is retained, caret/focus undisturbed.
- [x] 4.7 Regression-check the sibling surfaces (Read, Pinned) still update on external completion exactly
      as before (unchanged by this change).

## 5. Docs & close-out

- [x] 5.1 Add a CHANGELOG `Fixed` entry: completing a task from the HUD while the editor is open now
      updates the open editor live and is no longer reverted by the editor's next save. (Folded into the
      existing Pinned/Read fix entry so the two read coherently.)
- [x] 5.2 If any non-obvious mechanism was learned (scratch-merge boundary, flush-revert window), add a note
      to `VSAPI-NOTES.md` / update the relevant memory so it isn't re-derived. *(Recorded as memory
      `editor-scratch-external-completion-merge`: scratch merges done-state + completion reorder, never text;
      isDirty closes the flush-revert; ReorderEditorBlock `preserveFocusedRow`.)*
- [x] 5.3 `openspec validate sync-editor-view-on-external-completion --strict` passes.
- [x] 5.4 Record playtest verdicts in `TESTING.md` (regenerate via the what-to-test skill).
