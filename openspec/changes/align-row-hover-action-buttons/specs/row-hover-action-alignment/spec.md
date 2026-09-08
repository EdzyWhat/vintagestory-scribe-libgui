## Purpose

Keep hover-revealed row actions visually aligned with the first line of their associated content
across Scribe's document surfaces, themes, row kinds, and font treatments.

## ADDED Requirements

### Requirement: Row hover actions align to the row's first text line
Every hover-revealed Pin, Unpin, or Delete button on a Scribe row SHALL center its visible button
box vertically against that row's actual first text line. The alignment SHALL account for ordinary
task rows, item rows, and cuneiform item rows whose line height differs from Latin text.

#### Scenario: Read-view Pin aligns with row text
- **WHEN** the player hovers a pinnable row in Read view
- **THEN** the Pin button's visible box is vertically centered with the row's first text line

#### Scenario: Editor actions share one vertical center
- **WHEN** the player hovers a row in Editor view
- **THEN** the Delete and Pin buttons share the same vertical center as the row's first text line

#### Scenario: Pinned-tab actions share one vertical center
- **WHEN** the player hovers a row in the Pinned tab
- **THEN** the Delete and Unpin buttons share the same vertical center as the row's first text line

#### Scenario: Cuneiform item rows use their rendered line height
- **WHEN** a hover action appears on a cuneiform tablet item row
- **THEN** its vertical center follows the rendered cuneiform item-name line instead of a Latin text
  measurement

### Requirement: Alignment changes preserve row-action behavior
Changing vertical alignment SHALL preserve each button's size, horizontal position, glyph scale,
hover visibility, click target, and action result.

#### Scenario: Aligned action remains usable
- **WHEN** the player activates a repositioned Pin, Unpin, or Delete button
- **THEN** the same action occurs as before the alignment change
