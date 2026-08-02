## MODIFIED Requirements

### Requirement: Pin Tab rows are editable by default with complete, edit-text, delete, unpin, and reorder

Each row in the Pin Tab SHALL be editable by default (not behind a separate "edit" mode), reusing the
editor view's row rendering but sourced from the player's pin set rather than the current document. Each
row SHALL provide: a control to complete the task, a directly-editable text field for the task's text, a
control to delete the underlying task, a control to unpin (remove the pin without deleting the task), and
an affordance to reorder the pin within the player's list. Each control SHALL act on the pin by its stable
identity `(DocId, TaskId)` and SHALL drive the server-authoritative pin operations lock-free
(complete / edit-text / delete / unpin / reorder), never through the document's edit lock and never by
mutating pins locally without the server round-trip. Tab / Shift+Tab traversal within the Pin Tab SHALL
visit only the rows' editable text fields, in row order; it SHALL NOT stop focus on a row's completion
checkbox (the checkbox remains operable by mouse click).

#### Scenario: A row exposes every edit action
- **WHEN** a player views any pin row in the Pin Tab
- **THEN** the row offers complete, a directly-editable text field, delete, unpin, and reorder affordances
  for that pin

#### Scenario: Editing a row's text drives the identity-addressed edit
- **WHEN** a player edits a row's text and commits it
- **THEN** the Pin Tab sends the edit addressed by that pin's `(DocId, TaskId)` and the row reflects the
  server-synced result

#### Scenario: Tab traversal skips the row checkbox
- **WHEN** the player presses Tab or Shift+Tab to move between rows in the Pin Tab
- **THEN** focus moves directly from one row's editable text field to an adjacent row's editable text
  field, never landing on a row's completion checkbox

#### Scenario: Unpin removes only the pin
- **WHEN** a player uses a row's unpin control
- **THEN** that player's pin is removed without deleting the underlying task, distinct from the delete
  control which removes the task itself
