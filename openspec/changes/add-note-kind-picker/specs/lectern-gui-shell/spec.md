## MODIFIED Requirements

### Requirement: New tasks are created empty
When the player adds a block in the editor view — via the footer add control (the kind
picker) or by committing a row with Enter (insert-below) — the new block SHALL be created
with empty text rather than seeded with a placeholder literal (e.g. "New task"). This applies
to both kinds the picker creates: a Standard Task and a Note are each created empty. The new
row SHALL be focused so the player can type into the empty field immediately, with no
boilerplate text to select and delete first. Enter (insert-below) SHALL continue to insert a
task, matching the surrounding task-editing flow.

#### Scenario: Add task creates an empty focused row
- **WHEN** the player uses the add control to add a Standard Task
- **THEN** a new task row is added with empty text and receives focus, and its text field contains
  no pre-filled placeholder characters

#### Scenario: Add note creates an empty focused row
- **WHEN** the player uses the add control to add a Note
- **THEN** a new text-section row (no checkbox) is added with empty text and receives focus, and its
  text field contains no pre-filled placeholder characters

#### Scenario: Enter inserts an empty task below
- **WHEN** the player presses Enter (without Shift) while editing a non-empty task row
- **THEN** the current row is committed and a new empty task is inserted directly beneath it and
  focused, containing no pre-filled placeholder characters

### Requirement: An empty task row is removed when it loses focus
While in the editor view, when a row whose text is empty or whitespace-only loses focus (by
clicking away, moving to another row, switching to the read view, or closing the dialog), the
editor SHALL remove that block from the document rather than persisting it, and SHALL move focus to
the row immediately above the removed row when one exists. This applies to any empty editor row —
a task **or** a note — whether just created and abandoned without typing, or an existing block whose
text the player cleared (e.g. with select-all then Delete) — so that abandoned empty rows never grow
the list and a cleared row can be removed from the keyboard alone. Removal SHALL be applied through
the existing lock-gated server edit path and SHALL NOT leave an empty task or note visible in the read
view or persisted across reload. (The Core document model still stores text verbatim for both kinds;
this removal is an editing-layer behavior, not a model invariant.)

#### Scenario: Abandoned empty new task is removed on blur
- **WHEN** the player adds a task, types nothing, and moves focus away from that empty row
- **THEN** the empty task is removed from the document and does not appear in the read view or
  after reload, and the list is not grown by the abandoned add

#### Scenario: Abandoned empty new note is removed on blur
- **WHEN** the player adds a note, types nothing, and moves focus away from that empty row
- **THEN** the empty note is removed from the document and does not appear in the read view or
  after reload, and the list is not grown by the abandoned add

#### Scenario: Clearing a task's text then blurring removes the row
- **WHEN** the player selects all of an existing task row's text, deletes it, and then moves focus
  away from the now-empty row
- **THEN** the task is removed from the document and focus moves to the row directly above it

#### Scenario: Clearing a note's text then blurring removes the row
- **WHEN** the player selects all of an existing note row's text, deletes it, and then moves focus
  away from the now-empty row
- **THEN** the note is removed from the document and focus moves to the row directly above it

#### Scenario: Focus moves to the row above
- **WHEN** an empty row that is not the first row is removed on losing focus
- **THEN** focus moves to the task/note row that was directly above the removed row

#### Scenario: Switching to read or closing does not persist an empty row
- **WHEN** a task or note row is empty and the player switches to the read view or closes the dialog
- **THEN** the empty row is removed rather than saved, and the read view / reloaded document shows
  no empty row
