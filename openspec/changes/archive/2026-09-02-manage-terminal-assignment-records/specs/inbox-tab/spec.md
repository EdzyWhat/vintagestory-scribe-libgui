## ADDED Requirements

### Requirement: A terminal-state row's expanded view offers a delete control that permanently removes the record
When a row whose assignment state is terminal (Declined, Cancelled, Discarded, or Completed) is
expanded, it SHALL show one delete control, labeled/tooltipped "Remove Terminal Record", in
addition to the assigner and date already shown. This control is not a state-change action — it
is only visible to the record's Assigner or Assignee, and tapping it permanently removes the
assignment record (it does not transition the record to another state). Non-terminal rows
(Unaccepted, Accepted) SHALL NOT show this control at any expansion state. Tapping the control
SHALL take effect with no confirmation step.

#### Scenario: A terminal row's expanded view offers deletion
- **WHEN** the player expands a row whose assignment state is Declined, Cancelled, Discarded, or
  Completed
- **THEN** the row shows a "Remove Terminal Record" control alongside the assigner and date

#### Scenario: Deleting a terminal record removes it immediately
- **WHEN** the player taps "Remove Terminal Record" on an expanded terminal-state row
- **THEN** the assignment record is permanently removed and no longer appears in either the
  Inbox or Sent History, with no confirmation prompt shown first

#### Scenario: Non-terminal rows never show the delete control
- **WHEN** the player expands a row whose assignment state is Unaccepted or Accepted
- **THEN** no delete control is shown, regardless of who is viewing it

### Requirement: A title-bar toggle expands or collapses every currently-visible row at once
While the Inbox or Sent History view is the dialog's active view, the title bar SHALL show one
icon-only toggle button, positioned immediately left of the drag-grip-handle, that expands every
currently-visible row (those passing the active filter chips) when at least one is collapsed, or
collapses all of them when every one is already expanded. The button SHALL NOT appear while any
other view is active. Each row's individual chevron SHALL continue to toggle only that one row,
independent of this button.

#### Scenario: The toggle expands every visible row
- **WHEN** the player taps the title-bar toggle while at least one currently-visible row is
  collapsed
- **THEN** every currently-visible row becomes expanded

#### Scenario: The toggle collapses every visible row once all are expanded
- **WHEN** the player taps the title-bar toggle while every currently-visible row is already
  expanded
- **THEN** every currently-visible row becomes collapsed

#### Scenario: The toggle is absent outside Inbox and Sent History
- **WHEN** the dialog's active view is anything other than Inbox or Sent History
- **THEN** the title bar shows no expand/collapse-all toggle

#### Scenario: Filtered-out rows are unaffected by the toggle
- **WHEN** the player taps the title-bar toggle while a state filter chip is hiding some rows
- **THEN** only the currently-visible rows change expansion state; hidden rows' expansion state is
  unchanged
