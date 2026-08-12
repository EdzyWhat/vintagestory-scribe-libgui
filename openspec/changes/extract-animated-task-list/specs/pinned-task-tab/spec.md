## ADDED Requirements

### Requirement: Pin Tab row removal animates immediately with no undo window
When a pin is removed from the Pin Tab — by completing it (under a completion policy that removes
it), unpinning it, or deleting the underlying task — the departing row SHALL animate out (its height
collapsing so the rows below move up smoothly to fill the space) rather than vanishing in a single
frame. The Pin Tab SHALL take **immediate** removal action with no undo/grace window: the animation
begins as soon as the removal is initiated, and the action is not held for a revert period. This
differs deliberately from the pinned-task HUD, which delays completion behind an undo window; the
Pin Tab shows and lets the player change the Completion Policy and provides discrete unpin and delete
controls, so its choices are affirmative and need no misclick grace.

#### Scenario: Removing a Pin Tab row collapses instead of snapping
- **WHEN** the player completes (with a removing policy), unpins, or deletes a task from the Pin Tab
- **THEN** that row's height collapses to zero and the rows below move up to fill the space, rather
  than the row disappearing and the list snapping in a single frame

#### Scenario: Pin Tab removal is immediate, not delayed
- **WHEN** the player removes a Pin Tab row
- **THEN** the collapse begins immediately with no undo window held before it, unlike the HUD's
  delayed completion

#### Scenario: The underlying completion/unpin/delete semantics are unchanged
- **WHEN** the player removes a Pin Tab row
- **THEN** the same completion, unpin, or delete action is performed as before (same authoritative
  effect on the pin set), with only the visual removal now animated
