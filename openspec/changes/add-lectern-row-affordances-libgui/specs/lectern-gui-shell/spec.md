## MODIFIED Requirements

### Requirement: Editor rows reserve a drag-handle affordance column
Each editor-view row SHALL reserve a drag-handle (grip) affordance column and render a grip control
in it, so the row exposes a visible grab point for reordering. This column SHALL be present in the
editor view only (the read view exposes no per-row controls beyond the checkbox). The grip's width,
like the row's other affordance columns, SHALL scale with the text-size preference rather than
staying a fixed size. Dragging the grip SHALL reorder the row: while a drag is in progress the GUI
SHALL indicate the prospective drop position (for example, an insertion indicator or a highlighted
target), and releasing SHALL move the row to that position via the document's reorder path. A drag
released on the row's original position SHALL make no change.

#### Scenario: Editor rows show a grip control
- **WHEN** a row is composed in the editor view
- **THEN** a drag-handle grip control is present in a reserved column for that row

#### Scenario: Read view rows have no grip control
- **WHEN** a row is composed in the read view
- **THEN** no drag-handle grip control is present for that row

#### Scenario: The grip column scales with text size
- **WHEN** the text-size preference is changed
- **THEN** the reserved grip column's width scales with it, consistent with the row's other
  affordance columns and checkbox

#### Scenario: Dragging the grip reorders the row with drop feedback
- **WHEN** the player presses the grip and moves the pointer to a different row position
- **THEN** the GUI shows where the row would drop, and on release the row moves to that position

#### Scenario: Releasing in place is a no-op
- **WHEN** the player begins a grip drag and releases it on the row's original position
- **THEN** no reorder occurs and no edit is sent

## ADDED Requirements

### Requirement: Editor rows expose a working delete control
Each editor-view row SHALL provide a delete control that removes that block from the document
through the server-authoritative edit path. The control SHALL be a real action (not a reserved
column or a logging stub). Deleting the row the player is currently editing SHALL commit or
discard that row's in-progress edit safely (no crash, no orphaned focus on a removed row).

#### Scenario: Delete control removes the row
- **WHEN** the player activates a row's delete control
- **THEN** that block is removed from the document and the row disappears from the list

#### Scenario: Deleting the focused row does not break focus
- **WHEN** the player deletes the row that currently holds edit focus
- **THEN** the editor does not crash and focus is not left pointing at the removed row

### Requirement: Pinned tasks show a resting indicator
A pinned task SHALL be visually distinguishable at rest — without hovering the row — in both the
read view and the editor view, so a pin toggled via the (hover-conditional) pin control remains
legible after the mouse leaves the row. Unpinned tasks and text-section rows SHALL show no such
indicator.

#### Scenario: A pinned task reads as pinned without hovering
- **WHEN** a task is pinned and the mouse is not over its row
- **THEN** the row shows a resting indicator distinguishing it from unpinned rows, in both views

#### Scenario: Unpinning removes the resting indicator
- **WHEN** a pinned task is unpinned
- **THEN** the resting indicator is removed from that row
