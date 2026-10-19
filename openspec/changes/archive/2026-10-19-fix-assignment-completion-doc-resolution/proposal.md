## Why

Completing an accepted, pinned assignment task (via the HUD or Pin Tab checkbox) sometimes never
transitions the assignment to Completed: the Assignee's own Inbox and the Assigner's Sent
Assignment History both keep showing it as Accepted forever, with no error and nothing in the
logs. Root cause (confirmed by reading `CompleteTaskForPlayer`/`NotifyAssignmentDoneChanged` in
`src/Mod/ScribeModSystem.PinOperations.cs` and `src/Mod/ScribeModSystem.Assignment.cs`): the
completion-derivation call is only reached when the task's owning Notebook resolves via
`TryResolveDocHost`, which — for a Notebook (unlike a Lectern, which self-registers) — falls back
to scanning only the *completing player's own inventory*. If that Notebook is elsewhere (a chest,
another player, the ground) at the exact moment the player checks the task off from the HUD/Pin
Tab, resolution silently fails and the derivation never runs, with zero trace output anywhere on
that path.

## What Changes

- `NotifyAssignmentDoneChanged` stops depending on a resolved document/block at all: it already
  re-fetches the canonical `ScribeAssignmentStore` record by `taskId` alone to do its real work
  (mark Completed, stamp the date, push sync to both parties) — the `assignmentOnBlock` parameter
  it currently requires is only ever used as a pre-filter gate and to read fields the canonical
  record already carries. It is re-scoped to gate and act on the canonical store record directly.
- `CompleteTaskForPlayer` calls this derivation unconditionally on every Done→true toggle of a
  pinned task, not only inside its `if (resolved)` branch — so the Assignee's Inbox and the
  Assigner's Sent Assignment History update correctly even when the task's Notebook can't be
  found at that moment. The document-level write-through (mutating the live block's own embedded
  `Assignment` object) still only happens when the document *is* resolvable — there is no live
  block to mutate otherwise; the block's own "accepted assignment" marker icon stays stale until
  that document is next mutated, a narrower, purely cosmetic gap this change knowingly leaves
  (documented in design.md, not fixed here).
- Adds `Trace()` logging to every previously-silent early-return on this path (`CompleteTaskForPlayer`'s
  unresolved branch and `NotifyAssignmentDoneChanged`'s three guard clauses), so a future regression
  here shows up in logs instead of silently vanishing again.

## Capabilities

### New Capabilities
None.

### Modified Capabilities
- `assignment-state-machine`: the existing "Completed is derived from the task's own completion
  flag" requirement gains a scenario covering completion when the task's owning document is not
  currently resolvable (e.g. the Notebook isn't in the completing player's inventory) — derivation
  must still occur.

## Impact

- **Affected code**: `src/Mod/ScribeModSystem.PinOperations.cs` (`CompleteTaskForPlayer`),
  `src/Mod/ScribeModSystem.Assignment.cs` (`NotifyAssignmentDoneChanged`).
- **Affected specs**: delta to `openspec/specs/assignment-state-machine/spec.md`.
- **No Core changes**: `src/Core/ScribeDocument.ToggleTask` already correctly marks a resolved
  block's own embedded Assignment Completed when its document is available; this fix only closes
  the gap in the Mod-layer path that feeds the canonical store when it is not.
- **Test impact**: `tests/Core.Tests` is unaffected (Core is untouched); a Mod-layer/integration
  test should cover completing a pinned assignment task whose Notebook is not in the completing
  player's inventory.
