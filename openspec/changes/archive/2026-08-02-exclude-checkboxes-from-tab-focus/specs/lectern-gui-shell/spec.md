## MODIFIED Requirements

### Requirement: Editor rows navigate and commit by keyboard
The editor SHALL let the player move between rows and add rows from the keyboard while editing. Pressing
Tab (without Shift) SHALL commit the current row's edit and move focus to the next row WITHOUT inserting a
tab glyph; pressing Shift+Tab SHALL commit and move focus to the previous row. Tab / Shift+Tab traversal
SHALL visit only the rows' editable text fields, in row order; it SHALL NOT stop focus on a row's
completion checkbox (the checkbox remains operable by mouse click). Pressing Enter (without
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

#### Scenario: Tab traversal skips the row checkbox
- **WHEN** the player presses Tab or Shift+Tab to move between rows in the editor
- **THEN** focus moves directly from one row's editable text field to an adjacent row's editable text
  field, never landing on a row's completion checkbox, so a single Tab advances one row

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
