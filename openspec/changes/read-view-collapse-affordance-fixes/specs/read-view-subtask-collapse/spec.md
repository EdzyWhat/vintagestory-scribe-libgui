## MODIFIED Requirements

### Requirement: A collapse toggle appears on every subtask-group parent
Any Read View row that is a depth-0 block immediately followed by a contiguous run of depth-1
blocks (its "owned run", per `task-subtasks`) SHALL show a small toggle control in the row's left
column — the same column position a grip/drag-handle would occupy if the row had one. A row with
no owned run SHALL show nothing in that column. The toggle SHALL render as a bare triangle/caret
glyph only, with no button border or background chrome around it.

#### Scenario: Toggle appears on a Quest Link with objectives
- **WHEN** a Quest Link block is immediately followed by one or more `QuestObjective` blocks
- **THEN** the Quest Link's row shows a collapse toggle in its left column, rendered as a bare
  caret with no button chrome

#### Scenario: Toggle appears on a Craft parent with generated trackers
- **WHEN** a Craft block is immediately followed by its auto-generated `Tracker` blocks
- **THEN** the Craft block's row shows a collapse toggle in its left column, rendered as a bare
  caret with no button chrome

#### Scenario: No toggle on a row without an owned run
- **WHEN** a `Task` row has no depth-1 blocks immediately beneath it
- **THEN** its row shows nothing in the left column
