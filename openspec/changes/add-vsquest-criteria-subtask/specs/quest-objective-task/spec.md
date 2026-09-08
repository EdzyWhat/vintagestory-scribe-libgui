## ADDED Requirements

### Requirement: VS Quest Quest Links get one-shot static criteria subtasks
When a VS Quest Quest Link is created, the system SHALL generate one `QuestObjective` child per
objective in the quest's static catalog definition (kill, gather, block-place, and block-break
objectives alike), placed contiguously at depth 1 directly below the parent, exactly like
Progression Framework's objective-subtask generation. Each generated child's `TargetQuantity`
SHALL be the objective's required count, and its captured label SHALL be a human-readable
description resolved from the objective's matching codes (falling back to the raw code list if no
display name resolves). Unlike Progression Framework's objectives, these children SHALL NOT be
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
