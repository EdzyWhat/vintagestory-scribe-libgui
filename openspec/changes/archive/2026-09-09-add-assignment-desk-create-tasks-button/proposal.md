## Why

The Create Assignments empty state tells players to create tasks elsewhere before staging them,
even though the Assignment Desk already has its own editable document. A direct action will let a
player begin authoring Desk-local tasks from the place where they intend to assign them.

## What Changes

- Always show a “Create Tasks to Assign” button in the Create Assignments tab's empty task-list
  state, before the conditional “Pull existing tasks from this Desk” button.
- Make the new button enter the Assignment Desk's existing Editor view through the same
  server-authoritative editor-lock path as its Editor navigation button.
- Keep the existing pull button conditional on the Desk document having eligible rows; after the
  player creates and commits tasks, returning to Create Assignments exposes that pull action.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `assignment-desk-own-document`: adds a direct empty-state route from Create Assignments to the
  Desk's existing Editor view while preserving editor-lock behavior and the pull-from-Desk flow.

## Impact

- `src/Mod/ScribeAssignmentStageRow.cs`: render and route the new always-present empty-state button.
- `src/Mod/ScribeAssignmentFormContent.cs` and `src/Mod/GuiDialogScribeAssignmentDesk.cs`: pass the
  Editor-entry callback through the existing form and staged-list widget seams.
- `src/Mod/assets/scribe/lang/en.json`: add the “Create Tasks to Assign” label.
- No persistence, network protocol, Core model, or dependency changes.
