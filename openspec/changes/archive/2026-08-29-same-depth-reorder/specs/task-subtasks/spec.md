## MODIFIED Requirements

### Requirement: The drag grip toggles a row's depth on tap
The row drag grip SHALL support two distinct gestures: a **drag** reorders the row, and a **tap**
(press and release over the grip **without** the pointer having started a drag) toggles the row's
`Depth` between 0 and 1. A drag SHALL start only after the pointer moves past a movement threshold,
not on press. Once a drag has started, releasing the pointer SHALL NOT toggle depth, including when
the row is dropped on its original position (cancel). Tapping a depth-0 row makes it a depth-1
subtask; tapping a depth-1 row returns it to depth 0. The tap gesture SHALL apply to any block kind.
A drag of a **depth-0 parent** SHALL reorder that parent and its owned run as one cluster
(parent first). Dropping the cluster onto one of its own children SHALL NOT change order.
A drag of a depth-1 row remains a leaf (siblings do not follow).

A drag SHALL only be able to reorder a row onto a target of the **same** `Depth`: a depth-0 row's
drop target SHALL be another depth-0 row (or a position inside its own owned-run cluster, which is
the existing no-op), and a depth-1 row's drop target SHALL be another depth-1 row. Dropping on a
row of a different depth SHALL be rejected as a no-op, identically to dropping in place — no reorder
occurs and no edit is sent. This restriction and the owned-run clustering above SHALL be evaluated by
one shared validity check, so a depth-0 parent's cluster move and the same-depth restriction can
never disagree with each other.

While a drag is in progress, the grip's drop-target arrow (▶) SHALL render on a row if and only if
that row is a valid drop target for the row currently being dragged, per the same validity check the
drop commit uses. A row of a different depth than the dragged row SHALL NOT show the arrow, even
while the pointer hovers over it.

#### Scenario: Tapping a top-level row's grip makes it a subtask
- **WHEN** the player taps (presses and releases without dragging) the grip of a `Depth` 0 row
- **THEN** the row's `Depth` becomes 1 and it renders indented

#### Scenario: Tapping a subtask's grip promotes it back to top level
- **WHEN** the player taps the grip of a `Depth` 1 row
- **THEN** the row's `Depth` becomes 0 and it renders un-indented

#### Scenario: Press-and-hold still reorders rather than toggling depth
- **WHEN** the player presses the grip and drags to reorder the row
- **THEN** the row is reordered and its `Depth` is not toggled by that gesture

#### Scenario: Cancelling a drag does not nest
- **WHEN** the player starts a grip drag and releases on the same row without changing order
- **THEN** the row's `Depth` is unchanged

#### Scenario: A depth-1 row cannot be dropped among depth-0 rows
- **WHEN** the player drags a `Depth` 1 row and releases it over a `Depth` 0 row
- **THEN** no reorder occurs — the dragged row stays at its original position and no edit is sent

#### Scenario: A depth-0 parent cannot be dropped among depth-1 rows
- **WHEN** the player drags a `Depth` 0 row and releases it over a `Depth` 1 row that is not one of
  its own owned-run children
- **THEN** no reorder occurs — the dragged parent and its owned-run cluster stay at their original
  position and no edit is sent

#### Scenario: The drop-target arrow only appears on same-depth rows
- **WHEN** the player is mid-drag on a `Depth` 1 row and the pointer moves over a `Depth` 0 row
- **THEN** that `Depth` 0 row shows the idle grip glyph or a hidden grip (per the existing
  drag-active hide rule), never the drop-target arrow

#### Scenario: The drop-target arrow appears on a valid same-depth row
- **WHEN** the player is mid-drag on a `Depth` 1 row and the pointer moves over a different `Depth` 1
  row
- **THEN** that row shows the drop-target arrow (▶)
