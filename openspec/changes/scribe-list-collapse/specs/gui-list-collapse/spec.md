## ADDED Requirements

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
