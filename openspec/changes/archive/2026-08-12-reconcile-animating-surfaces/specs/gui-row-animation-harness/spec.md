## ADDED Requirements

### Requirement: A reusable host-owned animation harness drives row enter/exit/reorder
The mod SHALL provide a single reusable animation primitive for animating a dynamic list row's
appearance, departure, and reordering, generalized from the collapse mechanism. The harness SHALL be
driven by a self-ticking `AnimationController` (not a stock implicit-animation widget), SHALL own that
controller on the host surface keyed by the row's stable identity, and SHALL defer any list-mutating
side effect (such as removing a departed row) out of the ticker callback to a later frame. The harness
MUST be usable by any animating surface without that surface inventing its own per-animation survival
scaffolding.

#### Scenario: A new animating row uses the harness rather than bespoke code
- **WHEN** a surface needs to animate a row entering, departing, or moving
- **THEN** it uses the shared harness (self-ticking controller, host-owned, identity-keyed) instead of
  a new one-off animation widget or a new survival mechanism specific to that surface

#### Scenario: Removal side effect is deferred out of the ticker callback
- **WHEN** an exit animation reaches its end and the row must be removed from the underlying data
- **THEN** the removal is performed on a later frame (via a deferred-cleanup signal), not synchronously
  inside the animation tick, so the tree is not torn down re-entrantly during dispatch

### Requirement: The harness survives both a reconcile and a full-tree rebuild
Because the animation controller is host-owned and keyed by stable identity, an in-flight animation
SHALL resume from its current progress after any intervening rebuild of the surface — whether that
rebuild is a reconcile (`SetState`) or a full-tree `ForceRebuild` — rather than snapping to its end or
restarting from the beginning. This holds even when a mid-list mutation causes the positional
reconciler to remount the animating row.

#### Scenario: An in-flight animation resumes after a mid-list mutation
- **WHEN** a row is animating and another row in the same list is added, removed, or reordered before
  the animation completes, remounting the animating row
- **THEN** the animating row resumes its animation from where it was, because the host-owned controller
  for its identity is looked up and reused rather than recreated

#### Scenario: Identity is released when the animation completes
- **WHEN** a row's animation completes
- **THEN** the host releases that identity's controller, freeing it for reuse by a future row without
  inheriting stale animation state
