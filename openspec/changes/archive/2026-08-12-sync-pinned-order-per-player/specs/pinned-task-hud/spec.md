## MODIFIED Requirements

### Requirement: The HUD orders pins automatically, sinking completed tasks
The system SHALL present the HUD rows in a deterministic order derived from the single per-player
pin list: the player's pin order, with completed tasks ordered below not-completed tasks, using the
same ordering rule the Pin Tab uses (a stable partition of the shared pin list — not-completed pins
first in their pin-list order, then completed pins in theirs). The HUD SHALL NOT maintain any
HUD-only or session-only ordering overlay; its resting order SHALL be a pure function of the shared
per-player pin list and each pin's completion state, so the HUD and the Pin Tab render one and the
same order. When a task is completed from the HUD, the system SHALL keep its row briefly in place (a
short undo window in which the player can revert the completion) before re-ordering it to the bottom,
and SHALL visually de-emphasize a completed task's row (for example by muting its text). Because the
resting order is a pure function of completion state, un-completing a previously sunk task SHALL
return it to its prior position in the list. The HUD SHALL NOT offer manual reordering; manual
ordering is provided elsewhere (the Pin Tab).

#### Scenario: A completed task sinks to the bottom
- **WHEN** the player completes a pinned task from the HUD and its completion is retained (the pin is
  not removed by the completion policy)
- **THEN** the row is de-emphasized and, after a brief undo window, moves below the not-completed rows

#### Scenario: Completion can be undone within the window
- **WHEN** the player re-toggles a just-completed row's control within the undo window
- **THEN** the task returns to not-completed and to its prior position

#### Scenario: Un-completing a sunk task returns it to its prior position
- **WHEN** a completed pinned task has settled below the not-completed rows and the player later
  un-completes it (after the undo window has elapsed)
- **THEN** the task returns to its prior position among the not-completed pins (its persisted pin-list
  slot), rather than remaining at the bottom

#### Scenario: The HUD and Pin Tab render the same order
- **WHEN** the player views their pins on both the HUD and the Pin Tab, across pinning from multiple
  documents and across sessions
- **THEN** both surfaces render the pins in the same order, because both derive it from the one
  persisted per-player pin list with no HUD-only overlay
