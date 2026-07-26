## Context

Today a new task row is seeded with the localized literal "New task"
(`scribe-gui-newtask-placeholder`) at both creation sites in the LibGUI dialog:
`OnClickAddTask` (the footer "Add task" button) and `EditorInsertTaskBelow` (Enter=insert-below).
The player must clear that boilerplate before typing.

Two invariants shape the current behavior and constrain this change:

1. **The Core model rejects blank task text.** `ScribeDocument.AddTask`, `InsertTask`, and the
   task branch of `SetBlockText` all `return false` on `string.IsNullOrWhiteSpace(text)`,
   leaving the document unchanged. This is codified in the `task-note-document` spec
   ("Document stores task text verbatim" → "Blank or whitespace-only task text is rejected").
   Because the editor field writes through to `SetBlockText` on *every keystroke*
   (`NotifyTextChanged`), a task the player clears to empty is silently rejected — the scratch
   keeps the last non-empty text. So today an empty task literally cannot exist in the model.

2. **Blur-commit already has a seam, but it is unwired.** `ScribeMultilineField` already exposes
   an `OnBlur` callback ("Focus lost without an Enter/Shift+Tab … commit the row's edit"), but
   `ScribeEditRow` does not pass it through, so it never fires. Cross-row commit today happens
   only via the dialog's per-row `FocusNode` listeners (`OnRowFocusChanged`), which fire when a
   *different* row gains focus. A blur to *nothing* (click empty space, close) is handled by the
   autosave tick and the close/switch commit paths, not by a per-row blur signal.

The two requested behaviors are coupled: empty-init is only safe if an abandoned empty row is
cleaned up, and self-destruct-on-blur only makes sense if empty rows are allowed to exist
transiently. Both therefore land together.

## Goals / Non-Goals

**Goals:**
- New tasks start empty so the player types immediately with no boilerplate to clear.
- An empty (whitespace-only) *task* row is removed when it genuinely loses focus, and focus
  moves to the task/row directly above it.
- Abandoned empty adds never grow the list or reach the read view / persistence.
- "Cmd/Ctrl+A → Delete → blur" becomes a keyboard-only row deletion.
- Keep `src/Core/` free of any Vintage Story API reference (relaxing a validation rule is a pure
  model change).

**Non-Goals:**
- No change to freeform **text sections** — an empty note is still valid and is never
  auto-deleted (only *task* rows self-destruct).
- No new network message, persistence-format change, or dependency.
- No change to the read view's own behavior beyond never being handed an empty task.
- Not building a confirmation/undo affordance for auto-deletion (the row was empty; there is
  nothing to lose).

## Decisions

### D1: Relax the Core blank-task-text invariant (model permits empty task text)
`AddTask`, `InsertTask`, and `SetBlockText`'s task branch stop rejecting
`IsNullOrWhiteSpace`. Empty task text is stored verbatim like any other value.

- **Why:** the field writes through on each keystroke; without this, clearing a task to empty is
  rejected and the scratch silently retains stale text, making both empty-init and
  clear-to-delete impossible. Pushing the "don't persist empty" rule up to the editing layer is
  consistent with the spec's existing division of labor (the model stores verbatim; the editing
  layer normalizes).
- **Alternative considered — keep the model strict, add a separate "empty allowed while editing"
  flag or a parallel mutator:** rejected as more surface area for no benefit; the model's only
  job is storage, and the transient-empty state is inherently an editing concern.
- **Consequence:** the `task-note-document` spec's "Blank or whitespace-only task text is
  rejected" scenario is removed/inverted, and the two Core unit tests asserting rejection are
  updated to assert acceptance.

### D2: Seed new tasks with empty string at both creation sites
`OnClickAddTask` and `EditorInsertTaskBelow` pass `""` to `AddTask`/`InsertTask` instead of
`Lang.Get("scribe:scribe-gui-newtask-placeholder")`. The new row is auto-focused as it is today,
so the caret is already in an empty field.

### D3: Empty-task self-destruct fires on genuine blur, driven by the field's `OnBlur`
Wire `ScribeMultilineField.OnBlur` through `ScribeEditRow` to a new dialog handler
(`OnRowBlurred(index)`). On blur, if the block at that index is a **task** whose (trimmed) text
is empty, delete it (reusing the existing `DeleteEditorBlock` path: mutate scratch → mark dirty →
resync focus nodes → rebuild) and move focus to the row above.

- **Why blur, not the autosave tick:** deletion should be a deterministic response to leaving the
  row, not a timer side effect; the field already has the precise has→hasn't-focus transition.
- **Interaction with the existing `OnRowFocusChanged` commit path:** both can fire when moving
  row→row. Order and idempotency matter — see Risks. The handler must be safe to run alongside the
  normalize+flush that `OnRowFocusChanged` already does for the row being left.

### D4: "Focus moves to the row above" = the previous row (index − 1)
After deleting the empty row at index `i`, focus goes to index `i − 1` (the row that was directly
above). This matches the user's stated intent and makes repeated Cmd/Ctrl+A→Delete→blur walk *up*
the list. See Open Questions for the top-of-list and only-row cases.

### D5: Never persist or display an empty task (defense at every commit/exit)
`OnClickSwitchToRead`, `OnGuiClosed`, and `FlushIfDirty`/the autosave tick must not persist an
empty task. Preferred approach: run the same empty-task cleanup on the currently-focused row at
each of these commit points before flushing, so a switch-to-read or dialog-close of an abandoned
empty row removes it rather than saving it. The read view is a pure projection of the document, so
if the document never contains an empty task, the read view never shows one. (Belt-and-suspenders
alternative: have the read-view builder skip empty task rows — kept as a fallback, not the primary
mechanism, since suppressing at display leaves the empty task in the persisted document.)

### D6: Fate of the `scribe-gui-newtask-placeholder` lang string
Two options; recommend deciding during implementation:
- **(a) Remove it** — it has no remaining use as seed text.
- **(b) Repurpose it as ghost/placeholder text** rendered inside an empty focused field ("New
  task…") to hint what the row is for. This requires `ScribeMultilineField` to render placeholder
  text when empty (new capability in the field), which is a small but real addition.
Recommendation: (a) for the minimal change; flag (b) as a nice-to-have follow-up so this change
stays tightly scoped. (See Open Question Q4.)

## Risks / Trade-offs

- **Double-handling on row→row focus move** (`OnRowFocusChanged` commit + new `OnBlur` delete both
  fire) → Make `OnRowBlurred` idempotent and index-safe: re-read the block from scratch by index,
  bail if out of range or not a task, and after deletion let the single subsequent `ForceRebuild`
  settle focus. Do the delete decision from the authoritative scratch state, not from a captured
  snapshot, so a stale index can't delete the wrong row.
- **Deleting a row mid-focus-transition could strand focus** → Reuse `DeleteEditorBlock`'s existing
  `focusedEditIndex` fix-up logic (it already clears/shifts the focused index across a deletion),
  then explicitly focus index − 1. Guard all focus moves against an empty document.
- **Losing an in-progress edit if "empty" is mis-detected** → Only treat a row as empty when its
  text is `IsNullOrWhiteSpace` after the same `TrimEnd` normalization used at commit; a row with
  any non-whitespace character is never auto-deleted.
- **Relaxing the model invariant could let an empty task slip into persistence via a non-editor
  path** → There is only one edit path (the lock-gated `ScribeEditDocumentMessage`), and D5 cleans
  up at every editor exit; the Core tests are updated to document the new contract so the relaxed
  rule is intentional, not an accident.
- **Multiplayer / autosave timing** → The autosave tick could serialize a transiently-empty task
  a player is mid-clearing. Acceptable: it round-trips back as an empty task (now legal), and the
  next blur/commit cleans it up. If undesirable, autosave can skip a focused empty task (minor).

## Migration Plan

No data migration: the persistence format is unchanged, and empty task text is representable in
the existing codec (it stores text verbatim). Existing documents are unaffected — no document has
an empty task today (the old invariant forbade it), so relaxing the rule cannot change how any
saved document deserializes. Rollback is a pure code revert; a document saved under this change
will not contain an empty task (D5 prevents persisting one), so reverting is safe.

## Open Questions

- **Q1 (top-of-list / only-row):** When the empty task is the **first** row (nothing above), where
  does focus go on auto-delete? Options: (a) focus the *new* first row (old index 1, now 0) if one
  exists; (b) if it was the only row, delete it and show the editor empty-state hint with no focus.
  Recommended: (a) then (b). Confirm this is the desired top-of-list behavior.
- **Q2 (empty task above an empty task):** Cmd/Ctrl+A→Delete→blur repeatedly walks *up* the list,
  deleting each empty row and landing on the one above — which may itself be empty and get deleted
  on the next blur. Is this cascade the intended fast-clear, or should focus stop after one delete?
  (Recommended: allow the cascade; it is exactly the "quickly delete lists" behavior requested.)
- **Q3 (existing empty task mid-list vs only newly-created):** The proposal applies self-destruct
  to *any* empty task row on blur, not just freshly-created ones. Confirm that clearing an existing
  mid-list task and blurring should delete it (recommended — it is the keyboard-delete gesture the
  user asked for), rather than restricting auto-delete to rows created this session.
- **Q4 (placeholder lang string):** Remove `scribe-gui-newtask-placeholder` entirely (D6a), or keep
  it as ghost placeholder text in the empty field (D6b, requires field placeholder rendering)?
- **Q5 (Enter on an empty row):** Pressing Enter in an empty task today commits and inserts a new
  task below (leaving the empty one). With empty-init, Enter on a still-empty row would create a
  *second* empty row above/below the first. Should Enter on an empty task be a no-op (or delete the
  empty row) instead of stacking empties? (Recommended: Enter on an empty task does nothing rather
  than spawning another empty row.)
- **Q6 (sequencing):** This change edits the same editor file and rows touched by the in-flight
  `add-lectern-row-affordances-libgui` and pin changes. It is behaviorally independent of the pin
  work, but shares code surface — confirm whether it should land after those merge to avoid churn,
  or proceed in parallel.
