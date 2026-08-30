## ADDED Requirements

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
flag SHALL become seen once the Assignee opens an Inbox view showing that assignment, unless the
Assignee immediately takes an action that moves it to another state (in which case the flag
becomes moot). The Seen flag SHALL NOT alter which transitions are valid from Unaccepted.

#### Scenario: A newly-sent assignment starts unseen
- **WHEN** an Assigner sends a new assignment
- **THEN** the assignment's state is Unaccepted and its Seen flag is false

#### Scenario: Opening the Inbox marks it seen
- **WHEN** the Assignee opens an Inbox view that shows a New (unseen) assignment and takes no
  further action before closing it
- **THEN** the assignment remains Unaccepted but its Seen flag becomes true

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
