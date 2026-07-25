# task-note-document

## Purpose

TBD - created via spec sync from change `skeuomorphic-lectern-gui`. The base
task-note-document requirements are owned by the not-yet-synced `add-lectern-block` change;
this file currently holds only the requirements added by `skeuomorphic-lectern-gui`.

## Requirements

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

### Requirement: Serialization round-trip includes assignment field and stable identifiers
The system's document serialization SHALL preserve the document's `DocId`, each block's
`TaskId`, and each block's assignment field, in addition to the fields already preserved (kind,
text, completed flag, depth). Serialization SHALL write the current format version, and
deserialization SHALL accept both the current version and the immediately prior version; the
prior version does not carry stable identifiers, so deserializing it SHALL generate fresh
identifiers. Bytes from any version older than the immediately prior one SHALL fail to
deserialize rather than silently defaulting or misreading fields.

#### Scenario: Round-trip preserves identifiers and assignment
- **WHEN** a document is serialized in the current format and then deserialized
- **THEN** the resulting document has the same `DocId`, each block has the same `TaskId`, and
  each block's assignment field matches the original

#### Scenario: Prior version deserializes with generated identifiers
- **WHEN** bytes produced by the immediately prior format version are deserialized
- **THEN** deserialization succeeds, the document is assigned a fresh `DocId`, and each block is
  assigned a fresh `TaskId`

#### Scenario: An unsupported older version fails to deserialize
- **WHEN** bytes produced by a format version older than the immediately prior one are
  deserialized
- **THEN** deserialization reports failure rather than producing a document with incorrect or
  default values

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

### Requirement: Documents and tasks carry stable identifiers
Every document SHALL carry a stable identifier (`DocId`), and every block SHALL carry a stable
identifier (`TaskId`), each assigned when the document or block is created. These identifiers
SHALL remain unchanged across every mutation of the document — reordering, inserting, deleting
other blocks, toggling completion, and editing text — and SHALL be preserved through
serialization. A block's identifier SHALL be distinct from every other block's, including newly
inserted blocks.

#### Scenario: Identifiers are stable across mutations
- **WHEN** a document's blocks are reordered, other blocks are inserted or deleted, and a block's
  text or completed state is changed
- **THEN** the surviving blocks retain the same `TaskId` values they had before, and the document
  retains the same `DocId`

#### Scenario: A newly inserted block gets a distinct identifier
- **WHEN** a new block is added or inserted into a document
- **THEN** it is assigned a `TaskId` distinct from every existing block's

#### Scenario: Identifiers survive serialization
- **WHEN** a document is serialized and then deserialized
- **THEN** the resulting document has the same `DocId`, and each block has the same `TaskId`, as
  the original

### Requirement: Delete reports the removed task's identifier
Deleting a block SHALL report the stable identifier of the block that was removed (or report
that nothing was removed for an invalid index), so a caller can react to the removal of a
specific task (for example, to orphan references to it).

#### Scenario: Delete reports the removed identifier
- **WHEN** a block at a valid index is deleted
- **THEN** the operation reports the deleted block's `TaskId`

#### Scenario: Delete at an invalid index reports no removal
- **WHEN** a delete targets an index that does not exist
- **THEN** the document is unchanged and the operation reports that no task was removed

### Requirement: Look up a task by its identifier
The document SHALL support looking up a block by its stable identifier, returning the matching
block or reporting that none matches.

#### Scenario: Look up a present task
- **WHEN** a lookup uses the identifier of a block in the document
- **THEN** that block is returned

#### Scenario: Look up an absent task
- **WHEN** a lookup uses an identifier that no block in the document has
- **THEN** the lookup reports no match

### Requirement: Prior-version pin flags are surfaced for migration
Because the prior format version stored a per-task pinned flag that the current version no longer
stores, deserializing prior-version bytes SHALL make available the identifiers of the tasks that
were flagged pinned, so a caller can migrate them into a per-player pin store. Deserializing
current-version bytes SHALL surface no such flags (the current format has none).

#### Scenario: Prior-version pinned tasks are surfaced
- **WHEN** prior-version bytes containing pinned tasks are deserialized through the migration path
- **THEN** the operation reports the (freshly generated) `TaskId` of each task that was flagged
  pinned in those bytes

#### Scenario: Current-version bytes surface no pin flags
- **WHEN** current-version bytes are deserialized through the migration path
- **THEN** the operation reports no pinned-task identifiers
