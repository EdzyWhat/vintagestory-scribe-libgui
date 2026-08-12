## ADDED Requirements

### Requirement: The pinned HUD renders from persistent content updated by reconcile
The pinned-task HUD SHALL render its pin list from a persistent content `StatefulWidget` updated via
`SetState` on the pin-push, tick-expiry, and toggle paths, rather than rebuilding the whole HUD tree
via `ForceRebuild()` on each change. The 0⇄1-pin self-open/close remains a host concern
(`TryOpen`/`TryClose`), distinct from the in-place reconcile of the row list. HUD rows SHALL be keyed
by stable TaskId so that hover, animation controllers, and pointer-capture are preserved across an
in-place update.

#### Scenario: A pin push updates the HUD in place
- **WHEN** the server pushes a pin-set change that keeps the HUD open (still one or more pins)
- **THEN** the HUD updates its row list via `SetState`, preserving hover state and any in-flight row
  animation, rather than tearing down and recreating the whole HUD tree

#### Scenario: The HUD still opens and closes at the pin-count boundary
- **WHEN** the player's pin count crosses 0⇄1
- **THEN** the HUD opens or closes via the host `TryOpen`/`TryClose` path, independent of the in-place
  row-list reconcile
