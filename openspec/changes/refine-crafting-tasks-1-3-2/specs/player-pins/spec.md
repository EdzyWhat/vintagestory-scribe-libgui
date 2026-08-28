## ADDED Requirements

### Requirement: Pinning a subtask inserts it under its pinned parent
When a player pins a depth-1 row whose parent (the depth-0 row that owns its contiguous run in the
source document) is already in that player's pin list, the new pin SHALL be inserted immediately
after that parent's HUD cluster: the parent pin, then any already-contiguous pins whose tasks are in
that owned run. The child SHALL NOT be appended at the end of the list. Parent identity SHALL come
from the source document, never from “any depth-0 pin from the same notebook.” If the parent is not
pinned, or the source document cannot be resolved, the pin SHALL append. Pinning a child SHALL NOT
auto-pin the parent.

#### Scenario: Pinning a child under a pinned parent
- **WHEN** the player has pinned a Craft parent and then pins one of its ingredient children
- **THEN** the child pin sits directly under that parent in the pin list, not at the end

#### Scenario: Parent not pinned appends
- **WHEN** the player pins an ingredient child whose Craft parent is not pinned
- **THEN** the child is appended; the parent is not pinned automatically

### Requirement: Pinning a parent gathers its already-pinned children
When a player pins a depth-0 row, the pin SHALL be appended, then any of that player's existing pins
whose `TaskId` is in that parent's current document owned run SHALL be moved to sit immediately after
it, preserving those children's relative order.

#### Scenario: Pinning the parent later clusters children
- **WHEN** two ingredient children are already pinned and the player then pins their parent
- **THEN** the parent appears in the pin list with those two children directly under it in their prior relative order

### Requirement: Notes can be pinned
A Text (note) row SHALL be pinnable by the same per-player `(DocId, TaskId)` pin as other rows.
Unpinning a note SHALL work from the Pin Tab and from the source surface's pin control. Completing
a note SHALL NOT apply (notes have no done flag).

#### Scenario: Pin a note from the editor
- **WHEN** the player activates the pin control on a Text row
- **THEN** that note is added to the player's pin set

## MODIFIED Requirements

### Requirement: Completing a task under the Sink policy moves it to the document bottom
When a player completes a task while their completion policy is Sink, the system SHALL move that task
to the end of its source document's block order (a real reorder of the shared document, visible to
every viewer of that document), not merely a per-surface display sort. This SHALL apply to any
completed task, whether or not the acting player has pinned it. Completing under Keep SHALL leave the
task in place; the Sink reorder SHALL occur only on a transition into the done state (unchecking a task
SHALL NOT move it). When Subtask Behavior is **Bound to parent** and the completed row is a parent, the
owned run SHALL sink as **one contiguous block** (parent first, then its depth-1 rows in their prior
order), not as N independent `MoveTaskToBottom` calls.

#### Scenario: Sink moves a completed task to the document end
- **WHEN** a player whose policy is Sink completes a task that is not already last in its document
- **THEN** that task is moved to the end of the document's block order, and the new order is visible to
  every viewer of that document

#### Scenario: Sink applies to an unpinned task
- **WHEN** a player whose policy is Sink completes a task they have not pinned
- **THEN** the task is still moved to the document bottom (the policy is not limited to pinned tasks)

#### Scenario: Keep leaves order unchanged
- **WHEN** a player whose policy is Keep completes a task
- **THEN** the task's position in the document is unchanged

#### Scenario: Bound Sink keeps parent and children together
- **WHEN** Subtask Behavior is Bound to parent and a player whose policy is Sink completes a parent with two depth-1 children
- **THEN** the three rows appear together at the document bottom, parent first, still contiguous
