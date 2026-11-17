## MODIFIED Requirements

### Requirement: The Pin Tab exposes the completion-policy control

The Pin Tab SHALL surface the "on completing a task" completion-policy control (the same
`ScribeCompletionPolicy` choice — keep-and-sink, keep-in-place, unpin, or delete — offered by the Scribe
Settings window), because the Pin Tab is where that policy's effect on checking a task off is most
directly observed. The control on the Pin Tab and the control in the Settings window SHALL edit the one
shared per-player completion-policy preference; changing it in either place SHALL update the same stored
value and take effect for both.

#### Scenario: Changing the policy from the Pin Tab
- **WHEN** a player changes the completion policy using the control on the Pin Tab
- **THEN** the shared per-player completion-policy preference is updated (and persisted client-local),
  and the Settings window reflects the same value

#### Scenario: The policy governs Pin Tab completions
- **WHEN** a player completes a task from the Pin Tab under a given completion policy
- **THEN** the completion is applied under that policy (kept-and-sunk, kept-in-place, unpinned, or the
  task deleted) exactly as the same policy governs completions elsewhere, except that a task that repeats
  yearly is never deleted: under the delete policy it is kept-and-sunk.
  When the server cannot resolve the task's source document, the completion follows the player's policy
  until the pin snapshot carries the repeat flag (an accepted limitation)
