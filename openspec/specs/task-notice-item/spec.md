# task-notice-item

## Purpose

The Task Notice is a hand-carried, physical delivery item for an out-of-range or offline
assignment: a blank notice is a plain crafting supply, a sealed notice carries one pending
assignment's task data until its recipient accepts or declines it.

## Requirements

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
Notebook and Tablet, rendered in a locked/read-only state, showing that notice's document content
plus two explicit action buttons: Accept and Decline. The dialog's chrome SHALL render as a
parchment/scroll backing rather than a plain LibGUI window, and its task rows' completion
checkboxes — inert in this read-only dialog — SHALL render in a visibly disabled style rather than
looking identical to an interactive checkbox. When Accept requires picking a destination Scribe
item from more than one eligible carried candidate, that picker SHALL render in its own row above
the Accept/Decline buttons, and both buttons SHALL size to fit their text rather than stretching
to fill the dialog's width.

#### Scenario: Opening a sealed Task Notice shows a read-only view with Accept/Decline
- **WHEN** the Assignee right-clicks a sealed Task Notice they are holding
- **THEN** the document dialog opens showing that notice's content with no edit controls, and
  Accept and Decline buttons are both present

#### Scenario: Read-only checkboxes look disabled, not interactive
- **WHEN** the Assignee views a Task Notice's task rows
- **THEN** each row's completion checkbox renders in a visibly muted/disabled style, distinct from
  an interactive checkbox elsewhere in Scribe

#### Scenario: Multi-candidate Accept picker never clips the action buttons
- **WHEN** the Assignee has more than one eligible carried Scribe item and taps Accept
- **THEN** the destination picker appears in its own row above the Decline/Accept buttons, and
  neither button is pushed off the visible dialog area

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
Declining a Task Notice SHALL consume the item and SHALL transition its existing
`ScribeAssignmentStore` record (created at send time, revealed to the Assignee at receipt) to
Declined. The Assigner SHALL receive no active notification (no toast, no highlight) that the
notice was declined — their Sent Assignment History passively reflects the Declined state the
same way it reflects any other assignment's outcome.

#### Scenario: Declining a received notice updates history passively, with no active notification
- **WHEN** the Assignee declines a Task Notice they have received
- **THEN** the item is consumed, the existing record transitions to Declined, and the Assigner
  receives no active notification — their Sent Assignment History simply now shows it as Declined

### Requirement: A sent notice appears in the Create Assignments tab's output slot, never auto-inserted
When the Assigner sends an assignment in "Send a Notice" mode, the system SHALL place the sealed,
populated Task Notice into the Create Assignments tab's output slot rather than the Assigner's
inventory. The Assigner SHALL retrieve it from that slot themselves whenever they choose.

#### Scenario: Sending places the notice in the output slot, not inventory
- **WHEN** the Assigner sends an assignment while "Send a Notice" is selected
- **THEN** the sealed Task Notice appears in the tab's output slot, and the Assigner's inventory is
  unchanged until they drag it out themselves

### Requirement: A sent Task Notice creates an assignment-store record immediately, visible to the Assigner as Sent
When the Assigner sends an assignment in "Send a Notice" mode, the system SHALL create one
`ScribeAssignmentStore` record per task in a new `Sent` state, using the same identity already
embedded in the sealed notice's document. The Assigner's Sent Assignment History SHALL show each
such record immediately, labeled "Sent."

#### Scenario: Sending a notice creates a Sent record immediately
- **WHEN** the Assigner sends an assignment while "Send a Notice" is selected
- **THEN** a `ScribeAssignmentStore` record is created in the `Sent` state for each task, and the
  Assigner's Sent Assignment History shows it labeled "Sent" without waiting for Accept

### Requirement: The Assignee's Inbox stays silent until the physical notice reaches their inventory
A record in the `Sent` state SHALL NOT appear in the Assignee's Inbox. The system SHALL detect
when a sealed notice carrying a `Sent`-state record enters the Assignee's own inventory (the same
per-player scan already used by the proximity-signal heartbeat) and, on detection, transition that
record to `Unaccepted`, stamping a received date. From that point the record behaves exactly like
any other Unaccepted assignment.

#### Scenario: Inbox is silent while the notice is only nearby, not yet carried
- **WHEN** a sealed notice addressed to a player is sitting in a chest or on the ground, not yet
  in that player's own inventory
- **THEN** the Assignee's Inbox shows no record for it, even though the Assigner's Sent History
  already shows it as "Sent"

#### Scenario: Inbox reveals the assignment once the notice is actually carried
- **WHEN** the Assignee picks up a sealed notice addressed to them into their own inventory
- **THEN** the corresponding record transitions from `Sent` to `Unaccepted`, stamped with the date
  it was received, and the Assignee's Inbox now shows it labeled "Received"

### Requirement: Blank and sealed Task Notices render visually distinguishable models
The system SHALL render a blank Task Notice and a sealed Task Notice with visually distinct
models, so a player can tell the two states apart by looking at the item (in hand, on the
ground, or in a slot) without needing to read its tooltip.

#### Scenario: A blank notice looks different from a sealed one
- **WHEN** a player holds or views a blank Task Notice and, separately, a sealed Task Notice
- **THEN** the two render with visibly different models

#### Scenario: A notice's model updates immediately when it changes state
- **WHEN** a blank Task Notice becomes sealed (or a sealed one's document is otherwise cleared)
- **THEN** its rendered model updates to match its new state without requiring the item to be
  re-picked-up, re-equipped, or the game restarted
