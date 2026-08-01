## MODIFIED Requirements

### Requirement: Completing a pinned task from the HUD has a brief undoable window with animated feedback
When a player completes (checks off) a pinned task from the HUD, the system SHALL hold the completion for
a brief window before it takes effect on the server, during which the player MAY undo it by unchecking the
task; an undo within the window SHALL leave the task and its pin exactly as they were, with no completion
having been applied. All completion policies SHALL share the same window duration. The system SHALL give
animated feedback during the window that reflects the pending outcome: a completion under a policy that
removes the task or its pin SHALL fade the affected row's text **gradually and linearly from fully opaque
to fully transparent over the duration of the window** (a visible countdown to removal), and a completion
under a policy that keeps-and-sinks the task SHALL visibly settle the row toward its sunk position. The
task's checkbox SHALL remain operable throughout the window so the undo is always available. When a
sink-policy completion's window elapses, the completed task SHALL move to the end of the player's HUD pin
list and SHALL retain that resting position for the remainder of the session even if the player later
unchecks it (unchecking SHALL NOT return it to its former position).

#### Scenario: Undo within the window applies no completion
- **WHEN** a player checks off a pinned task on the HUD and unchecks it before the window elapses
- **THEN** no completion is applied — the task's done-state and the player's pin are unchanged, and no
  removal or sink occurs

#### Scenario: Completion applies after the window
- **WHEN** a player checks off a pinned task on the HUD and does not undo before the window elapses
- **THEN** the completion is applied under the player's current policy (sink, keep, unpin, or delete)

#### Scenario: A removing completion fades gradually over the window
- **WHEN** a completion under a policy that removes the task or its pin (unpin or delete) is pending within
  its window
- **THEN** the row's text opacity decreases linearly from fully opaque at the moment it is checked to fully
  transparent as the window elapses, while its checkbox stays fully opaque and operable for undo

#### Scenario: A sunk task stays at the bottom after being unchecked
- **WHEN** a sink-policy completion's window has elapsed (the task has settled to the end of the list) and
  the player later unchecks that task
- **THEN** the task remains at the end of the HUD pin list for the rest of the session rather than
  returning to its former position
