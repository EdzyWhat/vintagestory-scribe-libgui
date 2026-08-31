# task-note-document

## Purpose

TBD - created via spec sync from change `skeuomorphic-lectern-gui`. The base
task-note-document requirements are owned by the not-yet-synced `add-lectern-block` change;
this file currently holds only the requirements added by `skeuomorphic-lectern-gui`.
## Requirements
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

### Requirement: Document stores task text verbatim
The document model SHALL store a task's text exactly as supplied, without trimming leading,
trailing, or interior whitespace, and without rejecting any value. The model SHALL NOT enforce a
non-blank content invariant on task text: an add or text-change with blank or whitespace-only task
text SHALL succeed and store that text verbatim, exactly as it does for a freeform text section.
Ensuring an empty task is not *persisted* (removing an abandoned or cleared empty task) is the
responsibility of the editing layer, not the document model — consistent with the model's role of
storing text verbatim while normalization and content policy live in the editing layer.

#### Scenario: Adding a task preserves surrounding whitespace
- **WHEN** a task is added with text that has leading and/or trailing whitespace around
  non-blank content
- **THEN** the stored task text retains that whitespace exactly as supplied, rather than being
  trimmed

#### Scenario: Changing a task's text preserves surrounding whitespace
- **WHEN** a task block's text is changed to a value with leading and/or trailing whitespace
  around non-blank content
- **THEN** the stored task text retains that whitespace exactly as supplied

#### Scenario: Empty or whitespace-only task text is accepted
- **WHEN** a task is added, or an existing task's text is changed, with text that is empty or
  contains only whitespace
- **THEN** the operation succeeds and the document stores that empty/whitespace-only text
  verbatim, rather than reporting failure and leaving the document unchanged

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

### Requirement: Document is an ordered sequence of blocks

The system SHALL represent a Scribe document as an ordered sequence of blocks, where each
block is one of a fixed set of **kinds**: a **task** (text plus a completed flag), a **text
section** (freeform text, no checkbox), a **tracker** (a task that counts progress toward a
target item and quantity), or a **link** (a task referencing a Handbook page / location). Blocks
of any kind MAY be interspersed in any order. Each block SHALL carry a depth value (reserved for
future sub-item nesting; 0 for now). Kind values SHALL be assigned in append-only order — existing
kinds are never renumbered when a new kind is added. This model MUST NOT depend on the Vintage
Story API.

#### Scenario: A new document is empty

- **WHEN** a new document is created
- **THEN** it has zero blocks

#### Scenario: Block order is preserved

- **WHEN** a task, then a text section, then another task are added
- **THEN** the document lists them in that exact order with their respective kinds

#### Scenario: Blocks of every kind may be interspersed

- **WHEN** a task, a tracker, a link, and a text section are added in that order
- **THEN** the document lists all four in that order, each reporting its own kind

### Requirement: Add a task

The system SHALL allow adding a task block with given text to the end of the document.

#### Scenario: Add a task to an empty document

- **WHEN** a task with text "Find copper" is added to an empty document
- **THEN** the document contains exactly one task block, with text "Find copper" and not completed

#### Scenario: Adding a task trims and rejects blank text

- **WHEN** a task is added with text that is empty or only whitespace
- **THEN** no block is added and the operation reports that the input was invalid

### Requirement: Add a text section

The system SHALL allow adding a freeform text-section block to the end of the document.
Text sections MAY be empty.

#### Scenario: Add a text section

- **WHEN** a text section "Copper is south of the ridge" is added
- **THEN** the document contains one text-section block with that text

#### Scenario: Empty text section is allowed

- **WHEN** a text section is added with empty text
- **THEN** a text-section block is added with empty text

### Requirement: Edit a block's text

The system SHALL allow changing the text of an existing block identified by its position,
preserving that block's kind and completed flag. For task blocks blank text SHALL be
rejected; text sections MAY be set to empty.

#### Scenario: Rename a task keeps its done flag

- **WHEN** the text of a completed task at position 0 is changed to "Find tin"
- **THEN** the block at position 0 has text "Find tin" and is still completed

#### Scenario: Editing a task rejects blank text

- **WHEN** a task block's text is changed to empty or only whitespace
- **THEN** the text is unchanged and the operation reports that the input was invalid

#### Scenario: A text section may be cleared

- **WHEN** a text section's text is set to empty
- **THEN** the block's text is empty

### Requirement: Toggle task completion

The system SHALL allow toggling the completed flag of a task block. Toggling a text
section SHALL fail.

#### Scenario: Toggle an incomplete task

- **WHEN** an incomplete task is toggled
- **THEN** it becomes completed

#### Scenario: Toggle a completed task

- **WHEN** a completed task is toggled
- **THEN** it becomes incomplete

#### Scenario: Toggling a text section fails

- **WHEN** a toggle targets a text-section block
- **THEN** the document is unchanged and the operation reports failure

### Requirement: Delete a block

The system SHALL allow removing a block by its position, preserving the order of the rest.

#### Scenario: Delete a block from the middle

- **WHEN** the block at position 1 of three blocks is deleted
- **THEN** the document has two blocks, the former first and third, in that order

### Requirement: Reorder blocks

The system SHALL allow moving a block from one position to another, shifting the others to
keep a single ordered sequence.

#### Scenario: Move a block to a later position

- **WHEN** the block at position 0 of three is moved to position 2
- **THEN** it appears last and the other two keep their relative order

#### Scenario: Move a block to an earlier position

- **WHEN** the block at position 2 of three is moved to position 0
- **THEN** it appears first and the other two keep their relative order

#### Scenario: Moving to the same position is a no-op

- **WHEN** a block is moved to the position it already occupies
- **THEN** the operation succeeds and the order is unchanged

### Requirement: Out-of-range operations are safe

The system SHALL reject block operations that reference a position outside the current
sequence without corrupting the document or throwing to the caller.

#### Scenario: Operate on an invalid position

- **WHEN** an edit, toggle, delete, or move references a position that does not exist
- **THEN** the document is left unchanged and the operation reports failure

### Requirement: Serialization round-trip

The system SHALL serialize a document to a byte array and deserialize it back to an equal
document, so it can be persisted and sent over the network. The serialized form SHALL
preserve block order, each block's kind, text, completed flag, and depth.

#### Scenario: Round-trip preserves content

- **WHEN** a document with interspersed tasks (mixed completion) and text sections is serialized and then deserialized
- **THEN** the resulting document equals the original in block order, kinds, text, completed flags, and depth

#### Scenario: Deserializing invalid data fails safely

- **WHEN** deserialization is given empty or malformed bytes
- **THEN** it reports failure rather than throwing, and yields no partial document

### Requirement: Edit packets carry DocId instead of BlockPos
All document-edit network packets (save document, set task done, request/release editor
lock) SHALL carry the `DocId` (16-byte array) as the sole document address field. No
`PosX/PosY/PosZ` fields. The server routes via the host registry. Document model and
serialization are unchanged.

#### Scenario: Edit packet routes by DocId
- **WHEN** a client flushes an autosave or explicit save
- **THEN** the packet contains the `DocId` only (no block position), and the server
  resolves the host via the host registry

### Requirement: Document carries a title field
`ScribeDocument` SHALL carry a `Title` string property (max 80 chars). The default value when
unset or empty after trim SHALL be `"Lectern"`. The title SHALL be serialized in the document
codec and SHALL survive a round-trip through serialize/deserialize.

#### Scenario: Title round-trips through serialization
- **WHEN** a document with `Title = "My Lectern"` is serialized and deserialized
- **THEN** the resulting document has `Title == "My Lectern"`

#### Scenario: Missing title deserializes to default
- **WHEN** bytes from a prior codec version (which has no title field) are deserialized
- **THEN** the resulting document has `Title == "Lectern"`

#### Scenario: Empty title is not stored — default is used
- **WHEN** a document is saved with a title that is empty after trimming
- **THEN** the persisted title is `"Lectern"`, not an empty string

### Requirement: Serialization round-trip preserves identifiers, assignment, and kind-specific fields
The system's document serialization SHALL preserve the document's `DocId`, each block's `TaskId`,
each block's assignment field, and each block's kind-specific fields (the tracker
`TargetItemCode`/`TargetQuantity`/`CurrentQuantity` and the link `LinkTarget`), in addition to the
fields already preserved (kind, text, completed flag, depth). The accepted format-version window
and how prior versions are read (progressively, supplying documented defaults for absent fields)
is specified by the `codec-migration` capability; this requirement covers only which fields
survive a current-format round-trip.

#### Scenario: Round-trip preserves identifiers, assignment, and kind-specific fields
- **WHEN** a document containing tasks, a tracker, and a link is serialized in the current format
  and then deserialized
- **THEN** the resulting document has the same `DocId`, each block has the same `TaskId` and
  assignment field, and each tracker/link block has the same kind-specific fields
  (`TargetItemCode`, `TargetQuantity`, `CurrentQuantity`, `LinkTarget`) as the original

### Requirement: Add a tracker task
The system SHALL allow adding a tracker block for a given target item code and target quantity to
the document. The stored block SHALL have kind `Tracker`, `CurrentQuantity` 0, and `TargetQuantity`
clamped to at least 1. Adding a tracker SHALL follow the same ordering and identifier rules as any
other block (appended in order, assigned a distinct `TaskId`).

#### Scenario: Add a tracker with a target
- **WHEN** a tracker for `game:ingot-copper` with target quantity 5 is added to a document
- **THEN** the document gains one `Tracker` block with `TargetItemCode` `game:ingot-copper`,
  `TargetQuantity` 5, `CurrentQuantity` 0, and a distinct `TaskId`

#### Scenario: Setting tracker progress clamps to the target range
- **WHEN** a tracker's current quantity is set to a value below 0 or above its target
- **THEN** the stored `CurrentQuantity` is clamped into `[0, TargetQuantity]` and the operation
  reports success

### Requirement: Add a link task
The system SHALL allow adding a link block for a given reference target to the document. The stored
block SHALL have kind `Link`, carry the supplied `LinkTarget`, and have no tracker quantity fields.
Adding a link SHALL follow the same ordering and identifier rules as any other block.

#### Scenario: Add a link with a reference
- **WHEN** a link referencing the Handbook page for `game:ingot-copper` is added to a document
- **THEN** the document gains one `Link` block with `LinkTarget` `game:ingot-copper` and a distinct
  `TaskId`

