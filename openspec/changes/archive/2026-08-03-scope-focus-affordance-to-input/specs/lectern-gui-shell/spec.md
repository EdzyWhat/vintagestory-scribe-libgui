## MODIFIED Requirements

### Requirement: Focus ring is scoped to the active field
When a text field (a task's text input or a note's text area) has input focus, the GUI
SHALL visually indicate focus on that field specifically, not on the row as a whole. This
SHALL hold on every editor path, including the tablet cuneiform path — the focus border,
fill, and corner treatment SHALL be drawn around the focused input element, never around
the whole row `Container`. When the focused row is also pinned, the focus indicator SHALL
remain visually distinct from the pinned-row wash (a smaller, differently-shaped input
highlight inside the row's pinned tint), so the two states are never the same shape.

#### Scenario: Only the focused field is highlighted
- **WHEN** the player clicks into a row's text field to edit it
- **THEN** a focus indicator appears around that field, and no other part of the row
  (its checkbox, icons, or drag handle) is highlighted as focused

#### Scenario: Focused input on a pinned cuneiform row stays distinct from the pinned wash
- **WHEN** the player focuses the text input of a pinned task row on the tablet (cuneiform)
  path, where the row already carries the pinned-row tint
- **THEN** the focus indicator is drawn only around the input element (not the whole row),
  so the input's focus highlight and the row's pinned wash read as two distinct shapes
  rather than one ambiguous whole-row fill
