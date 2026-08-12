## 1. Baseline

- [ ] 1.1 Confirm the Core model needs no change: `ScribeBlockKind.Task`/`.Text` and
  `ScribeDocument.AddTextSection` exist and the editor already renders a `Text` row without a
  checkbox (`ScribeEditorContent.cs` `data.IsTask` branches). Capture a green baseline:
  `dotnet build src/Mod/Mod.csproj` clean, `dotnet test tests/Core.Tests` green.
- [ ] 1.2 Note the interaction with the in-flight `reconcile-animating-surfaces` editor-path
  changes (both touch `OnRenderGUI` / `RebuildBody`); plan to land this on top of that work so
  the empty-row predicate widening edits the reconciled code, not a soon-to-change version.

## 2. Kind registry (extensible seam)

- [ ] 2.1 Add a kind descriptor in `src/Mod/` — identifier, display-label lang key, and an add
  delegate `Action` that mutates `scratch` (design D2). Keep it a plain data list, no interface
  hierarchy.
- [ ] 2.2 Register exactly two live kinds: `Task` → `scratch.AddTask("")` (existing path) and
  `Note` → `scratch.AddTextSection("")`. Do NOT stub Tracked/Linked as disabled entries — they
  are absent (spec `task-kind-picker`: "This release offers exactly Task and Note").
- [ ] 2.3 Add lang keys for the "Note" label and the kind-menu affordance (and, if chosen, a
  "New note…" placeholder — see design Open Questions).

## 3. Footer add control (the picker)

- [ ] 3.1 Replace the single "Add task" `Button` in `ScribeEditorContent` with the split add
  affordance (primary Add button + narrow kind-menu caret, design D1). Build the kind menu from
  the registry. Reuse LibGUI menu/overlay building blocks; use LibGUI controls only (no native
  chrome — `macos-native-button-hittest-quadrant-bug`).
- [ ] 3.2 Wire callbacks: primary click adds the current kind (defaults to Task, so one click
  still adds a task); picking a kind from the menu sets the primary kind and performs that add
  immediately (spec: "The default add is a task").
- [ ] 3.3 Route both adds through a kind-parameterized `OnClickAdd(kind)` in
  `ScribeDialogBase.Editor.cs` (generalize `OnClickAddTask`): dispatch to the registry's add
  delegate, then `SyncFocusNodesToScratch()` + `autoFocusRowOnRebuild = last` +
  `pendingEnsureVisible` + `RebuildBody()` (reuse the existing add path, no new rebuild trigger).
- [ ] 3.4 Task cap: keep `CanAddTaskUnderPolicy()` + `NotifyTabletFull()` on the Task add path;
  the Note add path bypasses the task cap (design D4 — notes are uncapped).

## 4. Empty-row lifecycle (task OR note)

- [ ] 4.1 Widen `PurgeEmptyTasksFromScratch()` (`Editor.cs:489`) to remove any blank/whitespace
  row of either kind; rename to `PurgeEmptyRowsFromScratch` (kind-neutral, design D3).
- [ ] 4.2 Widen the `pendingEmptyRowRemoval` guard in `OnRenderGUI` (`Lifecycle.cs:125`) to drop
  the `block.IsTask` condition, keeping the `!stillFocused` guard and `IsNullOrWhiteSpace` check.
- [ ] 4.3 Widen `FocusedRowIsEmptyTask()` (`Editor.cs:477`) to either kind; rename to
  `FocusedRowIsEmptyBlock`. Verify the autosave-skip still holds for a transient empty note.
- [ ] 4.4 Widen the `OnRowBlurred` scheduling site so leaving an empty note schedules its
  removal the same way an empty task does.
- [ ] 4.5 Update comments referencing "empty task" in these paths to "empty task or note".

## 5. Build & Core tests

- [ ] 5.1 `dotnet build src/Mod/Mod.csproj` clean (0 new warnings); `dotnet test
  tests/Core.Tests` green (Core is unchanged, so this is a no-regression gate).
- [ ] 5.2 If any add/normalize logic moved into a Core-testable seam, add coverage; otherwise
  note that the picker + empty-row lifecycle are GUI-layer (not Core-unit-testable) and rely on
  the in-game gate below.

## 6. In-game playtest gate

- [ ] 6.1 `bash build/restage.sh Debug`, relaunch, open a Lectern editor. Add a Note via the
  picker: confirm it appears with NO checkbox, focuses empty, and accepts typed text.
- [ ] 6.2 Confirm one-click still adds a Task (default kind), unchanged from before.
- [ ] 6.3 Add a Note, type nothing, click away → the empty note self-destructs (not left as a
  blank row). Repeat with switch-to-read and with close-dialog. Add a note with text, clear it,
  blur → row removed, focus moves to the row above.
- [ ] 6.4 Confirm a Note round-trips: add note with text, switch to read (renders as text, no
  checkbox), reopen after close → note persists with its text and kind.
- [ ] 6.5 Tablet: at the 10-task cap, confirm adding a Task is still refused + surfaced
  (`NotifyTabletFull`), but adding a Note succeeds (design D4). Verify on every editor surface
  that shares the footer (Lectern, Notebook, Clockmaker's Notebook, always-edit tablet) that the
  picker appears and works.
- [ ] 6.6 Record verdicts into `TESTING.md` via the `what-to-test` flow.

## 7. Merge gate

- [ ] 7.1 `dotnet build` clean; `dotnet test tests/Core.Tests` green.
- [ ] 7.2 `openspec validate add-note-kind-picker` passes.
- [ ] 7.3 The in-game gate (§6) is green on at least the Lectern; note any surface deferred.
