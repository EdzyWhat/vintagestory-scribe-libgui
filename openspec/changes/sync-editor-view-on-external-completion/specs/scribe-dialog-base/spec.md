## MODIFIED Requirements

### Requirement: An external resync landing mid-edit does not drop a legitimately-local in-flight row
When an authoritative server resync arrives while the editor is reconciling and the player has a
freshly-created, not-yet-persisted row in progress, the editor SHALL NOT prune that local in-flight
row against the server snapshot. This preserves the existing guard (never drop the focused row; never
drop an empty task, which is never persisted by design) under the reconciling update path.

Additionally, when an external resync reflects a **completion** applied to a task that still exists in
the open editor's scratch document, the editor SHALL propagate that completion into scratch rather than
leaving scratch stale: it SHALL update the task's done-state to match the authoritative document, and it
SHALL apply the completion policy's document effect that keeps the task present — specifically the
`Sink`/`UnpinSink` move-to-bottom reorder — live in the open editor. This propagation SHALL NOT overwrite
any row's in-progress unsaved text, and the live reorder SHALL preserve the actively-edited row's caret
position and in-progress text and SHALL NOT leak or lose cross-row focus, reusing the editor's
reconcile-with-stable-identity machinery. Because scratch is thereby made consistent with the live
document, a subsequent autosave flush (`ApplyEdit` whole-document replace) SHALL NOT revert the external
completion or its reorder.

#### Scenario: A just-created local row survives an async server resync
- **WHEN** the player creates a new task row and, before it is persisted, an authoritative server
  resync arrives that does not contain that row
- **THEN** the local in-flight row is retained (not pruned), and its focus and caret are undisturbed

#### Scenario: An external completion under Keep updates the open editor's checkbox
- **WHEN** the editor is open on a document and the player completes one of its tasks from the HUD while
  their completion policy is `Keep` (the task stays in place)
- **THEN** that task's row in the open editor reflects the completion (checkbox checked) without the
  player reopening the editor, and no other row's in-progress text or caret is disturbed

#### Scenario: An external completion under Sink reorders the row live in the open editor
- **WHEN** the editor is open on a document and the player completes one of its tasks from the HUD while
  their completion policy is `Sink` (or `UnpinSink`)
- **THEN** that task's row is marked done and moved to the bottom of the open editor's list live,
  matching the Read and Pinned views, while the actively-edited row keeps its caret and in-progress text
  and focus is not lost or leaked

#### Scenario: A later editor flush does not revert the external completion
- **WHEN** an external completion has been propagated into the open editor's scratch and the player then
  makes an unrelated edit that triggers an autosave flush
- **THEN** the flushed whole-document write carries the external completion (done-state and any sink
  reorder) rather than reverting it, so the completion is not silently lost
