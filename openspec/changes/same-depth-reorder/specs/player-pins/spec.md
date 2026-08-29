## MODIFIED Requirements

### Requirement: Reorder the per-player pin list

The system SHALL allow a player to reorder their own pin list, addressed by pin identity, so the
Pin Tab can arrange pins in a player-chosen order. Reordering SHALL permute only that player's
per-player pin list; it SHALL NOT change any document's block order and SHALL NOT affect any other
player's pin list. The reordered list SHALL be persisted per-player (in the existing per-player pin
store) and re-synced to the owning player so the new order survives a restart and is reflected on every
surface that reads that player's pins.

A drag SHALL only be able to reorder a pin onto a target of the **same** `Depth`: a depth-0 pin's
drop target SHALL be another depth-0 pin, and a depth-1 pin's drop target SHALL be another depth-1
pin. Dropping on a pin of a different depth SHALL be rejected as a no-op — no permutation occurs and
no message is sent — the same validity check `task-subtasks`' editor reorder uses, applied here to
the pin list's own `Depth` values and position rather than a document's.

A drag of a depth-0 pin SHALL reorder that pin together with its owned-run cluster — the contiguous
run of depth-1 pins immediately following it in the current pin list — as one unit, so a pinned
parent's reorder can never strand its already-pinned children. Dropping the cluster onto one of its
own children SHALL NOT change order. A drag of a depth-1 pin remains a leaf (siblings do not follow).

While a drag is in progress on the Pin Tab, the grip's drop-target arrow (▶) SHALL render on a pin
row if and only if that row is a valid same-depth drop target for the pin currently being dragged,
mirroring the editor's arrow rule.

#### Scenario: Reorder persists and re-syncs
- **WHEN** a player reorders their pin list through the Pin Tab
- **THEN** their pin list is permuted into the new order, persisted per-player, and re-synced to their
  client, and the same order is restored after a restart

#### Scenario: Reordering does not touch document block order
- **WHEN** a player reorders their pins that reference tasks in one or more documents
- **THEN** no document's block order changes and no other player's pin list is affected — only the
  reordering player's own pin list order changes

#### Scenario: A depth-1 pin cannot be dropped among depth-0 pins
- **WHEN** the player drags a `Depth` 1 pin and releases it over a `Depth` 0 pin
- **THEN** no permutation occurs and no `ScribeReorderPinsMessage` is sent

#### Scenario: A depth-0 pin cannot be dropped among a different parent's depth-1 pins
- **WHEN** the player drags a `Depth` 0 pin and releases it over a `Depth` 1 pin that is not one of
  its own owned-run cluster
- **THEN** no permutation occurs and no message is sent

#### Scenario: Dragging a pinned parent keeps its pinned children with it
- **WHEN** a player drags a pinned depth-0 parent that has one or more already-pinned depth-1
  children immediately following it, and drops it elsewhere among the depth-0 pins
- **THEN** the parent and its pinned children move together, in their prior relative order, and no
  pinned child is left behind at the parent's old position

#### Scenario: The drop-target arrow only appears on same-depth pin rows
- **WHEN** the player is mid-drag on a `Depth` 0 pin and the pointer moves over a `Depth` 1 pin
  outside its own cluster
- **THEN** that row does not show the drop-target arrow
