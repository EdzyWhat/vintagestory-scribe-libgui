## Why

The Assignment Desk's dialog currently exposes six nav tabs: Create Assignments, Sent Assignment
History, Assignment Inbox, Read, Editor, Settings. The Read tab was added deliberately in
`add-assignment-desk-own-tasks` (per that change's design.md D1, at the user's own explicit
request at the time — "Read and Editor are both wanted"). A 2026-09-05 playtest note reverses that
decision: the Desk should drop Read entirely, leaving Create Assignments, Sent Assignment History,
Assignment Inbox, Editor ("Edit View"), and Settings.

## What Changes

- The Assignment Desk's nav column no longer shows a Read tab or button. The Desk's own document
  remains fully editable via the Editor tab — only the read-only viewing tab is removed, not the
  underlying document or its Editor access. No save-data or persistence format changes.
- The Desk's `viewMode` can no longer land on `ScribeLecternView.Read`; any code path that could
  put the dialog into Read (the nav button itself, and the default-view/last-active-view recall in
  `EnterGrantedView`) is updated so the dialog never lands there.
- No change to the Inbox block's own dialog (`GuiDialogScribeInbox`) or any other surface's Read
  tab — this is scoped to the Assignment Desk only.

## Capabilities

### Modified Capabilities
- `assignment-desk-own-document`: the "Assignment Desk exposes its own document via Read and
  Editor tabs" requirement changes to Editor-only — the Desk's document is still fully viewable and
  editable, just through one tab (Editor) instead of two (Read + Editor).

## Impact

- `src/Mod/GuiDialogScribeAssignmentDesk.cs`: `BuildRightColNav` drops the `readBtn` widget and its
  entry in the returned `Column`'s children; `EnterGrantedView`'s last-active-view recall no longer
  needs to treat Read as a legitimate landing tab for this dialog (Create Assignments stays the
  default open view, unchanged).
- `src/Mod/ScribeDialogBase.ViewSwitching.cs`: discovered during implementation — the Editor tab's
  "Done editing" footer button (and `EnterGrantedView`'s incidental editor-teardown path) otherwise
  force-lands `viewMode` on `Read` via the base `isEditorMode` setter, stranding the dialog with no
  active nav button once the Read tab is gone. Fixed by adding a `protected virtual void
  OnLeftEditorMode()` no-op hook at `LeaveEditorMode()`'s shared choke point — every other surface
  (Lectern/Notebook/Scriptorium/Chalkboard) is unaffected.
- `src/Mod/GuiDialogScribeAssignmentDesk.cs`: overrides `OnLeftEditorMode()` to call
  `DefaultToAssignmentView()` — the "Done editing" button stays visible (unlike the Tablet's
  `ShowEditorSwitchToRead => false` precedent, which hides it) and now redirects to Create Assignments
  instead of landing on Read.
- No `src/Core/` changes — this is a Mod-layer nav/UI change only.
