## Purpose

Lets a player collapse a subtask group (a Quest's objectives, a Crafting Task's ingredient
trackers, or any future depth-1 owned run) out of view under its parent row, so a long Read View
list can be skimmed at the top level, while keeping that group's context visible — even partially
— whenever a filter pill would otherwise hide it entirely.

## ADDED Requirements

### Requirement: A collapse toggle appears on every subtask-group parent
Any Read View row that is a depth-0 block immediately followed by a contiguous run of depth-1
blocks (its "owned run", per `task-subtasks`) SHALL show a small toggle control. A row with no
owned run SHALL show no toggle.

#### Scenario: Toggle appears on a Quest Link with objectives
- **WHEN** a Quest Link block is immediately followed by one or more `QuestObjective` blocks
- **THEN** the Quest Link's row shows a collapse toggle

#### Scenario: Toggle appears on a Craft parent with generated trackers
- **WHEN** a Craft block is immediately followed by its auto-generated `Tracker` blocks
- **THEN** the Craft block's row shows a collapse toggle

#### Scenario: No toggle on a row without an owned run
- **WHEN** a `Task` row has no depth-1 blocks immediately beneath it
- **THEN** its row shows no collapse toggle

### Requirement: Collapsing hides the group's owned run
Toggling a group's collapse control to "collapsed" SHALL remove its owned run's rows from the
rendered list; toggling it back to "expanded" SHALL restore them, preserving their original order.

#### Scenario: Collapsing hides children
- **WHEN** a player collapses a Quest Link's group
- **THEN** its `QuestObjective` rows no longer render, and the Quest Link's own row remains

#### Scenario: Expanding restores children in order
- **WHEN** a player expands a previously-collapsed group
- **THEN** its owned-run rows render again, in their original document order

### Requirement: A subtask group is filter-visible if any member matches the active pill
Under a non-All filter pill, a subtask group (parent + owned run) SHALL render if at least one of
its members — the parent or any child — individually matches the active filter's category. If no
member matches, the entire group SHALL be hidden, exactly as a non-matching standalone row is
hidden.

#### Scenario: A group with one matching child stays visible under that filter
- **WHEN** the Completed pill is active and one `QuestObjective` child of a Quest Link is complete
  while the Quest Link parent and its other children are not
- **THEN** the Quest Link parent and all of its children still render

#### Scenario: A group with no matching members is hidden
- **WHEN** the Completed pill is active and no member of a group (parent or any child) is complete
- **THEN** neither the parent nor any of its children render

### Requirement: Row opacity within a visible group is per-row and collapse-independent
Within a group that is rendering under a non-All filter, each member's opacity SHALL depend only
on whether that member individually matches the active filter: matching members render at normal
opacity, non-matching members render at reduced ("shadow", ~50%) opacity. This opacity rule SHALL
be unaffected by whether the group is expanded or collapsed. A shadow row SHALL remain fully
interactive (checkbox, pin, edit all still work) — only its opacity changes.

#### Scenario: Non-matching sibling renders as a shadow row
- **WHEN** the Completed pill is active and one `QuestObjective` child is complete while a sibling
  child is not
- **THEN** the complete child renders at normal opacity and the incomplete sibling renders at
  reduced opacity, both still checkable/editable

#### Scenario: A non-matching parent renders as a shadow row
- **WHEN** the Completed pill is active and a Quest Link parent (which has no complete-state of
  its own) has a complete child
- **THEN** the Quest Link parent renders at reduced opacity while its complete child renders at
  normal opacity

### Requirement: A collapsed group's parent still surfaces for a hidden matching child
When a group is collapsed, its parent row SHALL still render under a non-All filter whenever any
member of the group — including a now-hidden child — matches that filter, at the same
normal-or-shadow opacity it would have if the group were expanded.

#### Scenario: Collapsed parent surfaces via a hidden matching child
- **WHEN** the Completed pill is active, a group is collapsed, and one of its hidden children is
  complete while the parent itself is not
- **THEN** the parent row renders (at shadow opacity, since the parent itself doesn't match)

### Requirement: No shadow rendering under the All pill
When the All pill is active, no filtering or shadow-opacity logic SHALL apply: every row,
including every subtask-group member, SHALL render at normal opacity (subject only to collapse
state).

#### Scenario: All pill shows everything at full opacity
- **WHEN** the All pill is active
- **THEN** no row in any subtask group renders at reduced opacity, regardless of complete/pinned
  state

### Requirement: Shadow rows never count toward a pill's bracketed count
A pill's bracketed count (per `read-view-filter-pills`) SHALL reflect only rows that individually
match that pill's category — a row rendered solely because it shares a group with a matching row
SHALL never be included in any pill's count.

#### Scenario: A shadow sibling does not inflate the count
- **WHEN** the Completed pill is active, one child of a group is complete, and its shadow-rendered
  sibling is not
- **THEN** the Completed pill's count reflects only the one complete row, not the shadow sibling

### Requirement: Collapse state persists per subtask group, per Scribe item/block instance
Each Scribe item or block that hosts Read View SHALL remember the collapsed/expanded state of each
of its subtask groups across dialog close/reopen and across game sessions, keyed to that specific
group (not by list position, so it survives reordering), independent of every other instance.
Groups with no persisted state default to expanded.

#### Scenario: Reopening a document resumes its groups' collapse state
- **WHEN** a player collapses a Quest Link's group, closes the dialog, and reopens the same
  document later
- **THEN** that Quest Link's group reopens collapsed, and any other group not explicitly collapsed
  reopens expanded

#### Scenario: Collapse state follows the group after reordering
- **WHEN** a player collapses a group, then reorders blocks such that the group's parent moves to
  a different position in the list
- **THEN** the group is still shown collapsed at its new position
