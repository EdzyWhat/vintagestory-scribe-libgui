## MODIFIED Requirements

### Requirement: Editor rows reserve a drag-handle affordance column
Each editor-view row SHALL reserve a drag-handle (grip) affordance column and render a grip control
in it, so the row exposes a visible grab point for reordering. This column SHALL be present in the
editor view only (the read view exposes no per-row controls beyond the checkbox). The grip's width,
like the row's other affordance columns, SHALL scale with the text-size preference rather than
staying a fixed size. Dragging the grip SHALL reorder the row: while a drag is in progress the GUI
SHALL indicate the drag by transforming the grip glyphs rather than by washing the row backgrounds —
the grabbed (source) row's grip SHALL become a left-pointing indicator and that row SHALL be dimmed
to read as lifted, every non-participating row's grip SHALL be hidden, and the row the pointer is
currently over (the prospective drop) SHALL show a right-pointing indicator in its grip. Releasing
SHALL move the row to that position via the document's reorder path (a move that extracts the row and
reinserts it, not a swap). A drag released on the row's original position SHALL make no change. Drag
feedback SHALL NOT be drawn as a row-background wash, so it never collides with the pinned-row
highlight; a pinned row SHALL continue to show its pinned highlight even while a drag is in progress.
The grip column's reserved width SHALL NOT change when its glyph is hidden or swapped, so mid-drag
feedback does not reflow the row. This drag-feedback behavior SHALL apply equally to every
reorderable row surface (the editor view and the Pin Tab), which share the same drag mechanism.

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

#### Scenario: Dragging the grip shows arrow feedback on the grips
- **WHEN** the player presses a row's grip and moves the pointer over a different row
- **THEN** the grabbed row's grip shows a left-pointing indicator and that row is dimmed, every other
  non-target row's grip is hidden, and the row under the pointer shows a right-pointing indicator
  marking where the row will land, and on release the row moves to that position

#### Scenario: Drag feedback does not collide with the pinned wash
- **WHEN** the player drags a row over a pinned row
- **THEN** the pinned row keeps its pinned highlight and the drop position is shown by the
  right-pointing grip indicator, with no row-background drag wash drawn that could be mistaken for
  the pinned highlight

#### Scenario: Releasing in place is a no-op
- **WHEN** the player begins a grip drag and releases it on the row's original position
- **THEN** no reorder occurs and no edit is sent
