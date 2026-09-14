## MODIFIED Requirements

### Requirement: Assignment field carries assigner, state, and assigned date
Every block SHALL carry an optional assignment reference (absent by default) that, when present,
records the assigner's player UID, the current assignment state (one of the six states defined
by the `assignment-state-machine` capability), the in-game date the assignment was sent as an
identity string, and a numeric in-game timestamp for that same instant. This field is persisted
through serialization. The bare identifier previously reserved for this purpose (an unset,
semantics-free field) is replaced by this richer type — no prior shipped save ever populated the
old field, so no migration path is required. A document blob written before the timestamp field
SHALL load with the identity date string and no timestamp. When a timestamp is present, surfaces
that show the assigned date SHALL format it in the viewing player's locale via `scribe:date-format`.

#### Scenario: A new block has no assignment
- **WHEN** a task or text section is added
- **THEN** its assignment reference is absent (unset)

#### Scenario: An assigned block carries assigner, state, and date
- **WHEN** a task is created via the Assignment Desk's Assignment tab and sent to another player
- **THEN** the resulting block's assignment reference records the assigner's UID, an initial
  Unaccepted state, the in-game date it was sent, and a timestamp for that instant

#### Scenario: Assignment survives serialization
- **WHEN** a document containing both assigned and unassigned blocks is serialized and
  deserialized
- **THEN** unassigned blocks still have an absent assignment reference, and assigned blocks
  retain their assigner UID, state, assigned date, and assigned timestamp unchanged
