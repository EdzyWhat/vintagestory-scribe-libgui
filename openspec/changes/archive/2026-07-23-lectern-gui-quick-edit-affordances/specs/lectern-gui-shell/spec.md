## ADDED Requirements

### Requirement: Task done state can be toggled from Read view
A task row in the lectern's read view SHALL render an interactive checkbox reflecting its
current done state, and activating it SHALL toggle that state without requiring the
player to switch to editor mode first.

#### Scenario: Toggling a task from read view
- **WHEN** a player viewing a lectern in read view clicks a task row's checkbox
- **THEN** the task's done state flips, the change is sent to the server and applied to
  the authoritative document, and every client currently viewing that lectern
  (read or editor view) sees the updated state

#### Scenario: Read view still never touches the editor lock
- **WHEN** a player toggles a task's done state from read view
- **THEN** no editor-lock acquisition, refusal, or release occurs as a result — the
  single-editor lock's state is unaffected by a read-view toggle

#### Scenario: Note rows have no toggle in read view
- **WHEN** a text-section (note) row is rendered in read view
- **THEN** no checkbox or toggle control is present for that row — only task rows expose
  this control

### Requirement: Read view and editor view render at the same dialog width
The lectern dialog's row-list width SHALL be identical between read view and editor
view, so switching between the two views does not visibly resize the dialog.

#### Scenario: Switching views does not change dialog width
- **WHEN** a player switches between read view and editor view on the same lectern
- **THEN** the dialog's width is unchanged across the switch

### Requirement: Per-row icon columns scale with the text-size preference
The drag-handle, pin-toggle, and delete icon columns in a task/note row SHALL scale in
width proportionally to the current text-size preference, in the same proportion the
row's checkbox and text already scale.

#### Scenario: Icon columns shrink at a smaller text size
- **WHEN** the player lowers the text-size preference
- **THEN** the drag-handle, pin-toggle, and delete icon columns render narrower,
  proportional to the new text-size value, freeing more width for the row's text

#### Scenario: Icon columns grow at a larger text size
- **WHEN** the player raises the text-size preference
- **THEN** the drag-handle, pin-toggle, and delete icon columns render wider,
  proportional to the new text-size value
