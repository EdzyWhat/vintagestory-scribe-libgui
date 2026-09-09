## Why

Completing an assigned task from the **Editor view's own checkbox** never transitions the
assignment to Completed: the Assignee's Inbox and the Assigner's Sent Assignment History both
keep showing it as Accepted forever, and closing/reopening either dialog does not help (there is
nothing new to fetch — the server-side record genuinely never changed). This is most visible on a
Tablet, whose dialog has only an Editor tab and no HUD/Pin Tab/Read view to fall back on, but the
same gap exists for a Notebook's Editor tab too.

Root cause: `NotifyAssignmentDoneChanged` (`src/Mod/ScribeModSystem.Assignment.cs:453`) is the
only path that transitions a `ScribeAssignmentStore` record to Completed and pushes the resulting
sync to both parties. It is called from the HUD overlay and Pin Tab (`CompleteTaskForPlayer` /
`CompleteUnpinnedTaskAtSource` in `src/Mod/ScribeModSystem.PinOperations.cs`) and from the Read
view's checkbox (`OnReadViewCompleteTask` in `src/Mod/ScribeDialogBase.Layout.cs`) — all three send
a `ScribeCompleteTaskMessage` that the server dispatches through it. The Editor view's checkbox
(`ToggleEditorTask` in `src/Mod/ScribeDialogBase.Editor.cs:286`) is deliberately different: because
the Editor holds the edit lock and autosaves the whole scratch document, it mutates `Done` locally
and relies on the ordinary whole-document flush to persist it — `BlockEntityScribeWritingStation
.ApplyEdit` for a Lectern, or `OnServerReceivedNotebookSave` (`src/Mod/ScribeModSystem.Network.cs`)
for a held Notebook/Tablet. Neither of those flush handlers has ever called into
`assignmentStore`/`NotifyAssignmentDoneChanged`, so an assignment completed purely through the
Editor never gets recorded as Completed anywhere the Inbox/Sent History tabs actually read from.

The `assignment-state-machine` spec's derived-completion requirement already states this
derivation "SHALL occur whenever the task's completed flag is set true by any completion path
(read view, editor view, pinned view, or HUD)" — added by `fix-assignment-completion-doc-resolution`
— so this is a conformance bug against an existing requirement, not new desired behavior. That
change's own manual-verification task (1.4) was left unchecked, which is likely why the Editor gap
was never caught: the HUD/Pin Tab fix was verified, but the Editor path was not separately tested.

## What Changes

- On a whole-document flush (Lectern `ApplyEdit`, and the Notebook/Tablet
  `OnServerReceivedNotebookSave` path), walk the newly-saved document's completed, assigned
  tasks and call `NotifyAssignmentDoneChanged(taskId, true, ...)` for each — exactly as the
  HUD/Pin/Read paths already do — keyed by the task's stable id, not by whether the document
  happens to carry a live-resolved `Assignment` object. See design.md for why this is done
  unconditionally per save rather than by diffing against a prior document snapshot.
- `NotifyAssignmentDoneChanged` already gates and acts on the canonical `ScribeAssignmentStore`
  record by `taskId` alone (per the prior doc-resolution fix), so no change is needed to that
  method itself — only to adding call sites on the two whole-document flush handlers.
- No change to the Editor's own local mutation path (`ScribeCompletion.ApplyLocal` /
  `ToggleEditorTask`) — the fix lives entirely on the server-side flush handlers that already see
  the full before/after document, so it covers every surface whose completion travels through a
  whole-document save (Editor tab on a Lectern, Notebook, or Tablet) in one place.

## Capabilities

### New Capabilities
None.

### Modified Capabilities
- `assignment-state-machine`: the existing "Completed is derived from the task's own completion
  flag" requirement gains a scenario covering completion via a whole-document flush (the Editor
  view's own checkbox, with no other tab to fall back on, as on a Tablet) — derivation must still
  occur.

## Impact

- **Affected code**: `src/Mod/BlockEntityScribeWritingStation.cs` (`ApplyEdit`),
  `src/Mod/ScribeModSystem.Network.cs` (`OnServerReceivedNotebookSave`), and wherever the
  before/after `Done`-diff helper is best shared between them (likely alongside
  `NotifyAssignmentDoneChanged` in `src/Mod/ScribeModSystem.Assignment.cs`).
- **Affected specs**: delta to `openspec/specs/assignment-state-machine/spec.md`.
- **No Core changes**: this is purely a Mod-layer gap — `src/Core/ScribeDocument`'s task model
  already carries stable ids and `Done` flags; nothing in Core needs to change.
- **Test impact**: an Atlas integration test covering an assignee completing an assigned task from
  a Tablet's Editor tab (only tab available) and confirming both the Assignee's Inbox and the
  Assigner's Sent Assignment History show Completed without any HUD/Pin/Read interaction.
