## Context

Document-level creates are split across three Mod entry points, each hardcoded:

| Gesture | Today |
|---|---|
| Footer Add (`OnClickAdd` → `ScribeAddKind.Add` → `AddTask` / `AddTextSection`) | append |
| Shift+right-click (`QuickAddTopTask` → `InsertTask(0)`) | index 0 |
| Handbook (`ApplyHandbookAppend` / `ApplyCraftHandbookAppend` / `ApplyGuideLinkAppend`) | append |

`ScribeDocument.Add*` always `_blocks.Add`. `InsertTask(index)` already exists for Enter-below and quick-add. Craft children are not appended to the document end: `ReconcileCraftIngredients` inserts at `OwnedRun` `runEnd`, so a parent placed at index 0 gets its shopping list at 1, 2, … and the rest of the document shifts down as a block.

The setting is client-local (same as Subtask Behavior). Inserts happen on editor scratch, then the existing flush/save path. No new packet.

## Goals / Non-Goals

**Goals:**
- One `ScribeNewTaskInsert` policy (Top default, Bottom) honored by footer Add, quick-add, and Handbook creates.
- Craft parent + owned run land as one group at the chosen edge.
- Footer Add and quick-add still focus the new empty row (at the new index). Handbook stays unfocused.

**Non-Goals:**
- Enter insert-below (`EditorInsertTaskBelow`).
- Transcribe copy/import, pin HUD order, grip nest.
- Server-persisted per-document insert policy.

## Decisions

### D1: Enum on `ScribePlayerSettings`, not a boolean

`ScribeNewTaskInsert { Top = 0, Bottom = 1 }`, default **Top**. `NormalizeNewTaskInsert` maps unknown → Top. JSON serializer default; missing key → Top.

**Alternative considered:** bool `InsertNewTasksAtTop` — rejected; a two-value dropdown matches Subtask Behavior / completion policy.

### D2: Core insert-at-index; keep `Add*` as append wrappers

Add `ScribeDocument.InsertIndex(ScribeNewTaskInsert pos)` → `0` or `Blocks.Count`.

Add index-taking inserts (fail safely out of range, same as `InsertTask`):

- `InsertTextSection(index, text)`
- `InsertTracker(index, itemCode, targetQuantity)`
- `InsertCraft(...)` → `Guid` (parent at `index`; caller still reconciles)
- `InsertLink` / `InsertGuideLink`

Keep existing `Add*` as `Insert*(Blocks.Count, …)` so current tests that append stay valid.

**Alternative considered:** change every `Add*` to take the enum — rejected; Core stays position-agnostic, Mod passes an index.

### D3: One Mod helper; `ScribeAddKind.Add` takes the index

`ScribeDialogBase.NewTaskInsertIndex()` reads player settings (Normalized) and returns `scratch.InsertIndex(pos)`.

Change `ScribeAddKind.Add` to `Func<ScribeDocument, string?, int, bool>` (index last). Footer and Handbook item-kinds go through that. Guide-page and Craft handbook paths call `InsertGuideLink` / `InsertCraft` with the same index, then Craft reconcile (unchanged).

Rename `QuickAddTopTask` to `QuickAddNewTask` (or keep the name and stop hardcoding 0). Same cap / empty-row / focus machinery; `autoFocusRowOnRebuild` = the insert index, not always `Count - 1` or `0`.

### D4: Newest-at-edge stacking

Repeated Top inserts each go to index 0, so the newest row is always first (stack). Bottom stays chronological append. Accepted.

### D5: Enter-below stays relative

`EditorInsertTaskBelow` keeps `index + 1`. The setting is for "where does a new document-level row land," not caret-relative typing.

### D6: Settings chrome

Mod Behavior dropdown after Subtask Behavior. Labels: `Top` / `Bottom`. Helptext: footer Add, Shift+right-click, and Handbook Add to Scribe. Window Text Size does not preview this (not a paint setting).

## Risks / Trade-offs

- **Default Top surprises players who liked append** → changelog + helptext; one dropdown click restores Bottom.
- **Handbook + Top while scrolled to the bottom** → new row is off-screen until they scroll up; same as today's quick-add. Footer Add still `pendingEnsureVisible`.
- **Craft at Top in a long list** → parent+children occupy the top N rows; Bound complete still treats that run. No extra case.
- **Tablet cap** → insert still gated by `CanAddTaskUnderPolicy` before index is used.

## Migration Plan

No codec change. Settings JSON: new key, default Top. Rollback: revert the cut; old clients ignore the unknown key.

## Open Questions

None — product forks from the request are closed (Top default; three gestures; Enter-below out).
