## ADDED Requirements

### Requirement: The Task Notice item carries a Scribe document via the existing round-trip mechanism
The system SHALL provide a new item, the Task Notice, whose populated/sealed form carries exactly
one assignment's task data using the existing `IScribeDocumentItem`/`ScribeDocumentAttributes`
round-trip already used by the Notebook and Tablet, requiring no new serialization mechanism. A
blank Task Notice carries no document data and is a plain, stackable crafting-supply item; a
sealed Task Notice is unique-data and does not stack with any other item, including another sealed
notice.

#### Scenario: A blank Task Notice stacks like any plain resource
- **WHEN** two blank Task Notices are placed in the same inventory slot
- **THEN** they stack together, since neither carries unique document data

#### Scenario: A sealed Task Notice never stacks
- **WHEN** a sealed Task Notice is placed in a slot alongside another sealed Task Notice, blank or
  sealed
- **THEN** they do not stack, since each sealed notice carries its own unique document data

### Requirement: Blank Task Notices are crafted from a knife, parchment, and a reed
The system SHALL provide a crafting recipe consuming one knife (as a tool, not consumed), one
parchment, and one reed, yielding 8 blank Task Notices, using the existing placeholder scroll item
model.

#### Scenario: Crafting yields 8 blank notices
- **WHEN** a player crafts the Task Notice recipe with a knife, parchment, and a reed
- **THEN** they receive 8 blank Task Notices

### Requirement: A Task Notice opens via the existing held-item right-click document convention
Right-clicking a Task Notice while held SHALL open the same Scribe document dialog used by the
Notebook and Tablet, rendered in a locked/read-only state (no edit controls), showing that notice's
document content plus two explicit action buttons: Accept and Decline.

#### Scenario: Opening a sealed Task Notice shows a read-only view with Accept/Decline
- **WHEN** the Assignee right-clicks a sealed Task Notice they are holding
- **THEN** the document dialog opens showing that notice's content with no edit controls, and
  Accept and Decline buttons are both present

### Requirement: An unaccepted Task Notice has no assignment-store record
Until a Task Notice is Accepted, the assignment it carries SHALL NOT exist as a
`ScribeAssignmentStore` record — the physical item is the sole record. Consequently: no digital
Cancel action SHALL be offered to the Assigner for it (physically retrieving or destroying the item
is the equivalent action); the Assigner's Sent Assignment History SHALL show nothing for it until
Accept; and the Assigner receives no notification if the item is later lost or destroyed before
Accept.

#### Scenario: No Sent History entry before Accept
- **WHEN** an Assigner has sent a Task Notice that has not yet been Accepted
- **THEN** their Sent Assignment History shows no record for it

#### Scenario: No Cancel control exists for an unaccepted notice
- **WHEN** an Assigner wants to withdraw a Task Notice they have already sent
- **THEN** no digital Cancel action is available to them; retrieving or destroying the physical
  item is the only way to withdraw it

### Requirement: Accept converts a Task Notice into a normal tracked assignment
When the Assignee accepts a Task Notice, the system SHALL create a `ScribeAssignmentStore` record
for it beginning in the Accepted state, placing the resulting task via the existing
`AcceptedIntoLabel` bind-to-first-legal-item mechanism used by in-range assignments. From that
point forward the assignment SHALL behave identically to an in-range assignment: Complete and
Discard sync through the existing mechanism regardless of either party's distance or online
status, and no further physical item is required.

#### Scenario: Accepting a notice creates a normal Accepted record
- **WHEN** the Assignee accepts a Task Notice
- **THEN** a `ScribeAssignmentStore` record is created for it in the Accepted state, placed via the
  same mechanism used for in-range assignments

#### Scenario: Post-accept behavior matches an in-range assignment
- **WHEN** an assignment accepted from a Task Notice is later completed or discarded
- **THEN** that outcome syncs to the Assigner through the same existing mechanism used for
  in-range assignments, with no additional physical item involved

### Requirement: Decline consumes a Task Notice with no record and no notification
Declining a Task Notice SHALL consume the item and SHALL NOT create any `ScribeAssignmentStore`
record. The Assigner SHALL receive no notification that the notice was declined.

#### Scenario: Declining a notice leaves no trace for the Assigner
- **WHEN** the Assignee declines a Task Notice
- **THEN** the item is consumed, no assignment record is created, and the Assigner's Sent
  Assignment History shows nothing for it

### Requirement: A sent notice appears in the Create Assignments tab's output slot, never auto-inserted
When the Assigner sends an assignment in "Send a Notice" mode, the system SHALL place the sealed,
populated Task Notice into the Create Assignments tab's output slot rather than the Assigner's
inventory. The Assigner SHALL retrieve it from that slot themselves whenever they choose.

#### Scenario: Sending places the notice in the output slot, not inventory
- **WHEN** the Assigner sends an assignment while "Send a Notice" is selected
- **THEN** the sealed Task Notice appears in the tab's output slot, and the Assigner's inventory is
  unchanged until they drag it out themselves
