## REMOVED Requirements

### Requirement: An unaccepted Task Notice has no assignment-store record
**Reason**: The first playtest of the Task Notice found the "Sent History shows nothing until
Accept" behavior reads as broken rather than intentional — the Assigner has no way to confirm a
notice was actually sent. Replaced by the requirements below: a `ScribeAssignmentStore` record
exists from the moment a notice is sealed, visible to the Assigner as "Sent," while the Assignee's
Inbox stays silent until the physical item reaches their inventory.
**Migration**: No player-facing migration — this only affects notices sent after this change
ships. In-flight notices sealed before this change (no store record yet) keep working exactly as
before: Accept still creates a fresh Accepted record via the existing mechanism, Decline still
consumes the item with no record. There is nothing to backfill.

## ADDED Requirements

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

## MODIFIED Requirements

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
