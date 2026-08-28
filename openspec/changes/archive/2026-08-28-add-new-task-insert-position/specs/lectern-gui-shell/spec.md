## ADDED Requirements

### Requirement: Footer Add uses New Task Insert
When the player adds a Standard Task or a Note from the editor footer add control, the new empty
row SHALL be inserted at the player's **New Task Insert** edge (`Top` → index 0, `Bottom` → append)
and SHALL receive focus. Enter (insert-below) is unchanged.

#### Scenario: Add task at top
- **WHEN** New Task Insert is Top and the player uses the add control to add a Standard Task
- **THEN** the new empty task is at the top of the list and focused

#### Scenario: Add note at bottom
- **WHEN** New Task Insert is Bottom and the player uses the add control to add a Note
- **THEN** the new empty note is appended and focused
