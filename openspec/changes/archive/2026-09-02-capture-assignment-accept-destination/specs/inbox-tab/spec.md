## MODIFIED Requirements

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
