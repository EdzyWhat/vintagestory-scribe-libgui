## ADDED Requirements

### Requirement: A notice-originated assignment's tracked lifecycle begins at Accepted
An assignment created by accepting a Task Notice (per the `task-notice-item` capability) SHALL
enter the `ScribeAssignmentStore` directly in the Accepted state — it SHALL NOT pass through a
store-tracked Unaccepted state, since the pending Accept/Decline decision was carried by the
physical item itself, not by a store record, before that point. All other requirements of this
capability (derived Completed, Discard, Assigner read-only visibility, terminal-record deletion)
apply to a notice-originated assignment identically to an in-range one from the moment it enters
the store.

#### Scenario: A notice-originated record starts at Accepted, not Unaccepted
- **WHEN** the Assignee accepts a Task Notice
- **THEN** the resulting `ScribeAssignmentStore` record is created already in the Accepted state,
  with no prior Unaccepted record ever having existed for it

#### Scenario: Post-accept behavior is otherwise unchanged
- **WHEN** a notice-originated assignment is later completed, discarded, or deleted as a terminal
  record
- **THEN** it follows the same rules as an in-range assignment in the identical situation, per this
  capability's existing requirements
