# quest-objective-task Specification

## Purpose

Models one Progression Framework quest objective as a depth-1 subtask under its parent Quest Link,
showing live have/need progress without being mistaken for — or interfered with by — carried-item
Tracker counting.

## Requirements

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

### Requirement: VS Quest Quest Links get one-shot static criteria subtasks
When a VS Quest Quest Link is created, the system SHALL generate one `QuestObjective` child per
objective in the quest's static catalog definition (kill, gather, block-place, and block-break
objectives alike), placed contiguously at depth 1 directly below the parent, exactly like
Progression Framework's objective-subtask generation. Each generated child's `TargetQuantity`
SHALL be the objective's required count, and its captured label SHALL be a human-readable
description resolved from the objective's single exact matching code. Wildcard, multi-code, and
unresolved objectives SHALL use a localized `"{KindLabel} {demand}"` label. A static VS Quest child
SHALL use ordinary task text color and a dedicated objective marker rather than Quest Link color or
the Quest Link book marker. In Editor view, a generic child's drag handle, objective marker, and
label SHALL share the ordinary task row's vertical alignment, and pinning the child SHALL retain the
ordinary pinned-row highlight. When the child resolves to one concrete item, it SHALL show that
item's inventory icon and activation SHALL open the item's Handbook entry. A generic child SHALL
remain non-interactive. Unlike Progression Framework's objectives, these children SHALL NOT be
reconciled or updated again after creation — VS Quest exposes no live per-objective signal outside
an open quest-selection dialog, so this is a one-time snapshot of the acceptance criteria, not a
progress tracker. The player MAY freely edit or delete a generated child; no later action ever
recreates, resurrects, or overwrites it.

#### Scenario: Creating a VS Quest Quest Link generates one static child per objective
- **WHEN** a player creates a Quest Link for a VS Quest-backed quest with a kill objective (demand
  50) and a gather objective (demand 5)
- **THEN** two `QuestObjective` children are created at depth 1 directly below the parent, one with
  `TargetQuantity` 50 and one with `TargetQuantity` 5, each with a human-readable label describing
  its required item/entity

#### Scenario: Gather objectives get a static subtask despite having no live progress signal
- **WHEN** a VS Quest quest's catalog includes a gather objective
- **THEN** that objective still gets a generated `QuestObjective` child, even though VS Quest never
  reports live gather progress by any means

#### Scenario: A VS Quest objective subtask is never revisited after creation
- **WHEN** time passes, the quest is later accepted, progressed, or completed, or the player
  reopens the quest-selection dialog
- **THEN** none of that activity updates, recreates, or removes any previously generated VS Quest
  `QuestObjective` child

#### Scenario: A quest with no objectives generates no children
- **WHEN** a VS Quest Quest Link is created for a quest whose catalog definition has no kill,
  gather, block-place, or block-break objectives
- **THEN** no `QuestObjective` children are generated for it

#### Scenario: A resolved item objective opens its Handbook entry
- **WHEN** a generated VS Quest objective child resolves to one concrete item and the player
  activates that child
- **THEN** the item's Handbook entry opens

#### Scenario: A generic static objective reads as an indicator rather than a link
- **WHEN** a VS Quest objective uses wildcard, multi-code, or unresolved matching codes
- **THEN** its generated child uses ordinary task text color and a dedicated objective marker
- **AND** activating the child performs no action

#### Scenario: A generic static objective aligns like an ordinary Editor task row
- **WHEN** a wildcard, multi-code, or unresolved VS Quest objective child is shown in Editor view
- **THEN** its drag handle, objective marker, and label are vertically aligned like an ordinary
  task row

#### Scenario: A pinned generic static objective keeps its Editor highlight
- **WHEN** a wildcard, multi-code, or unresolved VS Quest objective child is pinned and shown in
  Editor view
- **THEN** its row displays the ordinary pinned-row highlight
