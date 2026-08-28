## ADDED Requirements

### Requirement: A parent owns the contiguous depth-1 run beneath it
A **parent** SHALL be any depth-0 row followed immediately by one or more depth-1 rows. That
contiguous run SHALL be the parent's **owned run**. The first row that is not depth 1 ends the run.
Completing, sinking, deleting, or trashing a **depth-1** row SHALL treat it as a leaf (siblings are
not its children). Text rows in the run have no done flag but SHALL still move or delete with the
range.

#### Scenario: Completing a parent under Bound moves the whole run
- **WHEN** Subtask Behavior is Bound to parent and the player completes a depth-0 row that has two depth-1 children immediately below it, with completion policy Sink
- **THEN** the parent and both children are marked done (when completable) and the three rows sink together as a block, still contiguous, parent first

#### Scenario: Completing a child does not take siblings
- **WHEN** the player completes a depth-1 row that has a depth-1 sibling below it
- **THEN** only that child is completed (and sunk/deleted per policy); the sibling is unchanged

#### Scenario: Independent leaves children in place
- **WHEN** Subtask Behavior is Independent and the player completes a parent with Sink
- **THEN** only the parent is marked done and sunk; the children stay incomplete where they were

#### Scenario: Discard children removes the owned run on parent complete
- **WHEN** Subtask Behavior is Discard children and the player completes a parent with Keep
- **THEN** the children are removed from the document and the parent stays in place, done; unchecking the parent does not restore the children

#### Scenario: Dragging a parent reorders its owned run as a block
- **WHEN** the player drag-reorders a depth-0 row that has depth-1 children immediately below it
- **THEN** the parent and those children move together, still contiguous, parent first

### Requirement: Subtask Behavior is a per-player setting
The player SHALL have a Subtask Behavior preference with values **Bound to parent** (default),
**Independent**, and **Discard children**. It SHALL apply to complete and to standalone delete
(trash) of a parent. Bound trash SHALL delete the owned run. Independent trash SHALL delete only
the parent. Discard-children trash SHALL delete the owned run. Unchecking a Bound parent SHALL
uncheck completable rows in the same owned run and SHALL NOT unsink or undelete. Pin policy SHALL
apply to whichever rows that option mutates, when those rows are pinned.

#### Scenario: Default is Bound to parent
- **WHEN** a player has never changed Subtask Behavior
- **THEN** completing a parent also completes its owned run

#### Scenario: Uncheck mirrors Bound complete
- **WHEN** Subtask Behavior is Bound and the player unchecks a parent whose children were marked done with it
- **THEN** those children are unchecked; they are not moved back if they had already sunk

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
