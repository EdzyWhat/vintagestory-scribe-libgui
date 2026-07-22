## MODIFIED Requirements

### Requirement: Row icons are hover-conditional
A row's per-row icon controls (at minimum the delete icon and the pin-toggle icon) SHALL
be visually hidden unless the mouse is currently positioned over that row, rather than
always rendered. These controls SHALL be drawn as a hover **overlay** floating over the
right end of the row's text — they SHALL NOT reserve a permanent gutter that narrows the
text column. When visible, each control SHALL draw an opaque background behind its icon so
that it occludes the portion of text it covers (rather than the text showing through), and
its chrome SHALL be a minimal thin outline rather than the engine's default filled button
background. Each control SHALL be sized to a single text line's height and top-aligned on
the row, so on a multi-line row it does not stretch to the full row height.

#### Scenario: An icon appears only while hovering its row
- **WHEN** the mouse moves over a task or note row
- **THEN** that row's icon controls become visible, and become hidden again once the
  mouse moves off that row

#### Scenario: Hovering does not disturb active typing
- **WHEN** the mouse moves over a row while the player is actively typing in a different
  row's text field
- **THEN** the typing field's focus and caret position are unaffected by the hover-driven
  visibility change

#### Scenario: The text uses full width and the icons overlay it on hover
- **WHEN** a row's text is long enough to reach the right edge of the row
- **THEN** the text is laid out to the full row width when no icon is shown, and when the
  hover icons appear they float over the text's right end, their opaque background hiding
  the text directly beneath them

#### Scenario: Icons sit on the first line of a multi-line row
- **WHEN** a row wraps onto multiple lines and its hover icons are shown
- **THEN** the icons are sized to a single line's height and sit at the top of the row,
  not stretched to span the full multi-line height

### Requirement: Editor rows reserve a drag-handle affordance column
Each editor-view row SHALL reserve a drag-handle (grip) affordance column at the row's far
left — to the left of the checkbox — and render a grip control in it, so the row exposes a
visible grab point for reordering. The read view SHALL reserve a column of the same width
(drawing no grip control in it) so that the checkbox and text sit at the same horizontal
position in both views, with no shift when toggling between them. The grip's width, like the
row's other affordance columns, SHALL scale with the text-size preference rather than staying
a fixed size. Providing the actual drag-to-reorder *interaction feedback* (a lift-ghost,
insertion indicator, or drop-settle animation) is out of scope for this requirement — this
requires only that the column and its grip control exist.

#### Scenario: Editor rows show a far-left grip control
- **WHEN** a row is composed in the editor view
- **THEN** a drag-handle grip control is present in a reserved column at the row's far left,
  positioned to the left of the checkbox

#### Scenario: Read view reserves the column without a grip
- **WHEN** a row is composed in the read view
- **THEN** no drag-handle grip control is drawn, but the same far-left column width is
  reserved so the checkbox and text are at the same X position as in the editor view

#### Scenario: The grip column scales with text size
- **WHEN** the text-size preference is changed
- **THEN** the reserved grip column's width scales with it, consistent with the row's other
  affordance columns and checkbox
