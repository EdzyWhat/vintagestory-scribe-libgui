## MODIFIED Requirements

### Requirement: Assignment field carries assigner, state, and assigned date
Every block SHALL carry an optional assignment reference (absent by default) that, when present,
records the assigner's player UID, the current assignment state (one of the six states defined
by the `assignment-state-machine` capability), and the in-game date the assignment was sent.
This field is persisted through serialization. The bare identifier previously reserved for this
purpose (an unset, semantics-free field) is replaced by this richer type — no prior shipped save
ever populated the old field, so no migration path is required.

#### Scenario: A new block has no assignment
- **WHEN** a task or text section is added
- **THEN** its assignment reference is absent (unset)

#### Scenario: An assigned block carries assigner, state, and date
- **WHEN** a task is created via the Assignment Desk's Assignment tab and sent to another player
- **THEN** the resulting block's assignment reference records the assigner's UID, an initial
  Unaccepted state, and the in-game date it was sent

#### Scenario: Assignment survives serialization
- **WHEN** a document containing both assigned and unassigned blocks is serialized and
  deserialized
- **THEN** unassigned blocks still have an absent assignment reference, and assigned blocks
  retain their assigner UID, state, and assigned date unchanged
