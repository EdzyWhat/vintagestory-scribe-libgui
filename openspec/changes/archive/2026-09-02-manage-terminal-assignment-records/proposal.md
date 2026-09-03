## Why

Playtesting the Assignment Desk Inbox and Sent History surfaces a real usability problem: once a
handful of assignments reach a terminal state (Declined, Cancelled, Discarded, Completed), the
list gets cluttered with records the player has no way to clear out, and there's no fast way to
scan a long list without expanding rows one at a time. Both gaps compound each other during
testing sessions that generate many terminal records quickly.

## What Changes

- Add a delete affordance ("Remove Terminal Record") to any Inbox or Sent History row whose
  assignment is in a terminal state (Declined, Cancelled, Discarded, Completed), visible only
  once that row's caret is expanded. Non-terminal rows (Unaccepted, Accepted) never show it.
  Deleting fires immediately (no confirmation dialog, matching every existing delete in the mod)
  and permanently removes the assignment record from the store.
- Add a new `ScribeAssignmentStore.TryDelete` method (the store is currently append-only) plus a
  client→server network message/handler, authorized to only the record's Assigner or Assignee.
- Add a single expand/collapse-all toggle button to the dialog title bar, positioned immediately
  left of the drag-grip-handle, visible only while the Inbox or Sent History view is active. It
  reuses the existing triangle-up/triangle-down chevron icons (no new art): down when not all
  currently-visible rows are expanded (tap expands all of them), up when all are expanded (tap
  collapses all of them). Icon-only, with a tooltip carrying the "Expand all"/"Collapse all" text.
- Lift each row's expand/collapse state out of its private per-row field into a shared
  `HashSet<Guid>` owned by the Inbox content state, so the new toggle can read and bulk-mutate it.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `inbox-tab`: terminal-state rows now show one action control (delete) when expanded, revising
  the existing "no action controls" scenario; adds the expand/collapse-all title-bar toggle and
  the shared expanded-state tracking it depends on.
- `assignment-state-machine`: adds permanent deletion of a terminal assignment record from the
  store, restricted to its Assigner or Assignee, as a new capability distinct from any state
  transition (a deleted record no longer exists at all, rather than moving to another state).

## Impact

- `src/Core/ScribeAssignmentStore.cs` — new `TryDelete` method; `src/Core/ScribeAssignment.cs` —
  new terminal-state helper.
- `src/Mod/ScribeInboxContent.cs` — delete button in `BuildExpandedDetail`; lift `expanded` state
  into shared `ScribeInboxContentState`.
- `src/Mod/ScribeDialogBase.Layout.cs` — new title-bar toggle button.
- `src/Mod/ScribeModSystem.Assignment.cs` — new network handler for the delete message.
- New `src/Mod/ScribeDeleteAssignmentMessage.cs` (mirrors `ScribeDeleteHistoryEntryMessage.cs`).
- `src/Mod/assets/scribe/lang/en.json` — new lang keys for the delete tooltip/label and the two
  toggle tooltip states.
