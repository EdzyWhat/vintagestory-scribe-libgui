## 1. Baseline

- [x] 1.1 Confirm the Core model needs no change: `ScribeBlockKind.Task`/`.Text` and
  `ScribeDocument.AddTextSection` exist and the editor already renders a `Text` row without a
  checkbox (`ScribeEditorContent.cs` `data.IsTask` branches). Capture a green baseline:
  `dotnet build src/Mod/Mod.csproj` clean, `dotnet test tests/Core.Tests` green.
- [ ] 1.2 ~~Note the interaction with the in-flight `reconcile-animating-surfaces` editor-path
  changes.~~ **RESOLVED 2026-08-12:** `reconcile-animating-surfaces` (and `animate-row-insertion`)
  are now MERGED + ARCHIVED on main, so the reconciled editor path (persistent `ScribeDialogBody` +
  `RebuildBody()`, the container-migrated editor) is already the code you edit. The empty-row
  predicate widening (§4) edits the settled version. Re-confirm line numbers before editing — they
  drifted since this change was drafted (`PurgeEmptyTasksFromScratch` is now `Editor.cs:456`,
  `FocusedRowIsEmptyTask` `:444`).

## 2. Kind registry (extensible seam)

- [x] 2.1 Add a kind descriptor in `src/Mod/` — identifier, display-label lang key, and an add
  delegate `Action` that mutates `scratch` (design D2). Keep it a plain data list, no interface
  hierarchy.
- [x] 2.2 Register exactly two live kinds: `Task` → `scratch.AddTask("")` (existing path) and
  `Note` → `scratch.AddTextSection("")`. Do NOT stub Tracked/Linked as disabled entries — they
  are absent (spec `task-kind-picker`: "This release offers exactly Task and Note").
- [x] 2.3 Add lang keys for the "Note" label and the kind-menu affordance (and, if chosen, a
  "New note…" placeholder — see design Open Questions).

## 3. Footer add control (the picker)

- [x] 3.1 Replace the single "Add task" `Button` in `ScribeEditorContent` with the segmented
  add-button group (design D1): a zero-gap `Row(spacing: 0)` of `[ primary Add Button (square
  corners) ][ 1px divider Container (theme Border color) ][ caret Button (square corners) ]`
  wrapped in an outer `Container` with `CornerRadius` 4 + rounded-rect clip, so it reads as one
  control with an interior divider line (no inter-button gap). LibGUI controls only (no native
  chrome — `macos-native-button-hittest-quadrant-bug`). Verify the clip masks the square child
  corners crisply in-game; fall back per D1 (square outer corners, or custom `Container` halves
  with per-corner `Vector4` radii) if soft.
- [x] 3.1a Build the kind list as a **floating drop-up** menu (design D1, corrected 2026-08-12),
  NOT an inline layout element: the scroll body keeps its exact height and the menu paints OVER it.
  Mirror LibGUI's `Dropdown` — a `LayerLink` ties the segmented group (`CompositedTransformTarget`)
  to a `CompositedTransformFollower(showAbove: true)` inserted into the `Overlay`, plus a
  full-screen barrier `OverlayEntry` that closes on outside tap. The caret toggles it open/closed;
  the menu grows in via a scale+fade anchored at `BottomCenter` (drop-up twin of `DropdownMenu`).
  Implemented as the self-contained `ScribeAddKindPicker` widget (owns selected-kind + open state +
  overlay entries; `Dispose` tears them down).
- [x] 3.2 Wire callbacks: primary click adds the current kind (defaults to Task, so one click
  still adds a task); picking a kind from the drop-up sets the primary kind, performs that add
  immediately, and closes the menu (spec: "The default add is a task"). Activating the primary
  button also closes an open menu.
- [x] 3.3 Route both adds through a kind-parameterized `OnClickAdd(kind)` in
  `ScribeDialogBase.Editor.cs` (generalize `OnClickAddTask`): dispatch to the registry's add
  delegate, then `SyncFocusNodesToScratch()` + `autoFocusRowOnRebuild = last` +
  `pendingEnsureVisible` + `RebuildBody()` (reuse the existing add path, no new rebuild trigger).
- [x] 3.4 Task cap: keep `CanAddTaskUnderPolicy()` + `NotifyTabletFull()` on the Task add path;
  the Note add path bypasses the task cap (design D4 — notes are uncapped).

## 4. Empty-row lifecycle (task OR note)

- [x] 4.1 Widen `PurgeEmptyTasksFromScratch()` (`Editor.cs:489`) to remove any blank/whitespace
  row of either kind; rename to `PurgeEmptyRowsFromScratch` (kind-neutral, design D3).
- [x] 4.2 Widen the `pendingEmptyRowRemoval` guard in `OnRenderGUI` (`Lifecycle.cs:125`) to drop
  the `block.IsTask` condition, keeping the `!stillFocused` guard and `IsNullOrWhiteSpace` check.
- [x] 4.3 Widen `FocusedRowIsEmptyTask()` (`Editor.cs:477`) to either kind; rename to
  `FocusedRowIsEmptyBlock`. Verify the autosave-skip still holds for a transient empty note.
- [x] 4.4 Widen the `OnRowBlurred` scheduling site so leaving an empty note schedules its
  removal the same way an empty task does.
- [x] 4.5 Update comments referencing "empty task" in these paths to "empty task or note".

## 5. Build & Core tests

- [x] 5.1 `dotnet build src/Mod/Mod.csproj` clean (0 new warnings); `dotnet test
  tests/Core.Tests` green (Core is unchanged, so this is a no-regression gate).
- [x] 5.2 If any add/normalize logic moved into a Core-testable seam, add coverage; otherwise
  note that the picker + empty-row lifecycle are GUI-layer (not Core-unit-testable) and rely on
  the in-game gate below.

## 6. In-game playtest gate

- [x] 6.1 `bash build/restage.sh Debug`, relaunch, open a Lectern editor. Add a Note via the
  picker: confirm it appears with NO checkbox, focuses empty, and accepts typed text.
- [x] 6.2 Confirm one-click still adds a Task (default kind), unchanged from before.
- [x] 6.3 Add a Note, type nothing, click away → the empty note self-destructs (not left as a
  blank row). Repeat with switch-to-read and with close-dialog. Add a note with text, clear it,
  blur → row removed, focus moves to the row above.
- [x] 6.4 Confirm a Note round-trips: add note with text, switch to read (renders as text, no
  checkbox), reopen after close → note persists with its text and kind.
- [x] 6.5 Tablet: at the 10-task cap, confirm adding a Task is still refused + surfaced
  (`NotifyTabletFull`), but adding a Note succeeds (design D4). Verify on every editor surface
  that shares the footer (Lectern, Notebook, Clockmaker's Notebook, always-edit tablet) that the
  picker appears and works.
- [x] 6.6 Record verdicts into `TESTING.md` via the `what-to-test` flow.

## 7. Merge gate

- [x] 7.1 `dotnet build` clean; `dotnet test tests/Core.Tests` green.
- [x] 7.2 `openspec validate add-note-kind-picker` passes.
- [x] 7.3 The in-game gate (§6) is green on at least the Lectern; note any surface deferred.

## 8. Per-kind character limits + player feedback (delta 2026-08-13)

- [x] 8.1 Give the note editor field a live `maxLength` of `ScribeDocumentCodec.MaxTextLength`
  (10,000), mirroring the task field's existing `MaxTaskTextLength` cap — in
  `ScribeEditorContent`, replace the `maxLength: Widget.Data.IsTask ? MaxTaskTextLength :
  (int?)null` with a per-kind value so notes clamp instead of typing unbounded.
- [x] 8.2 Change the codec's freeform-Text backstop from reject-whole-document to CLIP: in
  `ScribeDocumentCodec` deserialize, an over-`MaxTextLength` Text block clips to `MaxTextLength`
  (matching the Task `MaxTaskTextLength` clip) rather than `return false`. Removes the latent
  whole-document data-loss path a user-creatable note would open.
- [x] 8.3 Add a "cap reached" signal to `ScribeMultilineField`: an `Action? onMaxLengthReached`
  invoked from `Insert` whenever the maxlength clamp drops characters (a truncated paste or a
  no-op keystroke at the cap). Thread it up through the editor row to `ScribeEditorContent` and
  on to a dialog handler `OnRowMaxLengthReached(int index)`.
- [x] 8.4 In the dialog handler, fire the standard in-game error via `capi.TriggerIngameError`
  with a per-kind message: `scribe:task-limit` for a Task row, `scribe:note-limit` for a Note
  row. Pass the limit constant as the format arg so the count is never hardcoded in the lang
  string.
- [x] 8.5 Add the two parameterized lang keys to `en.json`: `"task-limit": "Tasks are limited
  to {0} characters."` and `"note-limit": "Notes are limited to {0} characters."`.
- [x] 8.6 `dotnet build src/Mod/Mod.csproj` clean; `dotnet test tests/Core.Tests` green (the
  codec clip change is Core-testable — add/extend a codec round-trip test for an over-limit
  note clipping instead of dropping the document).
- [x] 8.7 In-game gate: paste/type past a task's 1,000 and a note's 10,000 limit → input stops
  at the cap and the matching "limited to N characters" error appears. Confirm an over-limit
  note loads clipped (not a blanked document) after a reload. Record verdicts into `TESTING.md`.
