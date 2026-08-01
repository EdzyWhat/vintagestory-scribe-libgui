## MODIFIED Requirements

### Requirement: Complete a pinned task by stable identity
The system SHALL allow a player to mark a task complete addressed by `(DocId, TaskId)`, so that any
surface listing or showing a task (a HUD, a Pinned tab, or the Lectern read and editor views) can
complete a task without the task's position. Completing a task whose document is currently resolvable
SHALL toggle that task's completed state in the authoritative document (lock-free, not requiring or
acquiring the document's edit lock) and SHALL apply the acting player's completion policy
(Keep/Sink/Unpin/Delete). This completion behavior SHALL be uniform across every Scribe surface that
exposes a task checkbox — the read view, the editor view, the pinned view, and the HUD SHALL all
produce the same policy-applied result for the same player and task, with no surface applying a
different or reduced behavior. Completion is shared document state (it applies for every player),
distinct from the per-player pin.

#### Scenario: Complete a resolvable pinned task by identity
- **WHEN** a player completes a task addressed by `(DocId, TaskId)` whose document is loaded
- **THEN** that task's completed state is set in the authoritative document without acquiring the
  document's edit lock, and the player's completion policy is applied

#### Scenario: Completing while another player edits
- **WHEN** one player holds a document's edit lock and another player completes a task in that
  document by identity
- **THEN** the completion is applied without disturbing the editor's lock or edit

#### Scenario: Uniform completion across surfaces
- **WHEN** the same player with the same completion policy completes a given task from the read view,
  the editor view, the pinned view, or the HUD
- **THEN** the same policy-applied result occurs in every case (the Keep/Sink/Unpin/Delete effect),
  with no surface behaving differently

## ADDED Requirements

### Requirement: Completing a task under the Sink policy moves it to the document bottom
When a player completes a task while their completion policy is Sink, the system SHALL move that task
to the end of its source document's block order (a real reorder of the shared document, visible to
every viewer of that document), not merely a per-surface display sort. This SHALL apply to any
completed task, whether or not the acting player has pinned it. Completing under Keep SHALL leave the
task in place; the Sink reorder SHALL occur only on a transition into the done state (unchecking a task
SHALL NOT move it).

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
