## 1. Resolve open questions

- [ ] 1.1 Confirm top-of-list / only-row focus behavior on auto-delete (design Q1)
- [ ] 1.2 Confirm the walk-up cascade of consecutive empty rows is desired (design Q2)
- [ ] 1.3 Confirm auto-delete applies to any empty task, not only freshly-created ones (design Q3)
- [ ] 1.4 Decide the fate of `scribe-gui-newtask-placeholder`: remove vs. ghost placeholder (design Q4/D6)
- [ ] 1.5 Confirm Enter on an already-empty row is a no-op rather than stacking an empty task (design Q5)
- [ ] 1.6 Confirm sequencing relative to the in-flight row-affordances/pin changes (design Q6)

## 2. Core model: allow empty task text

- [ ] 2.1 Remove the `IsNullOrWhiteSpace` rejection from `ScribeDocument.AddTask`
- [ ] 2.2 Remove the `IsNullOrWhiteSpace` rejection from `ScribeDocument.InsertTask`
- [ ] 2.3 Remove the blank-rejection from the task branch of `ScribeDocument.SetBlockText` (store verbatim)
- [ ] 2.4 Update XML doc-comments on those methods to reflect that empty task text is now accepted
- [ ] 2.5 Update `tests/Core.Tests/ScribeDocumentTests.cs`: replace the "blank task text is rejected"
      cases with assertions that empty/whitespace-only task text is accepted and stored verbatim
- [ ] 2.6 Run `dotnet test tests/Core.Tests` and confirm green

## 3. Editor: create new tasks empty

- [ ] 3.1 `OnClickAddTask` seeds `""` instead of `Lang.Get("scribe:scribe-gui-newtask-placeholder")`
- [ ] 3.2 `EditorInsertTaskBelow` seeds `""` instead of the placeholder lang string
- [ ] 3.3 Verify the new empty row still auto-focuses (unchanged `autoFocusRowOnRebuild` path)
- [ ] 3.4 Apply the Q4 decision to the lang string (remove or repurpose as placeholder)

## 4. Editor: empty-task self-destruct on blur

- [ ] 4.1 Wire `ScribeMultilineField.OnBlur` through `ScribeEditRow` to a new dialog `OnRowBlurred(index)`
- [ ] 4.2 Implement `OnRowBlurred`: if the block at index is a task and its trimmed text is empty, delete it
      (reuse `DeleteEditorBlock`'s scratch-mutate + focus-fixup + rebuild path); read state from scratch
      by index and no-op safely on a stale/out-of-range index or a text section
- [ ] 4.3 After deletion, move focus to the row above (index − 1), honoring the Q1 top-of-list/only-row rule
- [ ] 4.4 Ensure idempotency with the existing `OnRowFocusChanged` commit so a row→row move doesn't
      double-handle (delete once, let the single rebuild settle focus)
- [ ] 4.5 Apply the Q5 decision to `EditorInsertTaskBelow` (Enter on an empty row does not stack an empty task)

## 5. Editor: never persist or display an empty task

- [ ] 5.1 Run empty-task cleanup on the focused row in `OnClickSwitchToRead` before flush/release
- [ ] 5.2 Run empty-task cleanup on the focused row in `OnGuiClosed` before flush/release
- [ ] 5.3 Ensure the autosave tick / `FlushIfDirty` cannot persist a lingering empty task (per D5 decision)
- [ ] 5.4 Confirm the read view never renders an empty task once the document is clean (projection of doc)

## 6. Verification

- [ ] 6.1 `dotnet build` the mod project succeeds
- [ ] 6.2 In-game: "Add task" then click away — the empty row disappears and does not persist across reload
- [ ] 6.3 In-game: Cmd/Ctrl+A → Delete → blur on an existing task removes it and focuses the row above
- [ ] 6.4 In-game: deleting the first/only empty row behaves per Q1 (no error, correct empty-state/focus)
- [ ] 6.5 In-game: an empty freeform text section is NOT auto-removed
- [ ] 6.6 In-game: switching to read view / closing with an empty focused task does not save an empty task
- [ ] 6.7 Update `TESTING.md` (via the what-to-test flow) with the new manual checks
