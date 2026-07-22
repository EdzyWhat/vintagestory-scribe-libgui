# task-note-document

## Purpose

TBD - created via spec sync from change `skeuomorphic-lectern-gui`. The base
task-note-document requirements are owned by the not-yet-synced `add-lectern-block` change;
this file currently holds only the requirements added by `skeuomorphic-lectern-gui`.

## Requirements

### Requirement: Pin a task
The system SHALL allow toggling a "pinned" flag on a task block, identified by its
position. Pinning SHALL NOT be allowed on a text-section block.

#### Scenario: Pin an unpinned task
- **WHEN** an unpinned task is toggled
- **THEN** it becomes pinned

#### Scenario: Unpin a pinned task
- **WHEN** a pinned task is toggled
- **THEN** it becomes unpinned

#### Scenario: Pinning a text section fails
- **WHEN** a pin toggle targets a text-section block
- **THEN** the document is unchanged and the operation reports failure

#### Scenario: Pinning an invalid position fails safely
- **WHEN** a pin toggle references a position that does not exist
- **THEN** the document is left unchanged and the operation reports failure

### Requirement: Reserved assignment field
Every block SHALL carry an assignment field (an optional identifier, absent by default)
that is persisted through serialization but has no mutation operation and no consumer in
this document capability. This field exists so a future capability can define its own
semantics (e.g. assigning a task to a player or group) without a further format change.

#### Scenario: A new block has no assignment
- **WHEN** a task or text section is added
- **THEN** its assignment field is absent (unset)

#### Scenario: Assignment survives serialization even though nothing sets it
- **WHEN** a document containing blocks with unset assignment fields is serialized and
  deserialized
- **THEN** the resulting blocks still have their assignment fields absent (unset)

### Requirement: Serialization round-trip includes pin and assignment fields
The system's document serialization SHALL preserve each block's pinned flag and
assignment field, in addition to the fields already preserved (kind, text, completed
flag, depth). A document serialized under an earlier format version SHALL fail to
deserialize rather than silently defaulting or misreading the new fields.

#### Scenario: Round-trip preserves pinned state
- **WHEN** a document with a mix of pinned and unpinned tasks is serialized and then
  deserialized
- **THEN** the resulting document's tasks have the same pinned state, in the same order,
  as the original

#### Scenario: An earlier format version fails to deserialize
- **WHEN** bytes produced by an earlier serialization format version are deserialized
- **THEN** deserialization reports failure rather than producing a document with
  incorrect or default pinned/assignment values

### Requirement: Document stores task text verbatim
The document model SHALL store a task's text exactly as supplied, without trimming leading,
trailing, or interior whitespace. The only content invariant the model enforces on a task is
that its text is not blank or whitespace-only: an add or text-change with blank/whitespace-only
task text SHALL be rejected and leave the document unchanged. Whitespace normalization (e.g.
trimming a trailing blank line from a committed edit) is the responsibility of the editing layer,
not the document model.

#### Scenario: Adding a task preserves surrounding whitespace
- **WHEN** a task is added with text that has leading and/or trailing whitespace around
  non-blank content
- **THEN** the stored task text retains that whitespace exactly as supplied, rather than being
  trimmed

#### Scenario: Changing a task's text preserves surrounding whitespace
- **WHEN** a task block's text is changed to a value with leading and/or trailing whitespace
  around non-blank content
- **THEN** the stored task text retains that whitespace exactly as supplied

#### Scenario: Blank or whitespace-only task text is rejected
- **WHEN** a task is added, or an existing task's text is changed, with text that is empty or
  contains only whitespace
- **THEN** the operation reports failure and the document is left unchanged
