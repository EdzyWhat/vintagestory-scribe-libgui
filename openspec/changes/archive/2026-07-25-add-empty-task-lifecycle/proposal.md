## Why

New tasks are born with the literal text "New task" (the `scribe-gui-newtask-placeholder`
lang string), so every add forces the player to select-all and delete that boilerplate before
they can type their own. And because a task always carries non-blank text, there is no cheap
way to get rid of a row from the keyboard, nor any pushback against a player who taps "Add
task" repeatedly and floods the block with half-finished rows.

This change makes a new task start **empty** and makes an **empty task row self-destruct when
it loses focus**, moving focus to the row above. Together those two behaviors remove the
delete-the-boilerplate friction, stop abandoned adds from growing the list, and turn "Cmd/Ctrl+A,
Delete, blur" into a fast keyboard-only way to remove a row — no mouse required.

## What Changes

- New tasks (the "Add task" button and Enter=insert-below) are created with **empty text**
  instead of the "New task" placeholder, so the field is ready to type into immediately.
- **A task row whose text is empty/whitespace-only is deleted when it loses focus**, and focus
  moves to the row immediately above it (the previous row). This is content-based, not
  creation-based: it applies to any empty task row (a just-added one abandoned without typing,
  or an existing task the player cleared with Cmd/Ctrl+A + Delete), not only to freshly-created
  rows. It does NOT apply to freeform text sections (empty notes remain valid).
- **BREAKING (model contract):** the Core document model **stops rejecting blank/whitespace-only
  task text**. Today `AddTask`/`InsertTask`/`SetBlockText` reject blank task text and leave the
  document unchanged; a task must therefore always be created with non-blank text, and clearing a
  task's text in the editor silently reverts it (the field's write-through `SetBlockText` is
  rejected, so the scratch keeps the old text). To let an empty task exist transiently while it is
  being edited, the model must permit empty task text. Responsibility for not *persisting* an empty
  task moves entirely to the editing layer (blur/commit/close cleanup) — consistent with the spec's
  existing stance that whitespace normalization is the editing layer's job, not the model's.
- The editor's commit/close/switch/autosave paths are updated so an empty task is removed rather
  than saved, and so a lingering empty task cannot be persisted or shown in the read view.

## Capabilities

### New Capabilities
<!-- None: this extends existing editor and document behavior rather than introducing a new capability. -->

### Modified Capabilities
- `task-note-document`: relax the task-text content invariant — the model no longer rejects
  blank/whitespace-only task text on add or text-change; empty task text is stored verbatim like
  any other value, and cleanup of empty tasks is the editing layer's responsibility.
- `lectern-gui-shell`: new tasks are created empty (no "New task" seed); an empty task row is
  removed when it loses focus, with focus moving to the row above; the Enter/Tab/Shift+Tab
  commit-and-navigate behavior is reconciled with empty-row removal.

## Impact

- **`src/Core/ScribeDocument.cs`** — remove the `IsNullOrWhiteSpace` rejection from `AddTask`,
  `InsertTask`, and the task branch of `SetBlockText`. (`Core` stays game-API-free.)
- **`tests/Core.Tests/ScribeDocumentTests.cs`** — update/replace the "blank task text is
  rejected" tests to assert empty task text is now accepted and stored verbatim.
- **`src/Mod/GuiDialogScribeLecternLibGui.cs`** — `OnClickAddTask` and `EditorInsertTaskBelow`
  seed empty text instead of the lang placeholder; add empty-task-removal + focus-to-above logic
  on blur/commit; ensure `OnClickSwitchToRead`, `OnGuiClosed`, and the autosave/flush path never
  persist an empty task.
- **`src/Mod/ScribeMultilineField.cs`** — the existing (currently unwired) `OnBlur` callback is
  wired through `ScribeEditRow` so the dialog is notified of a genuine blur to run the
  empty-row cleanup.
- **`assets/scribe/lang/en.json`** — the `scribe-gui-newtask-placeholder` string is no longer used
  as seed text (may be removed or repurposed as ghost placeholder text — see design.md).
- No network, persistence-format, or dependency changes; the existing lock-gated
  `ScribeEditDocumentMessage` edit path is reused unchanged.
