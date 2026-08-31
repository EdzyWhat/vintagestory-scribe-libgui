## ADDED Requirements

### Requirement: Inbox rows collapse to a compact summary by default
Each assignment in the Inbox tab SHALL render, by default, a single-line collapsed row showing
the task's checkbox/tracker control, its text, its depth indent, and a compact state chip
(reflecting one of the six assignment states). Assigner name, in-game assignment date, and any
state-change action SHALL NOT be shown in the collapsed row.

#### Scenario: A collapsed row shows only the essentials
- **WHEN** the Inbox tab renders an assignment that has not been expanded
- **THEN** the row shows the task text, depth indent, tracker/checkbox control (if applicable),
  and a state chip, and nothing else

### Requirement: A leading chevron is the sole expand/collapse trigger
Each Inbox row SHALL show a chevron disclosure control at its leading edge, before the
checkbox/tracker control. Clicking the chevron SHALL toggle that row between collapsed and
expanded. No other click target on the row (task text, checkbox, tracker stepper, pin, delete)
SHALL toggle expand/collapse.

#### Scenario: Chevron toggles expansion
- **WHEN** the player clicks a row's leading chevron
- **THEN** that row toggles between its collapsed and expanded rendering

#### Scenario: Other controls do not toggle expansion
- **WHEN** the player clicks the row's text, checkbox, tracker stepper, pin, or delete control
- **THEN** the row's expanded/collapsed state is unchanged

### Requirement: An expanded row shows assigner, date, and legal actions
When a row is expanded, it SHALL additionally show the assigner's name, the in-game date the
assignment was sent, and any state-change action(s) currently legal for the viewing player given
the assignment's current state and their role (Assigner or Assignee), per the
`assignment-state-machine` capability.

#### Scenario: Expanding a row reveals assigner, date, and actions
- **WHEN** the player expands an Unaccepted assignment row as its Assignee
- **THEN** the row additionally shows the assigner's name, the in-game assignment date, and
  Accept/Decline action controls

#### Scenario: A terminal-state row shows no action controls when expanded
- **WHEN** the player expands a row whose assignment state is terminal (Declined, Cancelled,
  Discarded, or Completed)
- **THEN** the row shows assigner and date but no state-change action control

### Requirement: The Inbox tab can filter by assignment state via a chip row
The Inbox tab SHALL provide a row of toggleable filter chips, one per assignment state, always
visible above the row list, letting the player narrow the visible rows to one or more of the six
assignment states so a long history of terminal-state assignments does not obscure active ones
by default. The active filter SHALL be visible at a glance with no control needing to be opened
to see which states are currently selected.

#### Scenario: Filtering to only active assignments
- **WHEN** the player toggles on only the Unaccepted and Accepted chips
- **THEN** rows in Declined, Cancelled, Discarded, or Completed states are hidden from the list

#### Scenario: The active filter is always visible
- **WHEN** the Inbox tab is showing any filter selection
- **THEN** the active/inactive state of every chip is visible without opening any additional
  control

### Requirement: The Inbox nav button shimmers when its tab isn't the active view
On any surface with an Inbox nav button that is not the dialog's currently active view
(Assignment Desk's Inbox button while the Assignment tab is showing; the Inbox nav button on
Lectern, Scriptorium, or Chalkboard), the button's icon SHALL play a periodic shimmer sweep
whenever the viewing player has a New (unseen) assignment. The shimmer SHALL stop and the button
SHALL render plain once the Inbox tab becomes the active view, or once no unseen assignment
remains. The standalone Inbox block has no other tab and SHALL NOT show this shimmer.

#### Scenario: The Inbox button shimmers while another tab is open
- **WHEN** a player with an unseen assignment has a Lectern's dialog open showing the Read view
  (Inbox not active)
- **THEN** the Lectern's Inbox nav button plays the periodic shimmer sweep

#### Scenario: The shimmer stops once the Inbox tab is opened
- **WHEN** the player switches that dialog to the Inbox tab
- **THEN** the shimmer stops playing on the Inbox nav button

#### Scenario: No shimmer once nothing is unseen
- **WHEN** the player has no New (unseen) assignment
- **THEN** no Inbox nav button on any surface plays the shimmer

### Requirement: Inbox-capable blocks show an ambient particle when the player has an unseen assignment
Every Inbox-capable block (Assignment Desk, standalone Inbox, and any Lectern, Scriptorium, or
Chalkboard exposing the Inbox nav button) SHALL emit an ambient particle effect, visible only to
the local client, when the viewing player has at least one New (unseen) assignment and is within
range of that block. The check SHALL be client-side and player-local; it SHALL NOT reveal
another player's unseen assignments.

#### Scenario: A nearby block particles when the player has a new assignment
- **WHEN** a player with an unseen assignment walks within range of any Inbox-capable block
- **THEN** that block emits the ambient particle effect for that player's client only

#### Scenario: No particle once every assignment is seen or resolved
- **WHEN** the player has no assignment in the New (unseen) state
- **THEN** no Inbox-capable block emits the particle effect for that player
