## ADDED Requirements

### Requirement: Ingredient subtask counts redraw live when the parent target changes
When the player changes a `Craft` parent's target quantity from the editor's inline stepper, the
ingredient subtask rows SHALL visually redraw with their rescaled target counts immediately, within
the same editor view, WITHOUT requiring a view swap (edit↔read) or any other externally forced
redraw. The field the player is actively editing (the focused parent stepper) SHALL NOT be disrupted
— it keeps its caret/focus and continues stepping — while the unfocused ingredient steppers update
in place.

#### Scenario: Raising the parent target rescales the visible ingredient counts in place
- **WHEN** a Craft task is open in the editor and the player raises the parent's target quantity with
  the +/- stepper
- **THEN** each ingredient subtask row's displayed target count updates to its rescaled value in the
  same frame's redraw, with no view swap required

#### Scenario: The parent stepper is not disrupted by the child redraw
- **WHEN** the player steps the parent target repeatedly
- **THEN** the parent stepper retains focus and continues stepping smoothly while the child ingredient
  counts update beneath it
