# gui-list-collapse

## Purpose

A reusable LibGUI mechanism for animating the removal of a row from a dynamic list: the departing
row collapses its height to zero (so rows below move up to fill the space) before it is removed.
The mechanism is self-driven so it survives full-tree rebuilds, and its per-row state is owned by
the host surface and keyed by stable identity. This capability was created via spec sync from
change `scribe-list-collapse`.
## Requirements
### Requirement: A departing list row collapses its height to zero before removal
The system SHALL provide a reusable GUI mechanism that, when a row is removed from a dynamic list,
animates that row's layout height from its full height to zero over a brief duration, causing the
rows below it to move up smoothly to fill the vacated space, and SHALL remove the row from the
list only after the collapse animation completes.

#### Scenario: A removed row collapses rather than vanishing
- **WHEN** a row is removed from a list that uses this mechanism
- **THEN** the row's height animates from full to zero over the collapse duration and the rows
  below it move up to meet it, rather than the row disappearing in a single frame

#### Scenario: Removal is deferred until the collapse completes
- **WHEN** a row's collapse animation is in progress
- **THEN** the row remains present in the list until the animation reaches its end, at which point
  it is removed and the list is rebuilt without it

### Requirement: The collapse animates correctly under a full-tree rebuild
The collapse mechanism SHALL animate correctly even when its host surface rebuilds by unmounting
and recreating its entire widget tree (rather than reconciling it) during the animation. The
animation SHALL be self-driven — it MUST NOT depend on an implicit/stock animation widget whose
tween state is lost on remount — and its progress SHALL be preserved across intervening rebuilds
so the collapse neither snaps instantly to its end nor restarts from the beginning.

#### Scenario: Collapse still animates when the host recreates the tree
- **WHEN** a row is collapsing and its host surface performs a full-tree rebuild before the
  animation has finished
- **THEN** the collapse continues to animate smoothly from where it was, without snapping to zero
  height immediately and without restarting from full height

### Requirement: Per-row collapse state is owned by the host and keyed by identity
The collapse animation state SHALL be owned by the host surface (not by the transient row widget)
and SHALL be keyed by the row's stable identity, so that multiple rows removed in quick succession
each collapse independently and correctly. The host SHALL release each row's collapse state once
that row's collapse completes.

#### Scenario: Multiple rapid removals collapse independently
- **WHEN** several rows are removed from the same list in quick succession
- **THEN** each row collapses independently over its own animation, and none is left as a
  partially-collapsed gap in the list

#### Scenario: Collapse state is released after completion
- **WHEN** a row's collapse animation completes
- **THEN** the host releases that row's collapse state, and the identity is free to be reused by a
  future row without inheriting stale animation state

### Requirement: Hover state stays current while a row collapses
The system SHALL keep hover state current for the entire duration of a list-collapse animation, so
that an element which slides beneath a stationary cursor as the rows reflow receives its pointer-enter
and displays its hover-gated affordances (such as a row's delete and pin controls) WITHOUT requiring
the user to physically move the mouse. This SHALL hold for every list that uses the collapse
mechanism, not only for a single removal path, and SHALL hold continuously throughout the animation
rather than only at its completion.

#### Scenario: The row that slides under a still cursor gains hover mid-collapse
- **WHEN** a row is removed and, while the collapse animation is in progress, a different row slides
  up beneath a cursor that has not moved
- **THEN** that row's hover-gated controls (delete/pin) become visible during the animation, without
  the user moving the mouse

#### Scenario: Consecutive removals without moving the mouse
- **WHEN** the user deletes a row and then, without moving the cursor, deletes the row that has slid
  under the cursor — repeatedly, before each collapse finishes
- **THEN** each successive delete control is available under the stationary cursor and the user can
  see each delete control without a mouse wiggle between removals

#### Scenario: No hover refresh cost when nothing is collapsing
- **WHEN** no collapse animation is in progress and no tree rebuild has just occurred
- **THEN** the mechanism performs no per-frame hover re-dispatch, and normal hover behavior on real
  mouse motion is unchanged

### Requirement: Hover state recovers after any tree rebuild
The system SHALL refresh hover state at the current cursor position after any full rebuild of a
dialog's or HUD's widget tree (not only rebuilds triggered by a collapse animation), so that an
element which ends up beneath a stationary cursor in the freshly built tree receives its pointer-enter
and displays its hover-gated affordances WITHOUT requiring the user to move the mouse. Because a
rebuild mounts an entirely new tree that lays out on a later frame, the refresh SHALL persist across
enough frames for the rebuilt tree to be laid out before the refresh is dispatched.

#### Scenario: Unpinning a HUD row refreshes hover on the row beneath
- **WHEN** the user unpins a task on the pinned HUD while hovering it, causing a rebuild, and a
  different row occupies the cursor position in the rebuilt list
- **THEN** that row's hover-gated controls become visible without the user moving the mouse

#### Scenario: Creating a new row preserves hover awareness
- **WHEN** the user creates a new task row (e.g. via Enter), causing a rebuild, while the cursor
  remains stationary over the list
- **THEN** the row beneath the stationary cursor retains its hover-gated controls without a mouse
  wiggle

### Requirement: On a reconciling host, hover and click activation hold via preserved identity
When the collapse mechanism runs on a surface that updates by reconciliation (rather than
`ForceRebuild`), the hover-currency and click-activation guarantees SHALL hold because the elements
under the cursor are preserved across the update, NOT via a per-frame hover re-dispatch or a
post-rebuild hover-refresh latch. Specifically, an element that slides beneath a stationary cursor as
rows reflow SHALL retain its hover state, and a row's control that is pressed SHALL remain the same
element at release so its activation is recognized, without the user moving the mouse.

#### Scenario: Hover persists mid-collapse without a refresh latch on a reconciling host
- **WHEN** a row collapses on a reconciling surface and a different row slides up beneath a stationary
  cursor
- **THEN** the row beneath the cursor shows its hover-gated controls because its element (and hover
  state) is preserved by reconciliation, with no per-frame re-dispatch required

#### Scenario: Consecutive mid-collapse deletes each register on the first click
- **WHEN** the user deletes a row and then, without moving the cursor, clicks the delete control of the
  row that has slid under the cursor while its collapse is still animating — repeatedly
- **THEN** each delete registers on the first click, because the delete control's element is preserved
  across the reconcile so the pressed and released element are the same and the activation is
  recognized (superseding the moving-target/rebuild-divide race)

### Requirement: The collapse mechanism is drivable by a container that infers departures from a data diff
The row-collapse mechanism SHALL be usable not only by a host surface that explicitly tracks
departing rows, but also by a reusable container that infers departures by diffing its
identity-keyed item set between builds. When driven this way, the departing-row bookkeeping — the
per-identity snapshot of the departing row, the slot/display-index at which it collapses, and the
deferral of its final removal until the collapse completes — SHALL be provided by the container
itself, so a hosting surface does not re-implement that bookkeeping to obtain the collapse.

#### Scenario: A container drives the collapse from a data diff alone
- **WHEN** a container renders identity-keyed rows and an identity present on the previous build is
  absent on the next
- **THEN** the collapse mechanism animates that row out at its former slot and removes it on
  completion, driven entirely by the container's diff without the hosting surface tracking the
  departing row

#### Scenario: The collapse still survives rebuilds when container-driven
- **WHEN** a container-driven collapse is in progress and the host rebuilds (reconciles) the tree
  before it finishes
- **THEN** the collapse continues smoothly from where it was, neither snapping to zero nor
  restarting from full height

