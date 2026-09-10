## MODIFIED Requirements

### Requirement: Create Assignments tab can pull tasks from the Desk's own document
When the staging slot is empty, the Create Assignments tab SHALL always show a “Create Tasks to
Assign” button in its empty task-list state. Activating that button SHALL enter the Assignment
Desk's existing Editor view through the same server-authoritative editor-lock behavior as the
Editor navigation button. When the Desk's own document has at least one eligible row, the empty
state SHALL also show the existing pull-from-Desk button after the create button; activating the
pull button SHALL populate the task list from the Desk's own document. The populated list SHALL
apply the same selection rules (independent per-row Selected checkboxes,
parent-selects-its-subtasks-once-on-select) as the existing staged-item task list.

#### Scenario: Create button is always present in the empty task-list state
- **WHEN** the staging slot is empty and the Create Assignments task list has no active source rows
- **THEN** the empty state shows a “Create Tasks to Assign” button below its hint text

#### Scenario: Create button enters the existing Editor through its lock path
- **WHEN** a player activates “Create Tasks to Assign”
- **THEN** the dialog requests the same server-authoritative editor access used by the Editor nav
  button and opens the Desk's local Editor view when access is granted

#### Scenario: Button appears only when there is something to pull
- **WHEN** the staging slot is empty, the Desk's own document has at least one eligible row, and the
  Desk source has not already been activated
- **THEN** the empty state shows “Create Tasks to Assign” first and the pull-from-Desk button after it

#### Scenario: Button is absent with nothing to pull
- **WHEN** the staging slot is empty and the Desk's own document has no eligible rows
- **THEN** the empty state shows its hint and “Create Tasks to Assign,” with no pull-from-Desk button

#### Scenario: Pulling from the Desk populates the list with normal selection rules
- **WHEN** a player clicks the pull-from-Desk button
- **THEN** the task list populates with the Desk's own document's rows, each with an independent
  Selected checkbox, and selecting a parent row also selects its immediately-following subtask
  rows exactly once
