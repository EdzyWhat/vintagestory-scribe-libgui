## MODIFIED Requirements

### Requirement: A pin's snapshot carries enough assignment provenance to render its tooltip without the source document
When a pinned task is an accepted assignment, the pin's persisted/synced snapshot SHALL include the
assigner's player uid, the date the assignment was sent, the date it was accepted, and a numeric
in-game timestamp for each of those dates when one was captured — so the Pin Tab can render the
assignment marker's tooltip using only the snapshot, without resolving the task's source document
(which may not be loaded). When a timestamp is present, the Pin Tab and HUD SHALL format that date
in the viewing player's locale via `scribe:date-format`. Pin identity and dirty-checking SHALL
continue to use the stored date strings, not the displayed locale form. A pin blob written before
timestamps SHALL load with the date strings and no timestamps.

#### Scenario: Pinning an accepted assignment captures its provenance
- **WHEN** a player pins a task that is an accepted assignment
- **THEN** the pin's snapshot records the assigner's uid, the assigned date, the accepted date,
  and the matching timestamps alongside the existing accepted-assignment flag

#### Scenario: A pre-existing pin blob still loads
- **WHEN** the pin store reads a pin-list blob written before this field was added
- **THEN** it loads successfully, with the new fields defaulting to empty/absent for that pin

#### Scenario: A pin tooltip date follows the viewer
- **WHEN** a pin snapshot carries assigned and accepted timestamps, and a player whose client
  locale is not English views the Pin Tab
- **THEN** those dates are built from the timestamps in that client's locale
