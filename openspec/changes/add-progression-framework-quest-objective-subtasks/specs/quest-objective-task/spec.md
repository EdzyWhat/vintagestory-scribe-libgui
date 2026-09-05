## Purpose

Models one Progression Framework quest objective as a depth-1 subtask under its parent Quest Link,
showing live have/need progress without being mistaken for — or interfered with by — carried-item
Tracker counting.

## ADDED Requirements

### Requirement: QuestObjective block kind, fields, and exclusion from carried-inventory tracking
The document model SHALL support a `QuestObjective` block kind, appended to the existing kind
enumeration (never renumbering existing values). A `QuestObjective` block SHALL carry a target
quantity (`TargetQuantity`, the objective's required count) and a current-progress count
(`CurrentQuantity`), using the same fields and clamping rules as Tracker/Craft. It SHALL retain the
fields common to every block (text, depth, `TaskId`, assignment). A `QuestObjective` block SHALL
**NOT** be counted by any carried-inventory tracking mechanism — its `CurrentQuantity` is driven
exclusively by the owning backend's own reported progress, never by what the viewing player happens
to be carrying.

#### Scenario: A QuestObjective block carries target/current like a Tracker
- **WHEN** a QuestObjective block is created with a required count of 27 and a reported progress of 3
- **THEN** its `TargetQuantity` is 27 and its `CurrentQuantity` is 3

#### Scenario: Carrying the matching item does not change a QuestObjective's count
- **WHEN** the viewing player carries items that would satisfy a QuestObjective's underlying item (if
  it has one), but the backend has not reported that progress
- **THEN** the QuestObjective's `CurrentQuantity` is unaffected by the carried-inventory scan

### Requirement: A QuestObjective shows a real item icon only when its objective resolves to one
When a Progression Framework quest objective's definition resolves to exactly one concrete item (a
delivery-type objective with no alternates), its `QuestObjective` block SHALL carry that item's code
so the row renders the real item icon and name, identically to how a Tracker row does. When no single
item resolves (multiple alternates, or a non-item objective type such as kill/interact), the block
SHALL instead carry a captured, human-readable label and render a generic icon, mirroring how a
guide-page Link degrades when it has no item to show.

#### Scenario: A single-item delivery objective shows the real item
- **WHEN** an objective requires delivering a specific item with no alternates (e.g. rope)
- **THEN** its QuestObjective row shows that item's real icon and name

#### Scenario: A non-item objective shows a generic icon and captured label
- **WHEN** an objective has no resolvable single item (e.g. a kill-count or interact objective)
- **THEN** its QuestObjective row shows a generic icon and the objective's captured readable label

### Requirement: Objective subtasks are reconciled, not regenerated, and never auto-deleted
On a Quest Link's creation, one `QuestObjective` child SHALL be generated per objective in the
quest's catalog definition, placed contiguously at depth 1 directly below the parent (per the
`task-subtasks` capability's owned-run mechanics). Thereafter, as the backend reports updated
progress, existing `QuestObjective` children SHALL be matched to their objective by the objective's
own stable code (not by item code, since two objectives may share one) and have their
`CurrentQuantity` updated in place. Reconciliation SHALL never delete an existing child (including one
the player has edited or added manually) and SHALL never create a duplicate for an objective that
already has a matched child.

#### Scenario: Objective children are generated once at Link creation
- **WHEN** a player creates a Quest Link for a quest with three objectives
- **THEN** three `QuestObjective` children are created at depth 1 directly below the parent, one per
  objective

#### Scenario: Reported progress updates an existing child in place
- **WHEN** the backend reports a new progress count for an objective that already has a matched
  QuestObjective child
- **THEN** that child's `CurrentQuantity` is updated to the new count, with no new row created

#### Scenario: A player-deleted objective child is not resurrected
- **WHEN** the player deletes one QuestObjective child and the backend later reports further progress
  on that same objective
- **THEN** the child is not recreated

### Requirement: A QuestObjective with a real item opens its Handbook page like a Tracker
When a `QuestObjective` block carries a resolvable item code, clicking its label SHALL open that
item's Handbook page, exactly like a Tracker or Craft-ingredient row. A `QuestObjective` with no
resolvable item (a captured-label-only row) SHALL NOT attempt to open anything on click.

#### Scenario: Clicking an item-backed QuestObjective opens its Handbook page
- **WHEN** the player clicks a QuestObjective row that carries a resolvable item code
- **THEN** that item's Handbook page opens

#### Scenario: Clicking a label-only QuestObjective does nothing
- **WHEN** the player clicks a QuestObjective row with no resolvable item
- **THEN** nothing opens and no error occurs
