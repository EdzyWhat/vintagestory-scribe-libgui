# assignment-state-machine

## Purpose

TBD - created via spec sync from change `add-assignment-and-quest-support`. This capability
covers the assignment lifecycle itself: its states, the transitions legal from each state and
who may take them, and how Accept resolves a placement target.
## Requirements
### Requirement: Assignment state transitions are validated by actor and current state
An assignment SHALL be in exactly one of six states at a time: Unaccepted, Accepted, Declined,
Cancelled, Discarded, or Completed. The system SHALL only permit the following transitions, each
restricted to the named actor:
- From **Unaccepted**: the Assignee may Accept (→ Accepted) or Decline (→ Declined); the
  Assigner may Cancel (→ Cancelled).
- From **Accepted**: the Assignee may Discard (→ Discarded); the underlying task's own
  completion toggle automatically transitions the assignment to Completed (see the derived-
  completion requirement below). The Assigner has no available action from this state.
- Declined, Cancelled, Discarded, and Completed are terminal: no further transition is valid
  from any of them.

Any transition not listed above (including any actor attempting an action not granted to them
from the current state) SHALL be rejected.

#### Scenario: Assignee accepts an Unaccepted assignment
- **WHEN** the Assignee chooses Accept on an Unaccepted assignment
- **THEN** the assignment's state becomes Accepted

#### Scenario: Assigner cannot cancel an Accepted assignment
- **WHEN** the Assigner attempts to cancel an assignment that is already Accepted
- **THEN** the action is rejected and the assignment remains Accepted

#### Scenario: Terminal states accept no further transition
- **WHEN** any actor attempts any action on an assignment whose state is Declined, Cancelled,
  Discarded, or Completed
- **THEN** the action is rejected and the assignment's state is unchanged

### Requirement: Completed is derived from the task's own completion flag, never a manual transition
An Accepted assignment SHALL automatically become Completed when its underlying task's completed
flag becomes true, and SHALL NOT offer Completed as a manually-selectable state-change action.
Uncompleting the underlying task while its assignment is Completed is out of scope for this
change (Completed is treated as terminal per the transition requirement above).

#### Scenario: Checking off the task completes the assignment
- **WHEN** the Assignee marks an Accepted assigned task's completion checkbox true
- **THEN** the assignment's state becomes Completed with no separate state-change action taken

#### Scenario: No manual Completed button exists
- **WHEN** an assignment is in any state
- **THEN** no available state-change action is labeled or behaves as a direct transition to
  Completed

### Requirement: Deleting an Accepted assigned task performs the Discard transition
Using the normal task-delete affordance on an assigned task that is in the Accepted state SHALL
transition its assignment to Discarded (in addition to removing the task from the Assignee's
document), rather than removing it without updating the assignment's recorded state.

#### Scenario: Delete on an accepted assigned task discards it
- **WHEN** the Assignee deletes an assigned task whose assignment state is Accepted
- **THEN** the task is removed from the Assignee's document and the assignment's state becomes
  Discarded, visible as such in the Assigner's read-only record

### Requirement: The Assigner retains read-only visibility after Accepted
Once an assignment leaves the Unaccepted state, the Assigner's Assignment tab SHALL continue to
show the assignment and its current state (including a later automatic Completed) as a record of
what was sent, without offering any state-change action.

#### Scenario: Assigner sees an Accepted assignment's outcome
- **WHEN** the Assignee later completes or discards a task the Assigner sent
- **THEN** the Assigner's Assignment tab reflects the current state (Completed or Discarded) with
  no action button available to them

### Requirement: New (unseen) is a flag on Unaccepted, not a separate state
An Unaccepted assignment SHALL carry a Seen flag, defaulting to unseen ("New") at creation. The
flag SHALL become seen the moment the Assignee's Inbox view for that assignment becomes the active
view on any Inbox-capable surface — whether reached by clicking an Inbox nav button, by opening a
surface whose Inbox view is its default or only view (the standalone Inbox block), or by any other
path that makes the Inbox view active — not only via an explicit nav-button click, unless the
Assignee immediately takes an action that moves it to another state (in which case the flag becomes
moot). The Seen flag SHALL NOT alter which transitions are valid from Unaccepted.

#### Scenario: A newly-sent assignment starts unseen
- **WHEN** an Assigner sends a new assignment
- **THEN** the assignment's state is Unaccepted and its Seen flag is false

#### Scenario: Opening the Inbox marks it seen
- **WHEN** the Assignee opens an Inbox view that shows a New (unseen) assignment and takes no
  further action before closing it
- **THEN** the assignment remains Unaccepted but its Seen flag becomes true

#### Scenario: Opening the standalone Inbox block marks it seen
- **WHEN** the Assignee opens the standalone Inbox block's dialog, which lands directly on its
  Inbox view as that block's only view, showing a New (unseen) assignment
- **THEN** the assignment's Seen flag becomes true, the same as if the Assignee had reached the
  Inbox view via a nav-button click on any other surface

#### Scenario: Re-opening the standalone Inbox block also marks it seen
- **WHEN** the Assignee closes and re-opens the standalone Inbox block's dialog while it still
  shows a New (unseen) assignment
- **THEN** the assignment's Seen flag becomes true on that re-open, the same as on first open

### Requirement: Accepted-task placement resolves held surface, then inventory, then blocks Accept
When the Assignee accepts an assignment, the system SHALL place the resulting task on: (1) a
Scribe document currently hosted by the item in the Assignee's active hand slot, if any; else
(2) a Scribe document-bearing item elsewhere in the Assignee's inventory — if more than one
exists, the Assignee SHALL be prompted to choose which; else (3) if no eligible document exists
anywhere, the Accept control SHALL be disabled (not merely rejected on click) with an
explanatory tooltip.

#### Scenario: Accept places the task on the currently held surface
- **WHEN** the Assignee accepts an assignment while holding a Notebook
- **THEN** the accepted task is added to that Notebook's document

#### Scenario: Accept prompts a choice among multiple inventory candidates
- **WHEN** the Assignee accepts an assignment while holding no Scribe document item, but has two
  eligible Scribe document items elsewhere in inventory
- **THEN** the Assignee is shown a picker to choose which one receives the task

#### Scenario: Accept is disabled with no eligible surface
- **WHEN** the Assignee has no Scribe document-bearing item held or in inventory
- **THEN** the Accept control is disabled and shows an explanatory tooltip rather than allowing
  a click that fails

### Requirement: A terminal assignment record can be permanently deleted by its Assigner or Assignee
An assignment record whose state is terminal (Declined, Cancelled, Discarded, or Completed) MAY
be deleted independently by its Assigner and by its Assignee — each deletion removes the record
from only that party's own view (the Assigner's Sent Assignment History, or the Assignee's
Inbox, respectively), never both. This holds even when the same player is both the Assigner and
the Assignee of a self-assignment: the two sides are deleted independently, one request at a
time. Deletion is distinct from every transition in the state machine: it never moves the record
to a further state and SHALL NOT be offered or performed on a record whose state is Unaccepted or
Accepted. Any deletion attempt by a player who is neither the record's Assigner nor its Assignee
SHALL be rejected and the record SHALL remain unchanged. Once BOTH the Assigner and the Assignee
have deleted their own side, the record no longer exists at all.

#### Scenario: The Assignee deletes a terminal record
- **WHEN** the Assignee of a Declined, Cancelled, Discarded, or Completed assignment requests its
  deletion
- **THEN** the record no longer appears in that Assignee's Inbox
- **AND** the record still appears in the Assigner's Sent Assignment History, unchanged

#### Scenario: The Assigner deletes a terminal record
- **WHEN** the Assigner of a Declined, Cancelled, Discarded, or Completed assignment requests its
  deletion
- **THEN** the record no longer appears in that Assigner's Sent Assignment History
- **AND** the record still appears in the Assignee's Inbox, unchanged

#### Scenario: A self-assignment's two sides are deleted independently
- **WHEN** a player who is both the Assigner and the Assignee of a terminal self-assignment
  deletes it from their Inbox only
- **THEN** the record no longer appears in that player's Inbox
- **AND** the record still appears in that same player's Sent Assignment History, unchanged

#### Scenario: A record is fully removed once both sides have deleted it
- **WHEN** the Assignee has already deleted their own side of a terminal record and the Assigner
  then deletes their own side too
- **THEN** the record no longer exists in the store at all

#### Scenario: Deletion is rejected on a non-terminal record
- **WHEN** either party requests deletion of an assignment whose state is Unaccepted or Accepted
- **THEN** the deletion is rejected and the record remains unchanged

#### Scenario: Deletion is rejected for an uninvolved player
- **WHEN** a player who is neither the Assigner nor the Assignee of a terminal assignment
  requests its deletion
- **THEN** the deletion is rejected and the record remains unchanged

### Requirement: Accept-time placement records a destination label
When the Assignee's Accept places the resulting task onto a resolved Scribe document (per
the placement requirement above), the system SHALL also record a short display label
identifying that destination item — `<Type> "<Title>"` (e.g. `Notebook "Book of Nick"`),
falling back to the item's bare name when the document has no meaningful title — on the
assignment record, alongside its existing accepted-date stamp. When placement does not
occur (the Accept control's no-eligible-surface case, or a defensive no-op on an
unresolvable/no-capacity target), no label is recorded.

#### Scenario: Accept records the destination label
- **WHEN** the Assignee accepts an assignment and it is placed into a Notebook titled
  "Book of Nick"
- **THEN** the assignment record's destination label is set to `Notebook "Book of Nick"`

#### Scenario: An assignment titled with the default title falls back to the bare item name
- **WHEN** the Assignee accepts an assignment and it is placed into a Notebook that still
  carries the default (never-renamed) title
- **THEN** the assignment record's destination label is the bare item name, not the
  default title

#### Scenario: No label when placement does not occur
- **WHEN** an Accept request resolves to an ineligible or no-capacity target and the
  assignment stays Accepted but unplaced
- **THEN** no destination label is recorded for that assignment

### Requirement: The accepted-assignment marker renders after the row checkbox and discloses its provenance on hover
On every surface that renders an accepted assignment's task row with the assignment marker icon
(Read view, Editor view, Pin Tab), the marker SHALL render immediately after the row's completion
checkbox (i.e. to the checkbox's right, not before it), and hovering it SHALL show a two-line
tooltip: the assigner's display name and the date the assignment was sent, and the date it was
accepted.

#### Scenario: Marker position
- **WHEN** a task row is an accepted assignment on the Read view, Editor view, or Pin Tab
- **THEN** the marker icon renders after the row's checkbox in reading order, not before it

#### Scenario: Marker tooltip content
- **WHEN** a player hovers the marker icon on an accepted assignment's row
- **THEN** a tooltip appears showing the assigner's display name and the assignment's sent date on
  its first line, and the date it was accepted on its second line

#### Scenario: Tooltip matches ambient illumination
- **WHEN** the marker tooltip is shown under a low-light ambient shade
- **THEN** the tooltip's bubble and text are shaded the same way every other row/nav tooltip in the
  dialog already is, rather than rendering full-bright

