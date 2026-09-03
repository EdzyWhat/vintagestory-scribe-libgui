## ADDED Requirements

### Requirement: A terminal assignment record can be permanently deleted by its Assigner or Assignee
An assignment record whose state is terminal (Declined, Cancelled, Discarded, or Completed) MAY
be permanently removed from the store by either its Assigner or its Assignee. Deletion is
distinct from every transition in the state machine: a deleted record no longer exists at all,
rather than moving to a further state, and deletion SHALL NOT be offered or performed on a record
whose state is Unaccepted or Accepted. Any deletion attempt by a player who is neither the
record's Assigner nor its Assignee SHALL be rejected and the record SHALL remain unchanged.

#### Scenario: The Assignee deletes a terminal record
- **WHEN** the Assignee of a Declined, Cancelled, Discarded, or Completed assignment requests its
  deletion
- **THEN** the record is permanently removed from the store

#### Scenario: The Assigner deletes a terminal record
- **WHEN** the Assigner of a Declined, Cancelled, Discarded, or Completed assignment requests its
  deletion
- **THEN** the record is permanently removed from the store

#### Scenario: Deletion is rejected on a non-terminal record
- **WHEN** either party requests deletion of an assignment whose state is Unaccepted or Accepted
- **THEN** the deletion is rejected and the record remains unchanged

#### Scenario: Deletion is rejected for an uninvolved player
- **WHEN** a player who is neither the Assigner nor the Assignee of a terminal assignment
  requests its deletion
- **THEN** the deletion is rejected and the record remains unchanged
