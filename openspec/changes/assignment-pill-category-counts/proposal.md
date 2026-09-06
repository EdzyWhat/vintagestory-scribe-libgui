## Why

The Inbox and Sent Assignment History tabs each show a row of filter chips ("All", "New",
"Accepted", "Cancelled", "Completed"). A chip's active/inactive state is visible, but not how
many rows currently sit in that category, so a player has to toggle a chip on just to find out
whether anything is in it. A per-category count lets the player scan the row once and know where
to look.

## What Changes

- Each non-"All" filter chip on the shared Inbox/Sent-History chip row appends a count of the
  rows currently in that category to its label, e.g. `New (1)`, `Accepted (3)`.
- The `All` chip never shows a count.
- A category with zero matching rows shows its plain label with no count/parentheses.
- The count is computed from the same row source the chip's own filter already narrows
  (`MyReceivedAssignments` on Inbox, `MySentAssignments` on Sent History), so it always reflects
  what toggling that chip on would reveal, independent of which chips are currently active.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `inbox-tab`: the filter chip row requirement gains a per-category count in each non-"All"
  chip's label.

## Impact

- `src/Mod/ScribeInboxContent.cs`: `ScribeInboxContentState.BuildFilterChip` (chip label
  construction) and `ScribeInboxContentState.Build` (per-group count computation, alongside the
  existing per-group `StatesFor` filtering).
- No change to `src/Core/` (pure Mod-layer GUI presentation) and no change to
  `ScribeAssignmentState`/`ScribeAssignmentFilterGroup` semantics.
