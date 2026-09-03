## MODIFIED Requirements

### Requirement: A terminal assignment record can be permanently deleted by its Assigner or Assignee
An assignment record whose state is terminal (Declined, Cancelled, Discarded, or Completed) MAY
be deleted independently by its Assigner and by its Assignee — each deletion removes the record
from only that party's own view (the Assigner's Sent Assignment History, or the Assignee's
Inbox, respectively), never both. This holds even when the same player is both the Assigner and
the Assignee of a self-assignment: the two sides are deleted independently, one request at a
time. Deletion is distinct from every transition in the state machine: it never moves the record
to a further state and SHALL NOT be offered or performed on a record whose state is Unaccepted or
Accepted. Any deletion attempt by a player who is neither the record's Assigner nor its Assignee
SHALL be rejected and the record SHALL remain unchanged. Once BOTH the Assigner and the Assignee
have deleted their own side, the record no longer exists at all.

#### Scenario: The Assignee deletes a terminal record
- **WHEN** the Assignee of a Declined, Cancelled, Discarded, or Completed assignment requests its
  deletion
- **THEN** the record no longer appears in that Assignee's Inbox
- **AND** the record still appears in the Assigner's Sent Assignment History, unchanged

#### Scenario: The Assigner deletes a terminal record
- **WHEN** the Assigner of a Declined, Cancelled, Discarded, or Completed assignment requests its
  deletion
- **THEN** the record no longer appears in that Assigner's Sent Assignment History
- **AND** the record still appears in the Assignee's Inbox, unchanged

#### Scenario: A self-assignment's two sides are deleted independently
- **WHEN** a player who is both the Assigner and the Assignee of a terminal self-assignment
  deletes it from their Inbox only
- **THEN** the record no longer appears in that player's Inbox
- **AND** the record still appears in that same player's Sent Assignment History, unchanged

#### Scenario: A record is fully removed once both sides have deleted it
- **WHEN** the Assignee has already deleted their own side of a terminal record and the Assigner
  then deletes their own side too
- **THEN** the record no longer exists in the store at all

#### Scenario: Deletion is rejected on a non-terminal record
- **WHEN** either party requests deletion of an assignment whose state is Unaccepted or Accepted
- **THEN** the deletion is rejected and the record remains unchanged

#### Scenario: Deletion is rejected for an uninvolved player
- **WHEN** a player who is neither the Assigner nor the Assignee of a terminal assignment
  requests its deletion
- **THEN** the deletion is rejected and the record remains unchanged
