## ADDED Requirements

### Requirement: Tracker task kind and fields
The document model SHALL support a `Tracker` block kind that carries a target item
(`TargetItemCode`, an item/block asset code stored as a plain string so `Core` stays free of the
Vintage Story API), a target quantity (`TargetQuantity`, an integer ≥ 1), and a current-progress
count (`CurrentQuantity`, an integer ≥ 0). A Tracker block SHALL also retain the fields common to
every block (text, completed flag, depth, `TaskId`, assignment). `TargetQuantity` SHALL be clamped
to at least 1 on creation and edit; `CurrentQuantity` SHALL be clamped to the range
`[0, TargetQuantity]` whenever it is set. The kind value SHALL be appended to the existing kind
enumeration (never renumbering `Task` or `Text`).

#### Scenario: A tracker carries its target and progress
- **WHEN** a Tracker block is created for target item `game:ingot-copper` with `TargetQuantity` 5
- **THEN** the block's kind is `Tracker`, its `TargetItemCode` is `game:ingot-copper`, its
  `TargetQuantity` is 5, and its `CurrentQuantity` is 0

#### Scenario: Target quantity is clamped to at least one
- **WHEN** a Tracker is created or edited with a target quantity of 0 or a negative number
- **THEN** the stored `TargetQuantity` is 1

#### Scenario: Current quantity is clamped to the target range
- **WHEN** a Tracker's `CurrentQuantity` is set below 0 or above its `TargetQuantity`
- **THEN** the stored `CurrentQuantity` is clamped into `[0, TargetQuantity]`

### Requirement: Tracker progress is driven by carried inventory only
A Tracker's `CurrentQuantity` SHALL reflect only the matching items the player is **carrying**
(hotbar plus backpack/inventory the player holds), never items in world containers such as chests.
The count SHALL be recomputed when the player's carried inventory changes and periodically as an
edge-case safeguard, and the server SHALL be the authority for the persisted `CurrentQuantity`
(clients report, the server decides).

#### Scenario: Counting matches carried items
- **WHEN** the player is carrying 3 stacks totalling 12 units that match the Tracker's target
- **THEN** the Tracker's `CurrentQuantity` reflects the carried total (clamped to `TargetQuantity`)

#### Scenario: Items in a nearby chest are not counted
- **WHEN** matching items are present only in a chest near the player, not carried
- **THEN** the Tracker's `CurrentQuantity` does not include them

#### Scenario: Progress updates when carried inventory changes
- **WHEN** the player picks up or removes matching items from their carried inventory
- **THEN** the Tracker's `CurrentQuantity` is recomputed to the new carried total

### Requirement: Tracker completion behavior is a per-player setting
Reaching the target (`CurrentQuantity == TargetQuantity`) SHALL trigger a per-player completion
behavior with three modes: **completes** the task (marks it done), **deletes** the task, or does
**nothing**. The default SHALL be *completes*. Falling back below the target after completion SHALL
NOT resurrect a deleted task, and SHALL follow the completion capability's existing rules for a
task that was auto-completed.

#### Scenario: Default behavior completes the task at target
- **WHEN** a Tracker with the default completion setting reaches its target
- **THEN** the task is marked completed

#### Scenario: Delete behavior removes the task at target
- **WHEN** a Tracker whose owner's completion setting is *delete* reaches its target
- **THEN** the task is removed from the document

#### Scenario: Do-nothing behavior leaves the task open at target
- **WHEN** a Tracker whose owner's completion setting is *nothing* reaches its target
- **THEN** the task remains present and not auto-completed, showing a full/satisfied counter

### Requirement: Tracker row shows a have/need counter and progress state
A Tracker row SHALL display the target item's icon and name and a `have/need` counter (e.g.
"Copper ingot 3/5"), together with a progress indicator that distinguishes at least: none, partial,
and satisfied. A shortfall SHALL read as unsatisfied (e.g. a red/negative treatment) and a met
target SHALL read as satisfied (matching the existing completed-row treatment).

#### Scenario: Partial progress reads as a shortfall
- **WHEN** a Tracker shows `CurrentQuantity` 3 of `TargetQuantity` 5
- **THEN** the row shows "3/5" and a shortfall/partial progress state

#### Scenario: Met target reads as satisfied
- **WHEN** a Tracker's `CurrentQuantity` equals its `TargetQuantity`
- **THEN** the row shows a satisfied progress state consistent with a completed row
