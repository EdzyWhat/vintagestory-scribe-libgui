## ADDED Requirements

### Requirement: Delete a block from the GUI
The lectern's GUI SHALL let the player delete a block (a task or a text section) from the
editor view, removing it from the document while preserving the order of the remaining blocks.
The deletion SHALL be applied through the existing server-authoritative edit path and persist
across reload.

#### Scenario: Delete a task in the editor
- **WHEN** the player activates a row's delete control in the editor view
- **THEN** that block is removed from the lectern's document, the remaining blocks keep their
  relative order, and the change is applied server-authoritatively and re-synced

#### Scenario: Deletion persists across reload
- **WHEN** a block is deleted, then the world is saved and reloaded
- **THEN** reopening that lectern shows the block still gone

#### Scenario: Deleting the last block leaves an empty document
- **WHEN** the player deletes the only remaining block
- **THEN** the document becomes empty and the editor shows its empty-state hint, with no error

### Requirement: Reorder blocks from the GUI
The lectern's editor view SHALL let the player reorder blocks by dragging a row to a new
position with the mouse. Dropping a row SHALL move it to the drop position, shifting the
intervening blocks, and SHALL be applied through the existing server-authoritative edit path
and persist across reload. A drag that is released on the row's original position SHALL be a
no-op.

#### Scenario: Drag a row to a new position
- **WHEN** the player presses on a row's drag handle, moves the pointer over a different row
  position, and releases
- **THEN** the dragged block moves to that position, the other blocks shift to accommodate it,
  and the new order is applied server-authoritatively and re-synced

#### Scenario: Reorder persists across reload
- **WHEN** blocks are reordered, then the world is saved and reloaded
- **THEN** reopening that lectern shows the blocks in the reordered sequence

#### Scenario: Dropping in place changes nothing
- **WHEN** the player begins a drag and releases without moving to a different position
- **THEN** the document order is unchanged and no edit is sent
