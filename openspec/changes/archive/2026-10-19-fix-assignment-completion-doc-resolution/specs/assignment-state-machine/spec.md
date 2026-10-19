## MODIFIED Requirements

### Requirement: Completed is derived from the task's own completion flag, never a manual transition
An Accepted assignment SHALL automatically become Completed when its underlying task's completed
flag becomes true, and SHALL NOT offer Completed as a manually-selectable state-change action.
Uncompleting the underlying task while its assignment is Completed is out of scope for this
change (Completed is treated as terminal per the transition requirement above). This derivation
SHALL occur whenever the task's completed flag is set true by any completion path (read view,
editor view, pinned view, or HUD), regardless of whether the task's owning document happens to be
resolvable at that moment — the canonical assignment record is addressed by the task's own stable
id, not by locating the document.

#### Scenario: Checking off the task completes the assignment
- **WHEN** the Assignee marks an Accepted assigned task's completion checkbox true
- **THEN** the assignment's state becomes Completed with no separate state-change action taken

#### Scenario: No manual Completed button exists
- **WHEN** an assignment is in any state
- **THEN** no available state-change action is labeled or behaves as a direct transition to
  Completed

#### Scenario: Completing a pinned assignment task whose document is not currently resolvable
- **WHEN** the Assignee completes an Accepted assigned task from the HUD or Pin Tab at a moment
  when the task's owning document (e.g. a Notebook not currently in the Assignee's inventory)
  cannot be resolved
- **THEN** the assignment's state still becomes Completed, and both the Assignee's Inbox and the
  Assigner's Sent Assignment History reflect Completed
