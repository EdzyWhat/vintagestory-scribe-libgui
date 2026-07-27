## MODIFIED Requirements

### Requirement: Read-view checkbox toggles task done state without the editor lock
The read view's task checkbox SHALL be interactive: clicking it completes that task by stable
identity, applying the player's completion policy (Keep/Sink/Unpin/Delete) the same way every other
Scribe surface does. Because the read view holds no editor lock, completing SHALL be an always-allowed
server action that does NOT require acquiring the single-editor lock, applied server-authoritatively
and re-synced to all viewers. A player SHALL be able to complete a task from the read view even while
another player holds the editor lock. No other part of a read-view row SHALL be interactive except its
checkbox and its pin-toggle control (see the read-view pin-toggle requirement). The checkbox MAY be
rendered with LibGUI's stock checkbox widget; its skeuomorphic custom-glyph appearance is not required.

#### Scenario: Clicking a read-view checkbox completes with the player's policy
- **WHEN** the player clicks a task row's checkbox in the read view
- **THEN** that task is completed by identity, the player's completion policy is applied
  server-authoritatively (without requiring the editor lock) and synced back, and the checkbox
  updates to reflect the new state

#### Scenario: Completing works while someone else is editing
- **WHEN** a player clicks a read-view task checkbox while a different player holds the lectern's
  editor lock
- **THEN** the completion is still applied and synced, and is not rejected for lack of the lock

#### Scenario: The rest of a read-view row is inert except pin and checkbox
- **WHEN** the player clicks or hovers a read-view row anywhere other than its checkbox or its
  pin-toggle control
- **THEN** no edit field opens, no row reorder begins, and no other per-row control activates

## ADDED Requirements

### Requirement: Read-view rows expose a pin-toggle affordance
Each task row in the read view SHALL provide a control that toggles the task's pinned state for the
acting player, addressed by stable identity, mirroring the editor view's pin control. Text-section
rows SHALL NOT expose this control. The control's visual state SHALL reflect whether the task is
currently pinned for the player.

#### Scenario: Toggling pin from a read-view row
- **WHEN** the player activates a read-view task row's pin-toggle control
- **THEN** the task's pinned state for that player flips and the control's visual state reflects the
  new value

#### Scenario: Read-view text sections have no pin control
- **WHEN** a text-section row is composed in the read view
- **THEN** no pin-toggle control is present for that row

### Requirement: Every Lectern view completes a task with the player's completion policy
Completing a task via its checkbox SHALL apply the player's completion policy identically in all three
Lectern views (read, editor, and pinned), matching the pinned-task HUD. The editor view's checkbox
SHALL NOT be an exception: completing a task from the editor SHALL apply the same policy
(Keep/Sink/Unpin/Delete) by stable identity, rather than only toggling a local done flag. The policy
SHALL apply verbatim in every view with no per-view guard or confirmation — including a policy that
deletes the task or reorders it within the shared document.

#### Scenario: Editor checkbox applies the completion policy
- **WHEN** a player completes a task via its checkbox in the editor view
- **THEN** the player's completion policy is applied to that task by identity (the same result the
  read, pinned, and HUD surfaces produce), not merely a local done-flag toggle

#### Scenario: Same result regardless of view
- **WHEN** the same player with the same completion policy completes a given task from the read view,
  the editor view, the pinned view, or the HUD
- **THEN** the outcome is the same in every case (the policy's Keep/Sink/Unpin/Delete effect)

#### Scenario: Completing an editor task preserves other in-progress edits
- **WHEN** a player has unsaved text edits in some editor rows and completes a different task via its
  checkbox
- **THEN** the completion is applied and the other rows' in-progress text and the caret are preserved
  (not discarded by the reconciliation)

### Requirement: A divider separates each view's header from its scrolling list
Each Lectern view (read, editor, pinned) SHALL render a horizontal divider directly above its
scrolling task/note list, providing a straight visual edge between the view's header area and the
scroll region.

#### Scenario: Divider above the scroll area in every view
- **WHEN** any of the read, editor, or pinned views is shown
- **THEN** a horizontal divider is drawn directly above that view's scrolling list

### Requirement: The pinned view places its completion-policy picker above the list
In the pinned view, the completion-policy picker SHALL be positioned above the pinned-task list (as a
header), not below it as a footer. Changing the policy from this picker SHALL continue to update the
same per-player completion-policy preference that the settings surface edits.

#### Scenario: Policy picker sits above the pinned list
- **WHEN** the pinned view is shown
- **THEN** the completion-policy picker appears above the list of pinned tasks

#### Scenario: The pinned picker and the settings control stay in sync
- **WHEN** the player changes the completion policy from the pinned view's picker
- **THEN** the same per-player completion-policy preference is updated, and the settings surface
  reflects the same value
