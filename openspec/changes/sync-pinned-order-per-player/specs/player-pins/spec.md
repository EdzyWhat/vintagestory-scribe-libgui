## MODIFIED Requirements

### Requirement: The sink completion order is shown on the Pinned view, not only the HUD
When a player's completion policy is *sink*, the resting display order in which completed tasks sink
below not-completed ones SHALL be applied to the Lectern's Pinned view, not only to the pinned-task HUD.
The Pinned view and the HUD SHALL render one and the same order, both derived from the single
persisted per-player pin list with the same ordering rule (completed pins ordered below not-completed
pins, preserving pin-list order within each group). Neither surface SHALL apply a surface-only or
session-only ordering overlay on top of that shared order. Completing a pinned task under *sink* from
any surface SHALL therefore move that task toward the bottom of both the Pinned view and the HUD in
the same order, and un-completing it SHALL return it to its prior position on both.

#### Scenario: A sunk task appears at the bottom of the Pinned view
- **WHEN** a player whose policy is *sink* completes a pinned task
- **THEN** that task is shown below the not-completed pins in the Pinned view (matching the HUD's sink
  order), rather than staying in its prior position

#### Scenario: Pinned view and HUD agree on sink order
- **WHEN** the same player views their pins on both the HUD and the Pinned view after a *sink* completion
- **THEN** both surfaces show the completed task sunk below the not-completed pins in the same order

#### Scenario: The Pinned view and HUD stay in sync across documents and sessions
- **WHEN** a player pins tasks from several documents, reorders them on the Pin Tab, completes and
  un-completes some, and rejoins in a later session
- **THEN** the HUD and the Pin Tab render the pins in the same order at every point, because both read
  the one persisted per-player pin list and apply the same ordering rule with no divergent overlay

### Requirement: Completing a pinned task from the HUD has a brief undoable window with animated feedback
When a player completes (checks off) a pinned task from the HUD, the system SHALL hold the completion for
a brief window before it takes effect on the server, during which the player MAY undo it by unchecking the
task; an undo within the window SHALL leave the task and its pin exactly as they were, with no completion
having been applied. All completion policies SHALL share the same window duration. The system SHALL give
animated feedback during the window that reflects the pending outcome: a completion under a policy that
removes the task or its pin SHALL visibly fade the affected row, and a completion under a policy that
keeps-and-sinks the task SHALL visibly settle the row toward its sunk position. The task's checkbox SHALL
remain operable throughout the window so the undo is always available. During the window the just-checked
row SHALL be held in its current position (not yet sunk), which follows from the completion not yet being
applied on the server. When the window elapses under a policy that removes the task or its pin, the
affected row SHALL collapse its height to zero — so the rows below it move up smoothly to fill the space —
and SHALL be removed from the HUD only after that collapse completes, rather than disappearing in a single
frame. When the window elapses under a keeps-and-sinks policy, the task SHALL settle to its resting sunk
position (below the not-completed pins); the resting order SHALL be a pure function of completion state,
so un-completing that task after the window SHALL return it to its prior position rather than holding it
at the bottom.

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

#### Scenario: Un-completing a sunk task after the window returns it to its prior position
- **WHEN** a keeps-and-sinks completion's window has elapsed (the task has settled to the bottom) and the
  player later un-completes that task
- **THEN** the task returns to its prior position among the not-completed pins, rather than remaining at
  the bottom for the session

#### Scenario: A re-pin during or after a collapse is not left invisible
- **WHEN** a task's row is collapsing (or has just collapsed) on the HUD and that same task is pinned
  again before the HUD reconciles with the server
- **THEN** the task reappears in the HUD at full height, with no residual collapse hiding it
