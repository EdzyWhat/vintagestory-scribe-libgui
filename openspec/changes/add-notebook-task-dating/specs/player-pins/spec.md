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
different or reduced behavior. The one exception is a task that repeats yearly (a plain task with a
date and Repeat yearly on): under the Delete policy it SHALL be treated as the Sink policy, so it is
never deleted by a completion, and every other policy (Keep, Sink, Unpin, UnpinSink) applies as the player
chose. Until the pin snapshot carries the repeat flag, two divergences are accepted: a completion whose
source document the server cannot resolve follows the player's policy, and the HUD predicts from the
policy alone, so a repeating task can fade out of the HUD under Delete and reappear when the server's
result arrives. Completion is shared
document state (it applies for every player), distinct from the per-player pin.

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

#### Scenario: A repeating task under the Delete policy
- **WHEN** a player whose completion policy is Delete completes a pinned task that repeats yearly
- **THEN** the task stays in the document, checked and moved to the bottom, and the player's pin for it
  remains
