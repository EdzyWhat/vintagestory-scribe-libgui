# assignment-desk-own-document Specification

## Purpose
TBD - created by archiving change add-assignment-desk-own-tasks. Update Purpose after archive.
## Requirements
### Requirement: Assignment Desk exposes its own document via an Editor tab
The Assignment Desk's block entity already owns a full `ScribeDocument` (inherited from the
writing-station base every Notebook/Lectern/Tablet uses). The system SHALL expose it through an
Editor nav tab on the Assignment Desk's dialog, with the same affordances as any other writing
station's Editor view (completion checkbox, pin, delete, reorder, Tracker/Link/Craft rows) and the
same server-lock-gated editor access every shared placed block already requires. The Assignment
Desk SHALL NOT expose a Read tab or a Pinned tab.

#### Scenario: Editing the Desk's own document requires the shared lock
- **WHEN** a player switches to the Assignment Desk's Editor tab
- **THEN** the dialog requests the same server-authoritative edit lock every other shared writing
  station requires, and the Editor view opens once granted

#### Scenario: No Read tab on the Assignment Desk
- **WHEN** a player views the Assignment Desk's nav column
- **THEN** no Read tab button is present, alongside Create Assignments, Sent Assignment History,
  Inbox, Editor, and Settings

#### Scenario: No Pinned tab on the Assignment Desk
- **WHEN** a player views the Assignment Desk's nav column
- **THEN** no Pinned tab button is present, alongside Create Assignments, Sent Assignment History,
  Inbox, Editor, and Settings

### Requirement: Create Assignments tab can pull tasks from the Desk's own document
When the staging slot is empty, the Create Assignments tab SHALL always show a "Create Tasks to
Assign" button in its empty task-list state. Activating that button SHALL enter the Assignment
Desk's existing Editor view through the same server-authoritative editor-lock behavior as the
Editor navigation button. When the Desk's own document has at least one eligible row, the empty
state SHALL also show the existing pull-from-Desk button after the create button; activating the
pull button SHALL populate the task list from the Desk's own document. The populated list SHALL
apply the same selection rules (independent per-row Selected checkboxes,
parent-selects-its-subtasks-once-on-select) as the existing staged-item task list.

#### Scenario: Create button is always present in the empty task-list state
- **WHEN** the staging slot is empty and the Create Assignments task list has no active source rows
- **THEN** the empty state shows a "Create Tasks to Assign" button below its hint text

#### Scenario: Create button enters the existing Editor through its lock path
- **WHEN** a player activates "Create Tasks to Assign"
- **THEN** the dialog requests the same server-authoritative editor access used by the Editor nav
  button and opens the Desk's local Editor view when access is granted

#### Scenario: Button appears only when there is something to pull
- **WHEN** the staging slot is empty, the Desk's own document has at least one eligible row, and the
  Desk source has not already been activated
- **THEN** the empty state shows "Create Tasks to Assign" first and the pull-from-Desk button after it

#### Scenario: Button is absent with nothing to pull
- **WHEN** the staging slot is empty and the Desk's own document has no eligible rows
- **THEN** the empty state shows its hint and "Create Tasks to Assign," with no pull-from-Desk button

#### Scenario: Pulling from the Desk populates the list with normal selection rules
- **WHEN** a player clicks the pull-from-Desk button
- **THEN** the task list populates with the Desk's own document's rows, each with an independent
  Selected checkbox, and selecting a parent row also selects its immediately-following subtask
  rows exactly once

### Requirement: A staged item always takes priority over the Desk's own document
Whenever the staging slot holds an item, the Create Assignments tab SHALL show that item's
document's rows, regardless of whether the Desk's own document was previously pulled in. Removing
the item SHALL reveal the Desk's own document's rows again if that source is still active and the
Desk's own document still has eligible rows.

#### Scenario: Staging an item overrides an active Desk-sourced list
- **WHEN** the Desk's own document is the active task source and a player places an item in the
  staging slot
- **THEN** the task list immediately switches to that item's document's rows

#### Scenario: Removing the staged item reveals the Desk's document again
- **WHEN** a player who previously pulled from the Desk removes the staged item, and the Desk's own
  document still has eligible rows
- **THEN** the task list shows the Desk's own document's rows again, without needing another click

### Requirement: The Create Assignments list live-tracks its active source document
Once the Desk's own document is the active task source, the Create Assignments tab's task list
SHALL reflect the current committed state of that document on every rebuild — the same guarantee
already given for a staged item's document.

#### Scenario: Editing the Desk's document updates the pulled-in list
- **WHEN** the Desk's own document is the active task source and a player commits an edit to it via
  the Editor tab
- **THEN** switching back to the Create Assignments tab shows the updated rows without needing to
  click the pull-from-Desk button again

### Requirement: "Delete from source on send" applies to the Desk's own document
When the active task source is the Desk's own document and "Delete from source on send" is enabled,
sending a batch SHALL remove the sent rows from the Desk's own document, the same way it already
removes them from a staged item's document.

#### Scenario: Sending with delete-from-source removes rows from the Desk's document
- **WHEN** the Desk's own document is the active task source, "Delete from source on send" is
  enabled, and a player sends a batch of selected rows
- **THEN** the assignments are created and the sent rows are removed from the Desk's own document

#### Scenario: A since-changed row is skipped, not an error
- **WHEN** a row selected for sending with delete-from-source enabled no longer exists in the Desk's
  own document by the time the server processes the removal (e.g. another player deleted it first)
- **THEN** the send still succeeds and every other matched row is removed; the missing row is
  silently skipped

