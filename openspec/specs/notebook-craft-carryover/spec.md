# notebook-craft-carryover Specification

## Purpose
TBD - created by archiving change carry-notebook-doc-through-craft. Update Purpose after archive.
## Requirements
### Requirement: Clockmaker's Notebook craft carries over the source document

When a Clockmaker's Notebook is crafted from a Notebook via the grid recipe, the crafted
output SHALL copy the source Notebook input's `"scribeDocument"` ItemStack attribute onto the
output stack, preserving the document's `DocId`, title, tasks, and task completion state, so
the crafted Clockmaker's Notebook opens showing the same document the source Notebook held.
The copy SHALL happen server-side using the existing `ScribeDocumentAttributes` serialization
(the same key the Notebook and Lectern already use), with no new attribute key and no new
network message.

#### Scenario: Document identity and contents transfer

- **WHEN** a player crafts a Clockmaker's Notebook from a Notebook whose document has a title,
  several tasks, and some tasks marked done
- **THEN** the crafted Clockmaker's Notebook opens showing that same title, the same tasks in
  the same order, and the same done/undone state
- **AND** the crafted document's `DocId` equals the source Notebook document's `DocId`

#### Scenario: Fresh document when no source document is present

- **WHEN** a Clockmaker's Notebook is created by a craft whose inputs contain no Notebook with
  a stored document (for example a future recipe, or a creative/`giveitem` output)
- **THEN** the crafted Clockmaker's Notebook opens with an empty document carrying a fresh
  `DocId`, exactly as an ungained Notebook does today

### Requirement: Clockmaker's Notebook craft carries over the History chronicle

The crafted Clockmaker's Notebook SHALL copy the source Notebook input's `"scribeHistory"`
attribute onto the output before the existing "Crafted" History entry is appended, so the new
entry is added on top of the carried-over chronicle rather than onto a blank one. When no
source history is present, the output's history SHALL contain only the newly appended
"Crafted" entry.

#### Scenario: Existing history is preserved and the craft is recorded

- **WHEN** a player crafts a Clockmaker's Notebook from a Notebook that already has History
  entries (crafted, picked up, deaths, etc.)
- **THEN** the crafted Clockmaker's Notebook's History shows all of the source Notebook's
  prior entries
- **AND** a new "Crafted" entry stamped with the current in-game date and the crafting
  player's name appears in that same History

#### Scenario: No prior history yields a single crafted entry

- **WHEN** a Clockmaker's Notebook is crafted from an input that carries no `"scribeHistory"`
  attribute
- **THEN** the crafted Clockmaker's Notebook's History contains exactly the one new "Crafted"
  entry

### Requirement: Notebook handbook documents the carryover

The Notebook's in-game handbook entry SHALL state that upgrading a Notebook into a Clockmaker's
Notebook keeps its tasks and History, alongside the existing mention of the upgrade path, so
the documentation matches the carryover behavior.

#### Scenario: Handbook mentions retained tasks and history

- **WHEN** a player opens the Notebook's handbook entry and reads the crafting section
- **THEN** the text notes that upgrading to the Clockmaker's Notebook keeps the notebook's
  tasks and History

