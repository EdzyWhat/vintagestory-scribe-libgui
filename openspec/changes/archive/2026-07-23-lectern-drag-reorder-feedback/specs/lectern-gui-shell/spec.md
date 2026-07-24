## ADDED Requirements

### Requirement: Drag-reorder shows live visual feedback
While a task/note row is being drag-reordered in the editor view, the lectern GUI SHALL show
continuous visual feedback: a lifted representation of the dragged row that follows the cursor,
and an indicator of the exact position the row will occupy if released at the current cursor
location. This feedback SHALL update continuously as the cursor moves, and the actual reorder
SHALL still occur only on release.

#### Scenario: Dragged row lifts and follows the cursor
- **WHEN** the player presses and holds a row's drag handle and moves the cursor within the
  editor view's row list
- **THEN** a semi-transparent, non-interactive representation of that row's content (its text,
  and a checkbox glyph for a task) follows the cursor
- **AND** the source row remains in its original position but is visually distinguished (e.g.
  dimmed) to read as "picked up"

#### Scenario: Insertion indicator tracks the drop target
- **WHEN** the player, while dragging, moves the cursor over different rows in the list
- **THEN** an insertion indicator is drawn at the boundary of the slot where the dragged row
  would land, and it updates continuously to match whichever slot the cursor is currently over
- **AND** when the cursor is past the last row, the indicator shows the drop will land at the
  end of the list

#### Scenario: Drop settles into place before the reorder commits
- **WHEN** the player releases the drag over a target slot different from the row's origin
- **THEN** the lifted representation eases (a short animated transition) into the target slot
- **AND** the row list is then reordered to reflect the new position and recomposed

#### Scenario: Dropping a row in its original position is a no-op
- **WHEN** the player releases the drag over the same slot the row started in
- **THEN** no reorder occurs and the feedback clears immediately without an easing transition

#### Scenario: Feedback clears if the drag is abandoned
- **WHEN** an in-progress drag ends because the dialog closes or the view mode switches
- **THEN** all drag feedback (lifted row, insertion indicator, any in-progress animation) is
  cleared and no reorder is applied
