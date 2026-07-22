## ADDED Requirements

### Requirement: Editor rows reserve a drag-handle affordance column
Each editor-view row SHALL reserve a drag-handle (grip) affordance column and render a grip control
in it, so the row exposes a visible grab point for reordering. This column SHALL be present in the
editor view only (the read view exposes no per-row controls beyond the checkbox). The grip's width,
like the row's other affordance columns, SHALL scale with the text-size preference rather than
staying a fixed size. Providing the actual drag-to-reorder *interaction feedback* (a lift-ghost,
insertion indicator, or drop-settle animation) is out of scope for this requirement — this requires
only that the column and its grip control exist.

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
