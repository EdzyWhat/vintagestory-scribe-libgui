# animated-task-list Specification

## Purpose
Defines a reusable, view-agnostic list-container component (`ScribeAnimatedList`) that renders an
ordered set of identity-keyed rows and animates row departures inferred purely from a frame-to-frame
data diff, so any hosting surface gets collapse-and-slide-up removal animation by rendering through the
container rather than hand-wiring departing-row bookkeeping. Adopted across all four Scribe surfaces —
the editor, Read view, Pin Tab, and pinned-task HUD.

## Requirements
### Requirement: A reusable container animates row departures inferred from a data diff
The system SHALL provide a reusable, view-agnostic list-container component that renders an
ordered set of rows keyed by stable identity, and that animates the **departure** of a row
without the hosting surface issuing any explicit "begin departure" call. The container SHALL
compare the identity-keyed item set it rendered on the previous build to the incoming set; any
identity that was present and is now absent SHALL be treated as a departing row and animated out
(its height collapsing so rows below move up), rather than disappearing in a single frame. The
hosting surface SHALL only need to mutate its own data and rebuild; it SHALL NOT need to own a
departing-row map, a frozen-row widget, a collapse-cleanup flag, or per-frame scroll/hover loops.

#### Scenario: Removing a row animates without host wiring
- **WHEN** a hosting surface renders its rows through the container, then mutates its data so one
  row's identity is no longer in the item set, and rebuilds
- **THEN** the container detects the absent identity and collapses that row's height to zero while
  the rows below move up to fill the space, without the surface issuing any departure call

#### Scenario: The departing row is removed only after its animation completes
- **WHEN** a row is animating its departure in the container
- **THEN** the row remains rendered until the collapse animation completes, at which point the
  container drops it and rebuilds without it

#### Scenario: A new surface gets removal animation by rendering the container
- **WHEN** a surface that previously had no removal animation renders its rows through the container
- **THEN** its row removals animate with the same collapse-and-slide-up behavior, with no
  animation-specific code added to that surface

### Requirement: The container renders a departed row from a captured snapshot
Because a departed row's live data no longer exists, the container SHALL render the departing row
from a snapshot captured while the row was still live — by default the last widget it built for
that identity — so the collapsing ghost is a faithful static image of the row as it last appeared.
The container SHALL allow the row-builder to supply an explicit non-interactive snapshot for an
identity when reusing the last live row is not safe.

#### Scenario: A departed row collapses as its last-rendered image
- **WHEN** a row departs and the container animates it out
- **THEN** the collapsing ghost shows the row's last-rendered content (or a builder-supplied
  snapshot), not a blank or a live/interactive row bound to now-absent data

### Requirement: Removal timing is a container policy parameter, independent of the collapse
The container's removal timing SHALL be expressed as a selectable per-surface policy parameter that
is independent of *how* the collapse looks: the choice of policy SHALL change only *when* a departed
row begins collapsing, never the height-collapse mechanism itself. The shipped policy set is a single
**immediate** policy, in which a departed row begins collapsing on the build its identity leaves the
item set. A held-ghost **delayed** policy — holding a departed row at full height for an undo window
before collapsing — was explored for the HUD and deliberately removed as a misconception: a frozen
ghost cannot host the live undo checkbox the misclick rescue depends on, so the HUD's undo window is
instead a live-row deferred-send phase that keeps the pin IN the item set until the window elapses
(see `gui-hud-shared-row-animation`). The policy parameter is retained (single-valued) so call sites
read their removal timing explicitly and a future timing policy can be added without touching the
collapse.

#### Scenario: The immediate policy collapses at once
- **WHEN** a surface using the immediate policy removes a row (its identity leaves the item set)
- **THEN** that row begins collapsing on the same build, with no holding delay before the collapse

#### Scenario: A removal reaching the container is always an affirmative choice
- **WHEN** any surface removes a row through the container, including the HUD
- **THEN** the row's identity has already left the item set as an affirmative removal — any
  misclick-grace window lives BEFORE the identity departs (on a live row), so the container never
  receives a tentative removal it must hold

### Requirement: A departing row that reappears before its animation completes is revived
The container SHALL handle an identity that departs and then reappears in the item set before its
collapse animation completes by cancelling the departure and restoring the identity as a live row,
rather than continuing to collapse it or rendering both a ghost and a live row for that identity.

#### Scenario: Re-adding a row mid-collapse cancels the departure
- **WHEN** a row is collapsing and its identity reappears in the incoming item set before the
  collapse completes
- **THEN** the container cancels that row's departure and renders it as a single live row again,
  with no lingering ghost for that identity

### Requirement: The container preserves correct slot order for simultaneous departures
When more than one row departs at the same time, the container SHALL collapse each departing row at
the slot it occupied, preserving the relative order of departing and surviving rows, so multiple
simultaneous removals do not reorder or misplace rows.

#### Scenario: Rapid multi-row removal collapses each in place
- **WHEN** several rows depart in quick succession or on the same build
- **THEN** each departing row collapses at its own former slot and the surviving rows retain their
  correct relative order throughout

### Requirement: The container keeps the scroll viewport and hover state correct during a collapse
While a row is collapsing, the container SHALL keep the scroll viewport tracking the shrinking
content height (so a bottom-anchored view eases upward with the collapse rather than snapping) and
SHALL keep hover-gated controls correct for a row that slides under a stationary cursor, without the
hosting surface implementing these behaviors itself.

#### Scenario: Scroll eases with a collapse at the bottom
- **WHEN** the list is scrolled to the bottom and its last row departs
- **THEN** the viewport eases upward in lockstep with the collapse rather than jumping to the new
  extent in a single frame

#### Scenario: A row sliding under a still cursor keeps its hover controls
- **WHEN** a row above the cursor departs so a different row slides under the stationary cursor
- **THEN** that row's hover-gated controls reflect the cursor being over it, without the cursor moving

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
