## ADDED Requirements

### Requirement: Document-level creates honor New Task Insert
The system SHALL insert a new document-level block at an index determined by the player's
**New Task Insert** setting (`Top` or `Bottom`). `Top` SHALL insert at index 0 (existing rows
shift down). `Bottom` SHALL append. Repeated `Top` inserts SHALL each land at index 0, so the
newest block is first.

This setting SHALL apply to:

- the editor footer Add control (Task and Note);
- Shift+right-click quick-add (empty Task);
- Handbook "Add to Scribe" (Link, Tracker, Craft, and guide-page Link).

A Crafting Task created this way SHALL place the Craft parent at that index; ingredient children
SHALL still occupy the contiguous depth-1 run immediately under that parent (the group moves
together).

Enter (insert a task directly beneath the focused row) SHALL NOT use this setting.

#### Scenario: Top insert from footer Add
- **WHEN** New Task Insert is Top and the player adds a Task from the footer
- **THEN** the new empty task is at index 0 and is focused

#### Scenario: Bottom insert from footer Add
- **WHEN** New Task Insert is Bottom and the player adds a Task from the footer
- **THEN** the new empty task is appended and is focused

#### Scenario: Quick-add follows the setting
- **WHEN** the player Shift+right-clicks a Scribe surface
- **THEN** a new empty task is inserted at the New Task Insert edge (Top → index 0, Bottom → append)
  and focused, subject to the existing document-full cap

#### Scenario: Handbook Craft lands as a group
- **WHEN** New Task Insert is Top and the player adds a Crafting Task from the Handbook
- **THEN** the Craft parent is at index 0 and its ingredient children sit immediately under it as
  depth-1 rows; the previous first row begins after that run

#### Scenario: Enter-below ignores the setting
- **WHEN** New Task Insert is Top and the player presses Enter on a non-empty mid-list row
- **THEN** the new empty task is inserted directly beneath that row, not at index 0

### Requirement: Missing setting defaults to Top
When the client settings JSON has no New Task Insert value, or the stored value is unknown, the
system SHALL treat the setting as `Top`.

#### Scenario: Fresh install uses Top
- **WHEN** a player has never saved New Task Insert
- **THEN** footer Add, quick-add, and Handbook creates insert at the top
