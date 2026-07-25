## ADDED Requirements

### Requirement: Edit a pinned task's text by stable identity

The system SHALL allow a player to change a pinned task's text addressed by `(DocId, TaskId)`, so a
surface listing a player's pins (the pin-editor pagelet) can edit a task's text without knowing the
task's position or block coordinates. The edit SHALL be best-effort write-through: when the task's
document is currently resolvable, the new text SHALL be written into the authoritative document
lock-free (mirroring the existing complete-by-identity path) and SHALL NOT require or acquire the
document's edit lock; regardless of whether the source is resolvable, the pin's last-known text
snapshot SHALL be updated to the new text and re-synced to the owning player. The edit SHALL respect
the document model's content invariant (blank or whitespace-only text is rejected and leaves the
document unchanged).

#### Scenario: Edit a resolvable pinned task's text
- **WHEN** a player edits a pinned task's text addressed by `(DocId, TaskId)` whose document is loaded
- **THEN** the task's text is changed in the authoritative document without acquiring the document's
  edit lock, and the player's pin snapshot for it is updated to the new text and re-synced

#### Scenario: Editing while another player holds the edit lock
- **WHEN** one player holds a document's edit lock and another player edits a task in that document by
  identity through the pin editor
- **THEN** the text change is applied lock-free without disturbing the editor's lock, and the pin
  snapshot is updated

#### Scenario: Blank edit text is rejected
- **WHEN** a player edits a pinned task's text to a value that is empty or whitespace-only
- **THEN** the operation reports failure, the authoritative document is left unchanged, and no snapshot
  update that would blank the pin is applied

### Requirement: Delete a task by stable identity as a standalone action

The system SHALL allow a player to delete a task addressed by `(DocId, TaskId)` as a first-class
standalone action (not only as a side effect of a completion policy), so the pin-editor pagelet can
delete a task directly. When the task's document is resolvable, the task SHALL be removed from the
authoritative document lock-free (reusing the existing delete-from-reader path) and SHALL NOT require
or acquire the document's edit lock; the player's pin for that task SHALL be removed from their pin set
and the updated set re-synced. Deleting SHALL be addressed by identity alone and SHALL be a safe no-op
if the pin is already gone.

#### Scenario: Delete a resolvable task by identity
- **WHEN** a player deletes a task addressed by `(DocId, TaskId)` whose document is loaded
- **THEN** the task is removed from the authoritative document without acquiring the edit lock, the
  player's pin for it is removed, and the updated pin set is re-synced

#### Scenario: Standalone delete removes the pin even when the source is unresolvable
- **WHEN** a player deletes a pinned task by identity while its owning document's chunk is unloaded
- **THEN** the player's pin is removed and the operation does not fail, without requiring the block to
  be resolved

### Requirement: Reorder the per-player pin list

The system SHALL allow a player to reorder their own pin list, addressed by pin identity, so the pin-
editor pagelet can arrange pins in a player-chosen order. Reordering SHALL permute only that player's
per-player pin list; it SHALL NOT change any document's block order and SHALL NOT affect any other
player's pin list. The reordered list SHALL be persisted per-player (in the existing per-player pin
store) and re-synced to the owning player so the new order survives a restart and is reflected on every
surface that reads that player's pins.

#### Scenario: Reorder persists and re-syncs
- **WHEN** a player reorders their pin list through the pin editor
- **THEN** their pin list is permuted into the new order, persisted per-player, and re-synced to their
  client, and the same order is restored after a restart

#### Scenario: Reordering does not touch document block order
- **WHEN** a player reorders their pins that reference tasks in one or more documents
- **THEN** no document's block order changes and no other player's pin list is affected — only the
  reordering player's own pin list order changes

### Requirement: Mutating an unloaded document's source is best-effort and snapshot-only

Because no mechanism exists to force-load an unloaded chunk, any pin mutation that would write through
to a source document (editing text or deleting a task by identity) SHALL degrade to updating the
per-player pin state only when the source document is unresolvable, exactly as the existing
delete-on-complete policy behaves when its source is unloaded. In this case the pin snapshot (for an
edit) or the pin's presence (for a delete) SHALL be updated and re-synced, the operation SHALL NOT
fail or crash, and the source document SHALL remain unchanged until it is next loaded. The system SHALL
NOT force-load a chunk to satisfy a pin mutation.

#### Scenario: Editing text of a pin whose source is unloaded
- **WHEN** a player edits a pinned task's text by identity while the task's owning document's chunk is
  unloaded
- **THEN** the pin's text snapshot is updated and re-synced, the operation does not fail, and the
  source document is not modified (and is not force-loaded) until it is next loaded

#### Scenario: Deleting a task whose source is unloaded
- **WHEN** a player deletes a pinned task by identity while its owning document's chunk is unloaded
- **THEN** the player's pin is removed and re-synced, the operation does not fail, and no chunk is
  force-loaded to attempt the source deletion
