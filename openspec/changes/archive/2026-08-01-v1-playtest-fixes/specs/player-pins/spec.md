## ADDED Requirements

### Requirement: The sink completion order is shown on the Pinned view, not only the HUD
When a player's completion policy is *sink*, the resting display order in which completed tasks sink
below not-completed ones SHALL be applied to the Lectern's Pinned view, not only to the pinned-task HUD.
The Pinned view SHALL render the player's pins with completed pins ordered below not-completed pins
(preserving pin order within each group), using the same ordering rule the HUD uses. Completing a pinned
task under *sink* from any surface SHALL therefore move that task toward the bottom of the Pinned view,
matching what the HUD shows.

#### Scenario: A sunk task appears at the bottom of the Pinned view
- **WHEN** a player whose policy is *sink* completes a pinned task
- **THEN** that task is shown below the not-completed pins in the Pinned view (matching the HUD's sink
  order), rather than staying in its prior position

#### Scenario: Pinned view and HUD agree on sink order
- **WHEN** the same player views their pins on both the HUD and the Pinned view after a *sink* completion
- **THEN** both surfaces show the completed task sunk below the not-completed pins in the same order

### Requirement: A sink completion of an owned task reorders the owner's Read and Edit views
When a player completes a task under the *sink* policy and that task's document is resolvable, the task
SHALL be moved to the bottom of that document's order (the existing document reorder), and the acting
player's open Read and Edit views of that document SHALL reflect the new order promptly rather than
requiring the player to reopen or switch views. This makes "drop to bottom" visible on the same surface
the player completed the task from, for a task in a document they can edit.

#### Scenario: Read view reflects a sink reorder without reopening
- **WHEN** a player whose policy is *sink* completes a task from the Read view of a resolvable document
- **THEN** the completed task moves to the bottom of the Read list without the player reopening or
  switching views

#### Scenario: Editor reflects a sink reorder in place
- **WHEN** a player whose policy is *sink* completes a task from the editor view
- **THEN** the task moves to the bottom of the editor list while other rows' in-progress edits are
  preserved
