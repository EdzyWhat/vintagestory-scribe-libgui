## 1. Core: enum, insert index, Insert* methods

- [x] 1.1 Add `ScribeNewTaskInsert` (`Top = 0`, `Bottom = 1`) on `ScribePlayerSettings` with default Top, `NormalizeNewTaskInsert` (unknown → Top), and call it from `Normalized()`.
- [x] 1.2 Add `ScribeDocument.InsertIndex(ScribeNewTaskInsert)` → 0 or `Blocks.Count`. Add `InsertTextSection`, `InsertTracker`, `InsertCraft` (returns Guid), `InsertLink`, `InsertGuideLink` (out-of-range fails safely like `InsertTask`). Keep existing `Add*` as append wrappers (`Insert*(Count, …)`).
- [x] 1.3 Unit tests: Top then Bottom on a document with existing rows; two Top inserts stack newest-first; InsertCraft at 0 then `ReconcileCraftIngredients` keeps children as the depth-1 run under the parent; out-of-range insert returns false. No VS API in Core.
- [x] 1.4 `dotnet test tests/Core.Tests` green for this group.

## 2. Settings chrome

- [x] 2.1 Mod Behavior dropdown after Subtask Behavior: New Task Insert Top / Bottom, persist via `onMutate`. Lang keys + helptext (footer Add, Shift+right-click, Handbook Add to Scribe). Leave `pt-br.json` to English fallback.

## 3. Wire the three create paths

- [x] 3.1 Shared helper on `ScribeDialogBase` that reads Normalized player settings and returns `scratch.InsertIndex(...)`.
- [x] 3.2 Change `ScribeAddKind.Add` to take the insert index. Footer `OnClickAdd` uses the helper; `autoFocusRowOnRebuild` is that index (not `Count - 1`).
- [x] 3.3 Quick-add (`QuickAddTopTask`) inserts at the helper index and focuses that row. Cap / empty-document behavior unchanged.
- [x] 3.4 Handbook `ApplyHandbookAppend`, `ApplyGuideLinkAppend`, and `ApplyCraftHandbookAppend` insert at the helper index (Craft still reconciles children under the parent). Do not auto-focus Handbook adds.

## 4. Copy, changelog, validate

- [x] 4.1 `CHANGELOG.md` 1.3.2 note: New Task Insert (Top default); Add / quick-add / Handbook honor it. Enter-below unchanged.
- [x] 4.2 `openspec validate add-new-task-insert-position` passes. `dotnet test tests/Core.Tests` green.

## 5. In-game gates

- [x] 5.1 Default Top: footer Add Task lands at the top and is focused. Flip to Bottom: next Add appends.
- [x] 5.2 Shift+right-click follows the same setting (Top → first row; Bottom → last).
- [x] 5.3 Handbook Link and Craft with Top: new row (Craft group) at the top; Enter on a mid-list row still inserts below that row.
