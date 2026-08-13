## ADDED Requirements

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

### Requirement: Removal timing is selectable by policy without changing the collapse
The container SHALL support at least two removal-timing policies, selectable per hosting surface,
while using the same height-collapse mechanism for the visible removal: an **immediate** policy in
which a departed row begins collapsing on the build it departs, and a **delayed** policy in which a
departed row is held at full height for an undo window (optionally fading) before it collapses. The
choice of policy SHALL NOT change *how* the collapse looks — only *when* it begins.

#### Scenario: Immediate policy collapses at once
- **WHEN** a surface using the immediate policy removes a row
- **THEN** that row begins collapsing on the same build, with no holding delay before the collapse

#### Scenario: Delayed policy holds the row for an undo window
- **WHEN** a surface using the delayed policy removes a row
- **THEN** the row is held (optionally fading) for the undo window, and only then does it collapse

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
