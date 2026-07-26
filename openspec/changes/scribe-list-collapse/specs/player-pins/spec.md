## MODIFIED Requirements

### Requirement: Completing a pinned task from the HUD has a brief undoable window with animated feedback
When a player completes (checks off) a pinned task from the HUD, the system SHALL hold the completion for
a brief window before it takes effect on the server, during which the player MAY undo it by unchecking the
task; an undo within the window SHALL leave the task and its pin exactly as they were, with no completion
having been applied. All completion policies SHALL share the same window duration. The system SHALL give
animated feedback during the window that reflects the pending outcome: a completion under a policy that
removes the task or its pin SHALL visibly fade the affected row, and a completion under a policy that
keeps-and-sinks the task SHALL visibly settle the row toward its sunk position. The task's checkbox SHALL
remain operable throughout the window so the undo is always available. When the window elapses under a
policy that removes the task or its pin, the affected row SHALL collapse its height to zero — so the rows
below it move up smoothly to fill the space — and SHALL be removed from the HUD only after that collapse
completes, rather than disappearing in a single frame.

#### Scenario: Undo within the window applies no completion
- **WHEN** a player checks off a pinned task on the HUD and unchecks it before the window elapses
- **THEN** no completion is applied — the task's done-state and the player's pin are unchanged, and no
  removal or sink occurs

#### Scenario: Completion applies after the window
- **WHEN** a player checks off a pinned task on the HUD and does not undo before the window elapses
- **THEN** the completion is applied under the player's current policy (sink, keep, unpin, or delete)

#### Scenario: The window gives animated feedback
- **WHEN** a completion is pending within its window
- **THEN** the row animates to preview the outcome (a fade for unpin/delete, a settle toward the bottom
  for sink), while its checkbox stays operable for undo

#### Scenario: A removing completion collapses the row before it leaves
- **WHEN** the undoable window elapses for a completion under a policy that removes the task or its pin
  (unpin or delete)
- **THEN** the faded row's height collapses smoothly to zero and the rows below move up to meet it, and
  the row is removed from the HUD only after that collapse finishes

#### Scenario: A re-pin during or after a collapse is not left invisible
- **WHEN** a task's row is collapsing (or has just collapsed) on the HUD and that same task is pinned
  again before the HUD reconciles with the server
- **THEN** the task reappears in the HUD at full height, with no residual collapse hiding it
