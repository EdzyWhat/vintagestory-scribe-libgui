## ADDED Requirements

### Requirement: A pin's snapshot carries enough assignment provenance to render its tooltip without the source document
When a pinned task is an accepted assignment, the pin's persisted/synced snapshot SHALL include the
assigner's player uid, the date the assignment was sent, and the date it was accepted — so the Pin
Tab can render the assignment marker's tooltip using only the snapshot, without resolving the task's
source document (which may not be loaded).

#### Scenario: Pinning an accepted assignment captures its provenance
- **WHEN** a player pins a task that is an accepted assignment
- **THEN** the pin's snapshot records the assigner's uid, the assigned date, and the accepted date
  alongside the existing accepted-assignment flag

#### Scenario: A pre-existing pin blob still loads
- **WHEN** the pin store reads a pin-list blob written before this field was added
- **THEN** it loads successfully, with the new fields defaulting to empty/absent for that pin
