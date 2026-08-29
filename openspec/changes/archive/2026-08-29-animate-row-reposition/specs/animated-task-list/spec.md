## ADDED Requirements

### Requirement: A reusable container animates row repositioning inferred from a data diff
The system SHALL animate a surviving row — one whose stable identity is present in both the previous
build's render order and the current one (neither departing, reviving, nor freshly appeared) — when
its slot index changes between builds, rather than letting it snap to its new position. The container
SHALL detect this purely from comparing the previous and current render order, without the hosting
surface issuing any explicit "begin reposition" call and without regard to the mutation's cause (an
insertion elsewhere in the list, a removal elsewhere in the list, or an explicit reorder of the item
set are all detected and animated identically).

#### Scenario: A survivor shifts because a row is inserted above it
- **WHEN** a hosting surface adds a new row above an existing one and rebuilds
- **THEN** the container detects the existing row's slot index change and animates its displacement
  from its previous rendered position to its new one, rather than the row jumping instantly

#### Scenario: A survivor shifts because a row elsewhere is removed
- **WHEN** a row departs the item set and other, non-departing rows shift slots as a result
- **THEN** each shifted survivor animates its own displacement independently of the departing row's
  own collapse animation

#### Scenario: A survivor shifts because of an explicit reorder
- **WHEN** a hosting surface reorders its item set without any row entering or departing (e.g. a
  completion policy moving a pin to a new position while it stays pinned)
- **THEN** every row whose slot changed animates its displacement the same way as an insertion- or
  removal-caused shift, with no surface-specific reorder animation code

#### Scenario: A new surface gets reposition animation by rendering the container
- **WHEN** a surface renders its rows through the container
- **THEN** its rows animate repositioning under the same rule as the other surfaces, with no
  reposition-specific code added to that surface

### Requirement: A row's reposition displacement reflects its real measured movement
The container SHALL seed a repositioning row's displacement animation from that row's actual
previous and current rendered position, not an assumed or fixed distance. A fixed distance is safe
for a freshly-appeared row (it has no prior on-screen position to match) but not for a survivor,
whose motion must start from where it was actually last rendered or the animation itself introduces a
visible discontinuity at its start.

#### Scenario: A wrapped row's animation starts from its true previous position
- **WHEN** a survivor's slot changes and its reposition animation begins
- **THEN** the animation's starting offset corresponds to that row's actual previously-rendered
  position relative to its new one, so the row appears to move continuously from where it visually
  was, with no pop at the start of the motion

### Requirement: Reposition animation reuses the self-ticking, identity-keyed harness
The reposition animation SHALL be driven by a self-ticking `AnimationController` owned by the same
host-passed registry the entry and departure animations already use, keyed by the row's stable
identity in a namespace distinct from the entry and collapse keys, so it survives a `ForceRebuild`
remount or a reconcile `SetState` mid-animation exactly like the existing motions, and so a row that
is simultaneously finishing an entry and beginning a reposition does not collide in the registry.

#### Scenario: A reposition survives a rebuild mid-animation
- **WHEN** a row is partway through its reposition animation and the host performs a `ForceRebuild`
  or a reconciling `SetState`
- **THEN** the reposition animation continues from its current progress to completion without
  snapping or restarting

#### Scenario: A row is excluded from reposition only on the exact build it first appears
- **WHEN** a row appears in the item set for the first time (it was not live in the previous build)
- **THEN** that same build it receives only its entry animation, never a reposition animation, because
  it has no prior rendered position to displace from yet

#### Scenario: A row that entered earlier remains eligible to reposition for the rest of its life
- **WHEN** a row that has already appeared in an earlier build (whether or not its own entry motion
  has finished playing) is later displaced by another row entering, departing, or reordering
- **THEN** it receives a reposition animation exactly like any other survivor, stacking with its own
  (possibly still-inert, possibly still-finishing) entry wrapper rather than being permanently
  excluded from reposition for the rest of its live lifetime just because it once entered
