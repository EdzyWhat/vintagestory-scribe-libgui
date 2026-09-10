## Context

See proposal.md for motivation. `ScribeAssignmentStageContent` owns the Create Assignments task
list and its empty state. It currently receives only the conditional pull-from-Desk callback
through `ScribeAssignmentFormContent`. `GuiDialogScribeAssignmentDesk` already exposes its local
document through the inherited Editor and wires its Editor nav button to the protected
`TryEnterEditor` entry point, which handles the shared-block lock check and server request.

## Goals / Non-Goals

**Goals:**

- Keep the empty-state action available regardless of whether the Desk already contains tasks.
- Route editor entry through the established lock-aware path.
- Preserve the existing staged-item and pull-from-Desk source-selection behavior.

**Non-Goals:**

- Adding inline task authoring to Create Assignments.
- Automatically pulling a newly authored task into the assignment list.
- Changing editor locking, persistence, assignment selection, or send behavior.

## Decisions

**D1: Pass an Editor-entry callback through the existing widget chain.**
`GuiDialogScribeAssignmentDesk` will pass `TryEnterEditor` into `ScribeAssignmentFormContent`, which
will forward it to `ScribeAssignmentStageContent`. The empty-state widget remains presentation-only
and does not learn about dialog view state or networking. Calling a dialog method from the widget
through a callback matches the existing `OnPullFromDesk` seam.

Alternative considered: have the empty-state widget mutate the active view directly. Rejected
because it would bypass the dialog's server-authoritative lock request and couple a reusable widget
to dialog internals.

**D2: Reuse `TryEnterEditor` exactly.**
The new button and the Editor nav button share the same lock check, in-game refusal message, server
request, and access-granted transition. The new route receives no special case for an occupied
editor.

Alternative considered: disable or hide the create button while another player holds the lock.
Rejected because the requirement says the action is always present, and `TryEnterEditor` already
provides consistent unavailable-state feedback.

**D3: Render the create action before the conditional pull action.**
The empty state's centered `Column` will contain the hint, the always-present create button, then
the pull button when `CanPullFromDesk` is true. The existing spacing and button treatment remain in
use. Returning from Editor recomputes `CanPullFromDesk`; the player chooses Pull explicitly after
committing a task.

## Risks / Trade-offs

- **[Risk]** Two buttons make the empty state taller. **Mitigation:** retain the existing compact
  centered column and verify both controls fit at the minimum supported dialog size.
- **[Risk]** A player may expect returning from Editor to stage new tasks automatically.
  **Mitigation:** keep the existing explicit pull action visible immediately after tasks exist.

## Migration Plan

No stored data or protocol changes are involved. Deployment is a normal mod update; rollback is a
code revert.
