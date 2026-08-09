## ADDED Requirements

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
