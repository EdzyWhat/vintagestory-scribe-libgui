# inbox-tab

## Purpose

TBD - created via spec sync from change `add-assignment-and-quest-support`. This capability
covers the shared Inbox tab — one implementation reused by the Assignment Desk, the standalone
Inbox block, and the Inbox nav button on the Lectern/Scriptorium/Chalkboard — plus the two
unseen-assignment indicators (nav-button shimmer, ambient particle) that key off the same trigger.
## Requirements
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
`assignment-state-machine` capability. When the assignment carries an Accepted-transition
destination label (per the `assignment-state-machine` capability's placement requirement), the
row's Accepted-date line SHALL include that label; when no label was recorded (accepted before
this capability existed, or never placed), the line SHALL show the date alone, unchanged from
before.

#### Scenario: Expanding a row reveals assigner, date, and actions
- **WHEN** the player expands an Unaccepted assignment row as its Assignee
- **THEN** the row additionally shows the assigner's name, the in-game assignment date, and
  Accept/Decline action controls

#### Scenario: A terminal-state row shows no action controls when expanded
- **WHEN** the player expands a row whose assignment state is terminal (Declined, Cancelled,
  Discarded, or Completed)
- **THEN** the row shows assigner and date but no state-change action control

#### Scenario: An accepted row with a destination label shows both label and date
- **WHEN** the player expands an assignment whose Accepted transition recorded a destination
  label of `Notebook "Book of Nick"`
- **THEN** the row's accepted line reads as "Accepted into Notebook \"Book of Nick\"" followed
  by the accepted date

#### Scenario: An accepted row with no destination label shows the date alone
- **WHEN** the player expands an assignment that was accepted before destination labels were
  recorded (or was never actually placed)
- **THEN** the row's accepted line shows only the accepted date, with no destination text

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
**12 blocks** of that block. The check SHALL be client-side and player-local; it SHALL NOT reveal
another player's unseen assignments. The particle field SHALL spawn from the block's vertical
midpoint (not from above its top face), SHALL rise to roughly two-thirds of the vertical distance
it covered before this requirement's range/position values changed while its total particle
lifetime is unchanged (particles travel more slowly, not for a shorter time), and SHALL spawn at a
reduced rate — 0.6× the per-tick particle count used before this requirement changed.

#### Scenario: A nearby block particles when the player has a new assignment
- **WHEN** a player with an unseen assignment walks within 12 blocks of any Inbox-capable block
- **THEN** that block emits the ambient particle effect for that player's client only, spawning
  from around the block's vertical midpoint

#### Scenario: No particle once every assignment is seen or resolved
- **WHEN** the player has no assignment in the New (unseen) state
- **THEN** no Inbox-capable block emits the particle effect for that player

#### Scenario: A player outside 12 blocks sees no particle
- **WHEN** a player with an unseen assignment is farther than 12 blocks from every Inbox-capable
  block
- **THEN** no particle is emitted for them until they come within that range

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

### Requirement: Each assignment state's chip renders in a distinct, named color
Each of the six assignment states SHALL render its filter chip and its row's compact state chip in
a color distinct from every other state's, except Declined and Discarded, which SHALL share one
color (both read as terminal rejections). The mapping SHALL be: New → Deep Indigo, Accepted → Rich
Plum/Amethyst, Declined and Discarded → Crimson/Burgundy, Cancelled → Charcoal/Dark Sepia,
Completed → Verdigris/Emerald Ink. The filter-chip row and each row's own state chip SHALL use the
same color for a given state (they SHALL NOT disagree), consistent with the existing requirement
that ties both to one shared lookup.

#### Scenario: Five distinct colors across six states
- **WHEN** the Inbox tab's filter-chip row or any row's state chip renders
- **THEN** New, Accepted, Cancelled, and Completed each render in their own distinct color, and
  Declined and Discarded render in the same color as each other

#### Scenario: The filter chip and a row's chip never disagree
- **WHEN** an assignment is in a given state
- **THEN** its filter-row chip and its own row's collapsed-state chip render in the same color for
  that state

### Requirement: The Inbox tab uses a dedicated inbox-with-arrow icon
Every Inbox nav entry point (the Assignment Desk's Inbox button, the standalone Inbox block's own
nav button, and the Inbox nav button on Lectern/Scriptorium/Chalkboard) SHALL render a dedicated
inbox-with-a-downward-arrow icon, not an icon already used elsewhere in the mod for an unrelated
purpose.

#### Scenario: The Inbox icon is distinct from the Scriptorium's inventory icon
- **WHEN** any Inbox-tab nav button renders its icon
- **THEN** it shows the dedicated inbox-with-down-arrow glyph, not the same icon used for the
  Scriptorium's own inventory-tab affordance or any other unrelated Scribe surface

### Requirement: The Inbox nav button is gated on assignment history for view-only surfaces
On the Lectern, Scriptorium, and Chalkboard — surfaces whose Inbox nav button is a view-only
addition, not their primary capability — the Inbox nav button SHALL be shown only once the viewing
player has received at least one assignment (in any state, ever, including a terminal one). Before
that, no Inbox nav button SHALL appear on those three surfaces. This gating SHALL NOT apply to the
Assignment Desk (whose Inbox tab is one of its exactly two tabs) or the standalone Inbox block
(whose sole capability is the Inbox tab) — both SHALL always show their Inbox surface regardless of
assignment history.

#### Scenario: A player with no assignment history sees no Inbox button on the Lectern
- **WHEN** a player who has never received an assignment opens a Lectern's dialog
- **THEN** no Inbox nav button is present alongside its Guestbook/History nav buttons

#### Scenario: The Inbox button appears once an assignment arrives
- **WHEN** a player who previously had no assignment history receives their first assignment
- **THEN** the Lectern's, Scriptorium's, and Chalkboard's dialogs each show an Inbox nav button on
  their next rebuild, without requiring the dialog to be closed and reopened

#### Scenario: A terminal-only history still counts as "ever assigned"
- **WHEN** a player's only assignment was Declined, Cancelled, or Discarded and no other assignment
  followed
- **THEN** the Inbox nav button remains shown on the Lectern, Scriptorium, and Chalkboard

#### Scenario: The Assignment Desk and standalone Inbox block are never gated
- **WHEN** a player who has never received an assignment opens the Assignment Desk or the
  standalone Inbox block
- **THEN** its Inbox tab (or, for the Inbox block, its sole view) is present exactly as it would be
  for a player with assignment history

