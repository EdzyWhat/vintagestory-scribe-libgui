## MODIFIED Requirements

### Requirement: The Inbox tab can filter by assignment state via a chip row
The Inbox tab SHALL provide a row of toggleable filter chips, one per assignment state, always
visible above the row list, letting the player narrow the visible rows to one or more of the six
assignment states so a long history of terminal-state assignments does not obscure active ones
by default. The active filter SHALL be visible at a glance with no control needing to be opened
to see which states are currently selected. Every chip other than "All" SHALL append, in
parentheses after its label, a count of the rows currently in that chip's category (independent
of which chips are active) whenever that count is greater than zero; the "All" chip SHALL never
show a count, and a chip whose category currently has zero rows SHALL show its plain label with
no parentheses.

#### Scenario: Filtering to only active assignments
- **WHEN** the player toggles on only the Unaccepted and Accepted chips
- **THEN** rows in Declined, Cancelled, Discarded, or Completed states are hidden from the list

#### Scenario: The active filter is always visible
- **WHEN** the Inbox tab is showing any filter selection
- **THEN** the active/inactive state of every chip is visible without opening any additional
  control

#### Scenario: A non-"All" chip shows its category's row count
- **WHEN** the Inbox tab has, for example, one row in the New category and three rows in the
  Accepted category
- **THEN** the New chip's label reads "New (1)" and the Accepted chip's label reads "Accepted (3)"

#### Scenario: The "All" chip never shows a count
- **WHEN** the Inbox tab renders its filter chip row, regardless of how many rows exist
- **THEN** the "All" chip's label shows no count

#### Scenario: A chip with zero matching rows shows no count
- **WHEN** a category currently has no rows in it
- **THEN** that chip's label shows no parentheses or count

#### Scenario: A chip's count reflects its category regardless of the active filter
- **WHEN** the player has toggled off a given category's chip, hiding its rows from the list
- **THEN** that chip's label still shows the count of rows in its category, unaffected by it
  being inactive
