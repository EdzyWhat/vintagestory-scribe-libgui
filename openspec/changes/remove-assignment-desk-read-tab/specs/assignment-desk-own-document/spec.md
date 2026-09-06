## MODIFIED Requirements

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
