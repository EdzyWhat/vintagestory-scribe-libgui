## MODIFIED Requirements

### Requirement: Row icons are hover-conditional
A row's per-row icon controls (at minimum the delete icon and the pin-toggle icon) SHALL
be visually hidden unless the mouse is currently positioned over that row, rather than
always rendered. As an exception, a task whose pinned flag is set SHALL carry an
always-visible indicator of its pinned state that does not depend on hover, so a pinned
task is distinguishable from an unpinned one at rest in both the read and editor views.

#### Scenario: An icon appears only while hovering its row
- **WHEN** the mouse moves over a task or note row
- **THEN** that row's icon controls become visible, and become hidden again once the
  mouse moves off that row

#### Scenario: Hovering does not disturb active typing
- **WHEN** the mouse moves over a row while the player is actively typing in a different
  row's text field
- **THEN** the typing field's focus and caret position are unaffected by the hover-driven
  visibility change

#### Scenario: A pinned task shows an indicator without hovering
- **WHEN** a task with its pinned flag set is composed and the mouse is not over its row
- **THEN** a persistent indicator of the pinned state is visible for that row, in both the
  read and editor views

#### Scenario: An unpinned task shows no resting indicator
- **WHEN** a task with its pinned flag unset is composed and the mouse is not over its row
- **THEN** no pinned indicator is shown for that row

### Requirement: Task rows expose a pin-toggle affordance
Each task row in the editor view SHALL provide a control that toggles the task's pinned
flag. Text-section rows SHALL NOT expose this control. Activating the control SHALL mutate
the persisted pinned flag (not merely a transient widget state), and the change SHALL be
saved and synchronized server-authoritatively so it survives a recompose, a reload, and is
reflected on other clients viewing the same lectern.

#### Scenario: Toggling pin from the GUI persists
- **WHEN** the player activates a task row's pin-toggle control
- **THEN** the task's persisted pinned flag flips, the control's visual state reflects the
  new value, and the new state survives a subsequent recompose (it is not reverted by
  re-seeding from the model)

#### Scenario: Toggling pin syncs across clients
- **WHEN** one player toggles a task's pin on a lectern
- **THEN** another client viewing that same lectern sees the pinned state update

#### Scenario: Pinned state survives a reload
- **WHEN** a task is pinned and the world is saved and reloaded
- **THEN** the task is still pinned

#### Scenario: Text sections have no pin control
- **WHEN** a text-section row is composed
- **THEN** no pin-toggle control is present for that row

### Requirement: Editor rows reserve a drag-handle affordance column
Each editor-view row SHALL reserve a drag-handle (grip) affordance column and render a grip control
in it, so the row exposes a visible grab point for reordering. This column SHALL be present in the
editor view only (the read view exposes no per-row controls beyond the checkbox). The grip's width,
like the row's other affordance columns, SHALL scale with the text-size preference rather than
staying a fixed size. The grip control SHALL render as a bare icon with no button chrome (no filled
background and no outline), visually distinct from the chromed pin/delete buttons, and SHALL be sized
so its glyph is at least as tall as the row's checkbox. Providing the actual drag-to-reorder
*interaction feedback* (a lift-ghost, insertion indicator, or drop-settle animation) is out of scope
for this requirement — this requires only that the column and its grip control exist.

#### Scenario: Editor rows show a grip control
- **WHEN** a row is composed in the editor view
- **THEN** a drag-handle grip control is present in a reserved column for that row

#### Scenario: The grip renders without button chrome
- **WHEN** a grip control is composed
- **THEN** it draws only its icon, with no filled background and no outline, and its glyph
  is at least as tall as the row's checkbox

#### Scenario: Read view rows have no grip control
- **WHEN** a row is composed in the read view
- **THEN** no drag-handle grip control is present for that row

#### Scenario: The grip column scales with text size
- **WHEN** the text-size preference is changed
- **THEN** the reserved grip column's width scales with it, consistent with the row's other
  affordance columns and checkbox

## ADDED Requirements

### Requirement: Pin and delete render as one grouped control
The editor view's pin and delete affordances SHALL render as a single grouped control — an
abutted pair sharing one outer outline with a thin divider drawn between the two icons —
rather than two separately-outlined buttons. Hit-testing SHALL remain per-icon so that a
click on the pin half toggles the pin and a click on the delete half deletes the row.

#### Scenario: Pin and delete appear as a divided group
- **WHEN** a task row's affordances are shown on hover
- **THEN** the pin and delete icons appear within one grouped outline with a divider
  between them, not as two independently outlined buttons

#### Scenario: Each half still routes to its own action
- **WHEN** the player clicks the pin half of the group, then the delete half
- **THEN** the pin click toggles the pin and the delete click deletes the row, each
  routing to its own action

### Requirement: Affordance buttons show a pressed state
While a pin or delete button is held down (mouse button pressed over the button), the GUI
SHALL show a transient pressed/depressed visual state — a low-opacity light overlay clipped
to the button — that clears when the button is released or the pointer leaves it. This gives
a click a visible acknowledgement.

#### Scenario: Holding a button shows the pressed overlay
- **WHEN** the player presses and holds the mouse button over a pin or delete button
- **THEN** a pressed-state overlay is shown on that button

#### Scenario: Releasing clears the pressed overlay
- **WHEN** the player releases the mouse button, or moves the pointer off the button
- **THEN** the pressed-state overlay is removed

### Requirement: Pin and delete buttons are square with a minimum size
The pin and delete buttons SHALL be square (equal width and height) and SHALL scale with the
text-size preference, but SHALL NOT shrink below a configured minimum on-screen size, so they
remain legible at the smallest text-size setting. Their icons SHALL be sized consistently so
the two buttons match.

#### Scenario: Buttons stay square across the text-size range
- **WHEN** the text-size preference is swept from minimum to maximum
- **THEN** the pin and delete buttons remain square and equally sized at every step

#### Scenario: Buttons do not shrink below the minimum
- **WHEN** the text-size preference is at its minimum
- **THEN** the pin and delete buttons are no smaller than the configured minimum size and
  remain legible, with no rendering crash

### Requirement: Row ruling has no internal padding
The row ruling SHALL be drawn as the line alone, with no internal top or bottom padding
band separating it from the row content, so the line sits directly beneath the content. The
focused input's symmetric top/bottom margin against the content SHALL be preserved (the
highlight SHALL NOT butt directly against the ruling). The padding amount SHALL remain a
configurable value so the spacing can be re-tuned.

#### Scenario: The ruling hugs the row content
- **WHEN** a row is composed
- **THEN** the ruling line is drawn directly beneath the row content with no internal
  padding band above or below it

#### Scenario: The focused input keeps its margin
- **WHEN** a row is focused for editing after the ruling padding is removed
- **THEN** the focus highlight still has a small margin and does not butt directly against
  the ruling line
