## ADDED Requirements

### Requirement: Document is an ordered sequence of blocks

The system SHALL represent a Scribe document as an ordered sequence of blocks, where each
block is either a **task** (text plus a completed flag) or a **text section** (freeform
text, no checkbox). Tasks and text sections MAY be interspersed in any order. Each block
SHALL carry a depth value (reserved for future sub-item nesting; 0 for now). This model
MUST NOT depend on the Vintage Story API.

#### Scenario: A new document is empty

- **WHEN** a new document is created
- **THEN** it has zero blocks

#### Scenario: Block order is preserved

- **WHEN** a task, then a text section, then another task are added
- **THEN** the document lists them in that exact order with their respective kinds

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
