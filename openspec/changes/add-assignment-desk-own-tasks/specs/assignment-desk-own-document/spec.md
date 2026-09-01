## ADDED Requirements

### Requirement: Assignment Desk exposes its own document via Read and Editor tabs
The Assignment Desk's block entity already owns a full `ScribeDocument` (inherited from the
writing-station base every Notebook/Lectern/Tablet uses). The system SHALL expose it through
Read and Editor nav tabs on the Assignment Desk's dialog, with the same affordances as any other
writing station's Read/Editor views (completion checkbox, pin, delete, reorder, Tracker/Link/Craft
rows) and the same server-lock-gated editor access every shared placed block already requires. The
Assignment Desk SHALL NOT expose a Pinned tab.

#### Scenario: Reading the Desk's own document
- **WHEN** a player switches to the Assignment Desk's Read tab
- **THEN** the Desk's own document renders with the same row affordances (checkbox, pin, delete,
  reorder, Tracker/Link/Craft display) as any other surface's Read view

#### Scenario: Editing the Desk's own document requires the shared lock
- **WHEN** a player switches to the Assignment Desk's Editor tab
- **THEN** the dialog requests the same server-authoritative edit lock every other shared writing
  station requires, and the Editor view opens once granted

#### Scenario: No Pinned tab on the Assignment Desk
- **WHEN** a player views the Assignment Desk's nav column
- **THEN** no Pinned tab button is present, alongside Create Assignments, Sent Assignment History,
  Inbox, Read, Editor, and Settings

### Requirement: Create Assignments tab can pull tasks from the Desk's own document
When the staging slot is empty and the Desk's own document has at least one eligible row, the
Create Assignments tab SHALL show a button below its empty-state hint that, when clicked, populates
the task list from the Desk's own document. The list SHALL apply the same selection rules
(independent per-row Selected checkboxes, parent-selects-its-subtasks-once-on-select) as the
existing staged-item task list.

#### Scenario: Button appears only when there is something to pull
- **WHEN** the staging slot is empty and the Desk's own document has at least one eligible row,
  and the Desk source has not already been activated
- **THEN** the Create Assignments tab's empty state shows the pull-from-Desk button below its hint
  text

#### Scenario: Button is absent with nothing to pull
- **WHEN** the staging slot is empty and the Desk's own document has no eligible rows
- **THEN** the Create Assignments tab's empty state shows only its hint text, no button

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
