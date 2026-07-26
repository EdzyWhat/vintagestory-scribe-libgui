## ADDED Requirements

### Requirement: New tasks are created empty
When the player adds a task in the editor view — via the "Add task" control or by committing a row
with Enter (insert-below) — the new task SHALL be created with empty text rather than seeded with a
placeholder literal (e.g. "New task"). The new row SHALL be focused so the player can type into the
empty field immediately, with no boilerplate text to select and delete first.

#### Scenario: Add task creates an empty focused row
- **WHEN** the player activates the "Add task" control
- **THEN** a new task row is added with empty text and receives focus, and its text field contains
  no pre-filled placeholder characters

#### Scenario: Enter inserts an empty task below
- **WHEN** the player presses Enter (without Shift) while editing a non-empty task row
- **THEN** the current row is committed and a new empty task is inserted directly beneath it and
  focused, containing no pre-filled placeholder characters

### Requirement: An empty task row is removed when it loses focus
While in the editor view, when a task row whose text is empty or whitespace-only loses focus (by
clicking away, moving to another row, switching to the read view, or closing the dialog), the
editor SHALL remove that task from the document rather than persisting it, and SHALL move focus to
the row immediately above the removed row when one exists. This applies to any empty task row —
whether just created and abandoned without typing, or an existing task whose text the player
cleared (e.g. with select-all then Delete) — so that abandoned empty tasks never grow the list and
a cleared row can be removed from the keyboard alone. This SHALL apply only to task rows; a
freeform text section MAY be empty and SHALL NOT be auto-removed. Removal SHALL be applied through
the existing lock-gated server edit path and SHALL NOT leave an empty task visible in the read
view or persisted across reload.

#### Scenario: Abandoned empty new task is removed on blur
- **WHEN** the player adds a task, types nothing, and moves focus away from that empty row
- **THEN** the empty task is removed from the document and does not appear in the read view or
  after reload, and the list is not grown by the abandoned add

#### Scenario: Clearing a task's text then blurring removes the row
- **WHEN** the player selects all of an existing task row's text, deletes it, and then moves focus
  away from the now-empty row
- **THEN** the task is removed from the document and focus moves to the row directly above it

#### Scenario: Focus moves to the row above
- **WHEN** an empty task row that is not the first row is removed on losing focus
- **THEN** focus moves to the task/note row that was directly above the removed row

#### Scenario: Empty text section is not removed
- **WHEN** a freeform text section is empty and loses focus
- **THEN** the text section is retained (it is not a task and is not auto-removed)

#### Scenario: Switching to read or closing does not persist an empty task
- **WHEN** a task row is empty and the player switches to the read view or closes the dialog
- **THEN** the empty task is removed rather than saved, and the read view / reloaded document shows
  no empty task

## MODIFIED Requirements

### Requirement: Editor rows navigate and commit by keyboard
The editor SHALL let the player move between rows and add rows from the keyboard while editing. Pressing
Tab (without Shift) SHALL commit the current row's edit and move focus to the next row WITHOUT inserting a
tab glyph; pressing Shift+Tab SHALL commit and move focus to the previous row. Pressing Enter (without
Shift) SHALL commit the current row's edit and insert a NEW empty task directly beneath it, moving focus to
that new row, WITHOUT inserting a line break into the current row; pressing Shift+Enter SHALL instead insert
a hard line break into the row's text (growing the row) rather than committing. Pressing Enter on a row that
is itself empty or whitespace-only SHALL NOT stack a second empty task; it SHALL be a no-op on the row set.
Committing an edit (by Tab, Shift+Tab, Enter, or losing focus) SHALL apply the change through the existing
lock-gated server edit path (`ScribeEditDocumentMessage`), server-authoritatively; except that committing a
task row whose text is empty or whitespace-only SHALL remove that task (see "An empty task row is removed
when it loses focus") rather than saving it. Pressing Esc SHALL commit the focused row (via the same
blur-commit path) and close the dialog — a fast panic-close, not an in-place revert. On commit, a
non-empty row's text SHALL be normalized by trimming trailing blank lines and trailing whitespace while
preserving interior newlines, and the read view SHALL render those interior newlines as hard line breaks.

#### Scenario: Tab commits and advances
- **WHEN** the player finishes typing in a row and presses Tab (without Shift)
- **THEN** the row's new text is committed through the server edit path and focus moves to the next row,
  and no tab glyph is inserted into the row's text

#### Scenario: Enter commits and inserts a new empty task below
- **WHEN** the player presses Enter (without Shift) while editing a non-empty row
- **THEN** the row's edit is committed, a new empty task is inserted directly beneath it, focus moves to
  that new empty task, and no line break is inserted into the original row's text

#### Scenario: Enter on an empty row does not stack another empty task
- **WHEN** the player presses Enter (without Shift) while the focused task row is itself empty or
  whitespace-only
- **THEN** no additional empty task is inserted (the row set is unchanged)

#### Scenario: Shift+Enter inserts a hard line break
- **WHEN** the player presses Shift+Enter while editing a row
- **THEN** a line break is inserted at the caret, the row's height grows to fit the new line, and focus
  stays in the row (no commit, no new row)

#### Scenario: Shift+Tab commits and retreats
- **WHEN** the player presses Shift+Tab while editing a row
- **THEN** the row's edit is committed and focus moves to the previous row

#### Scenario: Committing an empty task removes it
- **WHEN** the player commits a task row (by Tab, Shift+Tab, Enter, losing focus, or closing) whose text
  is empty or whitespace-only
- **THEN** the task is removed from the document rather than saved, and focus moves to the row above when
  one exists

#### Scenario: Esc commits and closes
- **WHEN** the player presses Esc while editing a row
- **THEN** the focused row is committed via the blur-commit path (a non-empty row is saved and normalized;
  an empty task row is removed) and the dialog closes

#### Scenario: Committed text has trailing blank lines trimmed
- **WHEN** the player commits a non-empty row whose text ends in one or more blank lines or trailing
  whitespace
- **THEN** the committed text has its trailing blank lines and whitespace removed, while any interior
  newlines between text are preserved
