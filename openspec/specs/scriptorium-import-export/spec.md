# scriptorium-import-export Specification

## Purpose
TBD - created by archiving change add-scriptorium-import-export. Update Purpose after archive.
## Requirements
### Requirement: Export a document to the clipboard as JSON or TSV
The Transcribe view SHALL provide two export actions on the Import/Export section — **Copy as JSON** and
**Copy as TSV** — that serialize the document held by the section's slot and place the text on the OS
clipboard. JSON SHALL be a versioned, human-readable, lossless representation of the document. TSV SHALL be
a tab-separated table whose fixed columns are `Type · Done · Text · Special · Count · Depth`, legible and
pasteable into a spreadsheet, with the document title carried as a leading `title`-type row. A block's
sequence SHALL be its row position (there is no order column). Neither export SHALL include per-player pin
state, assignment, or live tracker counts. On a successful export the section's slot SHALL play the stamp
flourish with the imprint **EXPORTED**.

#### Scenario: Export a document as JSON
- **WHEN** a Scribe item with tasks is in the Import/Export slot and the player clicks Copy as JSON
- **THEN** the OS clipboard holds a versioned, indented JSON document containing every task's kind, text,
  done state, depth, and any tracker/link reference
- **AND** the clipboard JSON contains no TaskId, DocId, assignment, or live tracker count
- **AND** the slot plays the stamp flourish imprinted EXPORTED

#### Scenario: Export a document as TSV
- **WHEN** a Scribe item with tasks is in the Import/Export slot and the player clicks Copy as TSV
- **THEN** the OS clipboard holds a tab-separated table with a header row, a leading `title` row carrying
  the document title, and one row per block, columns `Type · Done · Text · Special · Count · Depth`
- **AND** pasting it into Excel or Google Sheets lays the data out into those columns
- **AND** the slot plays the stamp flourish imprinted EXPORTED

#### Scenario: Nothing to export
- **WHEN** the Import/Export slot is empty
- **THEN** the export actions are unavailable (disabled) and place nothing on the clipboard

### Requirement: Import a document from the clipboard
The Transcribe view SHALL provide an **Import** action that reads the OS clipboard, auto-detects the format
(JSON when the trimmed payload begins with `{`, otherwise TSV), and applies the parsed document onto the
Scribe item in the Import/Export slot. Import SHALL respect the copy-mode radio: **Overwrite** replaces the
target's tasks, **Append** adds the imported tasks after the existing ones. Import SHALL be
server-authoritative and SHALL mint a fresh identity for every imported task, so no import can create or
resurrect a pin. On a successful import the slot SHALL play the stamp flourish with the imprint **IMPORTED**.

#### Scenario: Import JSON onto a target
- **WHEN** the clipboard holds a valid Scribe JSON document and the player clicks Import in Overwrite mode
- **THEN** the target item's tasks are replaced by the imported tasks with their kinds, text, done state,
  and depth reconstructed
- **AND** every imported task is unpinned
- **AND** the slot plays the stamp flourish imprinted IMPORTED

#### Scenario: Import TSV in Append mode
- **WHEN** the clipboard holds a valid TSV table and the player clicks Import in Append mode
- **THEN** the imported rows are added after the target's existing tasks, nothing is deleted, and the
  imported tasks are unpinned

#### Scenario: Auto-detect the clipboard format
- **WHEN** the player clicks Import
- **THEN** a payload beginning with `{` is parsed as JSON and any other non-empty payload is parsed as TSV,
  without the player choosing a format

#### Scenario: Invalid clipboard content
- **WHEN** the clipboard is empty or holds text that parses as neither a Scribe JSON document nor a TSV table
- **THEN** the import performs no change to the target and surfaces a message that the clipboard is not a
  valid Scribe export

### Requirement: TSV round-trips task kinds with best-effort reconstruction
The TSV `Type` column SHALL name each block's kind (`title`, `note`, `task`, `tracker`, `link`, extensible
to future kinds such as `map`/`craft`). The `Special` column SHALL carry the kind's machine reference as a
per-kind comma-separated payload (item code for a tracker, link target for a link, and — for future
multi-field kinds — a packed payload such as `x,y,z,icon,color` for a map), and the `Count` column SHALL
carry the numeric modifier (target quantity for a tracker). The `Depth` column SHALL carry an integer
nesting level as loose visual grouping only, with no parent relationship implied. On import, an item-bound
row whose reference resolves to a real game target SHALL be reconstructed as that typed task; a row whose
reference is blank, invalid, or unknown SHALL be imported as a plain Task carrying the row's text. A single
unparseable or unresolved row SHALL NOT abort the import; the number of degraded rows SHALL be reported to
the player.

#### Scenario: A tracker row round-trips
- **WHEN** a TSV row has type `tracker`, a Special item code that resolves to a real item, and a Count
- **THEN** it is imported as a Tracker task for that item and quantity

#### Scenario: An unknown item degrades to a plain task
- **WHEN** a TSV row has type `tracker` but a Special code that does not resolve to any item
- **THEN** it is imported as a plain Task carrying the row's text, and the degradation is counted in the
  import result message

#### Scenario: The document title round-trips as a title row
- **WHEN** an exported TSV carries a leading row with type `title` and the document title in its Text column
- **THEN** importing it sets the target document's title from that row and creates no task block for it
- **AND** a TSV with no `title` row leaves the target's existing title unchanged

#### Scenario: Nesting depth round-trips as the Depth column
- **WHEN** a TSV row has a Depth value greater than 0 (e.g. a tracker under a craft)
- **THEN** the imported block's depth reflects that integer, with no parent link implied, and a child row is
  still a valid standalone task if reordered or separated from its group

#### Scenario: A multi-field kind packs into the Special column
- **WHEN** a TSV row's Special holds a comma-separated payload for its kind (e.g. `x,y,z,icon,color`)
- **THEN** the kind parses its own sub-fields positionally from Special, defaulting any missing trailing
  sub-field, without any additional columns

#### Scenario: Unknown trailing columns are tolerated
- **WHEN** a TSV table produced by a newer version carries extra trailing columns
- **THEN** import reads the columns it understands, ignores the unknown ones, and does not fail

### Requirement: Export and import are safe against injection
TSV export SHALL escape any field containing a tab, carriage return, line feed, or leading/trailing space so
the table structure round-trips, and SHALL neutralize spreadsheet formula injection by defanging any field
whose first character is a formula lead (`=`, `+`, `-`, `@`). Import SHALL treat all imported text as literal
content — never interpreted as Vintage Story rich-text markup, hotkeys, or handbook links — and SHALL strip
the export defang so text round-trips unchanged. Both codecs SHALL enforce the existing document length and
count caps.

#### Scenario: A formula-like cell is defanged on export
- **WHEN** a task's text begins with `=` (or `+`, `-`, `@`)
- **THEN** the exported TSV cell is prefixed so a spreadsheet will not execute it as a formula
- **AND** importing that cell restores the original text without the defang prefix

#### Scenario: Text with tabs and newlines round-trips
- **WHEN** a task's text contains a tab or an interior line break
- **THEN** it is exported as a quoted field and imported back identical

#### Scenario: Imported markup is inert
- **WHEN** imported text contains Vintage Story rich-text tags (e.g. `<a href=...>` or `<hotkey>`)
- **THEN** the text is stored and displayed literally, with no tag interpreted

### Requirement: Import never creates or resurrects a pin
An import SHALL produce only unpinned tasks. Because pins are stored per-player keyed by document and task
identity, and import mints a fresh identity for every imported task and document, an import into the same
or a different world SHALL NOT cause any task to appear pinned.

#### Scenario: Importing into a world that had pins
- **WHEN** a document is imported into a world where the player previously pinned tasks
- **THEN** none of the imported tasks are pinned, and the player's existing unrelated pins are unaffected

