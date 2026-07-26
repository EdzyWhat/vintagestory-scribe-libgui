## 1. Resolve open questions

- [x] 1.1 Confirm top-of-list / only-row focus behavior on auto-delete (design Q1) — CONFIRMED (D4 recommended set): focus row above (i−1); first-row delete → focus new first row; only-row delete → empty-state, no focus
- [x] 1.2 Confirm the walk-up cascade of consecutive empty rows is desired (design Q2) — CONFIRMED: cascade allowed (repeated blur walks up deleting consecutive empties)
- [x] 1.3 Confirm auto-delete applies to any empty task, not only freshly-created ones (design Q3) — CONFIRMED: content-based, applies to any empty task row
- [x] 1.4 Decide the fate of `scribe-gui-newtask-placeholder`: remove vs. ghost placeholder (design Q4/D6) — DECIDED (D6b): repurpose as ghost placeholder text in the empty focused field (adds placeholder rendering to `ScribeMultilineField`)
- [x] 1.5 Confirm Enter on an already-empty row is a no-op rather than stacking an empty task (design Q5) — CONFIRMED: Enter on an empty row is a no-op on the row set
- [x] 1.6 Confirm sequencing relative to the in-flight row-affordances/pin changes (design Q6) — DECIDED: proceed now (behaviorally independent; row-affordances/pin changes already merged/archived on this branch history)

## 2. Core model: allow empty task text

- [x] 2.1 Remove the `IsNullOrWhiteSpace` rejection from `ScribeDocument.AddTask`
- [x] 2.2 Remove the `IsNullOrWhiteSpace` rejection from `ScribeDocument.InsertTask`
- [x] 2.3 Remove the blank-rejection from the task branch of `ScribeDocument.SetBlockText` (store verbatim)
- [x] 2.4 Update XML doc-comments on those methods to reflect that empty task text is now accepted
- [x] 2.5 Update `tests/Core.Tests/ScribeDocumentTests.cs`: replace the "blank task text is rejected"
      cases with assertions that empty/whitespace-only task text is accepted and stored verbatim
- [x] 2.6 Run `dotnet test tests/Core.Tests` and confirm green (103 passed)

## 3. Editor: create new tasks empty

- [x] 3.1 `OnClickAddTask` seeds `""` instead of `Lang.Get("scribe:scribe-gui-newtask-placeholder")`
- [x] 3.2 `EditorInsertTaskBelow` seeds `""` instead of the placeholder lang string
- [x] 3.3 Verify the new empty row still auto-focuses (unchanged `autoFocusRowOnRebuild` path) — both sites still set `autoFocusRowOnRebuild`
- [x] 3.4 Apply the Q4 decision to the lang string (D6b): repurposed as ghost placeholder — `ScribeMultilineField` gains `Placeholder`/`PlaceholderColor` rendering (dimmed hint when empty, task rows only); lang string now "New task…"

## 4. Editor: empty-task self-destruct on blur

- [x] 4.1 Wire `ScribeMultilineField.OnBlur` through `ScribeEditRow` (new `onRowBlurred` param) to dialog `OnRowBlurred(index)`
- [x] 4.2 Implement `OnRowBlurred`: task + empty → schedule removal; DEFERRED to `OnRenderGUI` (pendingEmptyRowRemoval)
      then `DeleteEditorBlock` (reuses scratch-mutate + focus-fixup + collapse + rebuild). Re-reads scratch by index
      and re-checks emptiness at both blur and removal time; no-ops on stale/out-of-range index or a text section.
      (Deferred because blur fires inside the focus-notification / mid focus-transition — a synchronous rebuild would
      dispose focus nodes and strand the incoming focus; same guard as `needsEditorCollapseCleanup`.)
- [x] 4.3 After deletion, move focus to the row above (index − 1) — handled by `DeleteEditorBlock`'s existing Q1 logic
      (row above, or new first row when the top was deleted, or empty-state with no focus)
- [x] 4.4 Idempotent with `OnRowFocusChanged`: removal reads live scratch + re-checks emptiness, so the focus-change
      commit and the deferred single rebuild don't double-handle a row→row move
- [x] 4.5 Q5: `EditorInsertTaskBelow` early-returns (no-op) when the focused row is itself an empty/whitespace task

## 5. Editor: never persist or display an empty task

- [x] 5.1 `OnClickSwitchToRead` calls `PurgeEmptyTasksFromScratch()` (removes ALL empty task blocks, not just the
      focused one) before flush/release; clears any `pendingEmptyRowRemoval`
- [x] 5.2 `OnGuiClosed` runs the same `PurgeEmptyTasksFromScratch()` + pending-clear before flush/release
- [x] 5.3 Autosave tick skips a flush while the focused row is a transiently-empty task (`FocusedRowIsEmptyTask`),
      so a mid-clear empty task is never serialized; other dirty edits still flush once content/focus moves (D5)
- [x] 5.4 Read view filters out empty task rows (`.Where(r => !r.IsTask || !IsNullOrWhiteSpace(r.Text))`) as the
      belt-and-suspenders display guard; toggle addresses by TaskId so filtering can't misalign a row

## 6. Verification

- [x] 6.1 `dotnet build` the mod project succeeds (0 warnings, 0 errors; Core suite still 103 green)
- [x] 6.2 In-game: "Add task" then click away — the empty row disappears and does not persist across reload
      — Confirmed (playtest 2026-07-25T23-02-59; empty-init also confirmed 2026-07-25T22-36-25)
- [x] 6.3 In-game: Cmd/Ctrl+A → Delete → blur on an existing task removes it and focuses the row above
      — Confirmed (playtest 2026-07-25T22-36-25, reported vs superseded `f34ea553`)
- [x] 6.4 In-game: deleting the first/only empty row behaves per Q1 (no error, correct empty-state/focus)
      — Confirmed both cases (playtest 2026-07-25T23-02-59)
- [x] 6.5 In-game: an empty freeform text section is NOT auto-removed — OBSOLETE per tester (freeform text
      deprioritized, "not on the page to test"); code guard remains (only task rows self-destruct). TESTING.md `6f9ef4c2`
- [x] 6.6 In-game: switching to read view / closing with an empty focused task does not save an empty task
      — Confirmed (playtest 2026-07-25T23-02-59)
- [x] 6.7 Update `TESTING.md` (via the what-to-test flow) with the new manual checks — added 7 items
      (9d85da89/8c411565/577159f1/3433d07d/6f9ef4c2/76b2a6ba/7bdddcd1) under add-empty-task-lifecycle,
      superseding the two pre-implementation placeholders
