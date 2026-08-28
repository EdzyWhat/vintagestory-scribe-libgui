## MODIFIED Requirements

### Requirement: Read and editor views share a single row-list width
The lectern's row list SHALL be a single consistent width across both the read view and the
editor view. Switching between views on the same lectern SHALL NOT change the row-list width.

In addition, a task row SHALL occupy the same vertical space in the read view and the editor
view: for a single-line task, the read-view row and the editor-view row SHALL have identical
rendered height, and each task SHALL remain at the same vertical position when the player
switches views on the same lectern. This parity SHALL hold for every selectable task-text font,
not only Caudex: both views SHALL reserve Caudex's Skia line-box at the current window font
scale (see `task-font-metrics`). This parity SHALL be achieved by unifying the row font
size, vertical alignment, per-row padding, and inter-row spacing between the two views. The
read-view row SHALL NOT draw a text-field border, while the editor-view row's field border
(drawn inside its existing internal padding) SHALL NOT change the row's height. Multi-line
rows are best-effort: they need not be pixel-identical when the read and editor wrap widths
or field chrome differ.

#### Scenario: Row-list width is identical in both views
- **WHEN** the player switches between read and editor view on the same lectern
- **THEN** the row list occupies the same width in both views, with no visible reflow or
  resize of the list column

#### Scenario: A single-line task keeps its position across a view switch
- **WHEN** the player switches between read and editor view on a lectern whose tasks each fit
  on a single line
- **THEN** each task's row occupies the same vertical height and the same vertical position in
  both views, so no task visibly jumps or shifts when the view changes

#### Scenario: Read/Edit height parity holds for a non-Caudex task font
- **WHEN** the player has selected a non-Caudex task font (for example Scapholene or La Belle
  Aurore)
- **AND** the player switches between read and editor view on a lectern whose tasks each fit
  on a single line
- **THEN** each task's row occupies the same vertical height and the same vertical position in
  both views, matching the Caudex-tuned line-box at that window font scale

#### Scenario: Read-view rows have no border while matching the editor field's box
- **WHEN** a task row is shown in the read view
- **THEN** it draws no text-field border
- **AND** its text is inset vertically and horizontally to match the editor field's internal
  padding, so the text's top edge and left edge align with the editor field's text across a
  view switch
