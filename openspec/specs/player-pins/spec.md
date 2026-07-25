# player-pins

## Purpose

Per-player pins are references to specific tasks, keyed by stable identity `(DocId, TaskId)`
rather than by position or block coordinates. Pins are owned by a single player, persisted with
the save game, synced authoritatively to that player's client, and carry a last-known snapshot so
they can be displayed even when their source is unresolvable. This capability was created via spec
sync from change `add-pinned-task-foundation`.

## Requirements

### Requirement: Pins are per-player references to a specific task
The system SHALL record pins as a per-player set of references, each identifying a specific
task by its owning document's stable identifier and the task's stable identifier
(`(DocId, TaskId)`), never by the task's position within a document or by a block position. A
pin SHALL belong to exactly one player, and a player SHALL only ever see their own pins.

#### Scenario: A pin references a task by stable identity
- **WHEN** a player pins a task
- **THEN** the recorded pin identifies that task by its document's `DocId` and the task's
  `TaskId`, so the pin still resolves to the same task after the task is reordered within its
  document

#### Scenario: One player's pins are not visible to another
- **WHEN** two players each pin different tasks
- **THEN** each player's pin set contains only the tasks that player pinned, and neither
  player's set includes the other's pins

### Requirement: Pin and unpin a task by stable identity
The system SHALL allow a player to add a pin for a task and to remove one of their existing
pins, with both the add and the remove addressed by `(DocId, TaskId)` alone. Removing a pin
SHALL NOT require the task's owning block or item to be present, loaded, or resolvable — a
player SHALL be able to remove any pin they hold using only its identity. Adding a pin that
already exists for that player SHALL be idempotent (no duplicate); removing a pin that does not
exist SHALL be a safe no-op.

#### Scenario: Add a pin
- **WHEN** a player pins a task that is not currently in their pin set
- **THEN** the task is added to that player's pin set

#### Scenario: Adding an existing pin is idempotent
- **WHEN** a player pins a task already in their pin set
- **THEN** the pin set is unchanged (no duplicate entry is created)

#### Scenario: Remove a pin
- **WHEN** a player unpins a task currently in their pin set
- **THEN** the task is removed from that player's pin set

#### Scenario: Remove a pin whose source is gone or unloaded
- **WHEN** a player unpins a task by its `(DocId, TaskId)` while that task's owning block has
  been broken or its chunk is unloaded
- **THEN** the pin is removed from that player's pin set without requiring the block to be
  resolved

#### Scenario: Removing a non-existent pin is a safe no-op
- **WHEN** a player unpins a task that is not in their pin set
- **THEN** the pin set is unchanged and the operation does not fail

### Requirement: Complete a pinned task by stable identity
The system SHALL allow a player to mark a task complete addressed by `(DocId, TaskId)`, so that
a surface listing a player's pins (a HUD or a Pinned tab) can complete a task without the task's
position. Completing a task whose document is currently resolvable SHALL toggle that task's
completed state in the authoritative document (lock-free, mirroring the existing read-view
task-toggle) and SHALL NOT require or acquire the document's edit lock. Completion is shared
document state (it applies for every player), distinct from the per-player pin.

#### Scenario: Complete a resolvable pinned task by identity
- **WHEN** a player completes a task addressed by `(DocId, TaskId)` whose document is loaded
- **THEN** that task's completed state is set in the authoritative document, without acquiring
  the document's edit lock

#### Scenario: Completing while another player edits
- **WHEN** one player holds a document's edit lock and another player completes a task in that
  document by identity
- **THEN** the completion is applied without disturbing the editor's lock or edit

### Requirement: Completing a pinned task removes the pin unless the player opted out
When a player completes one of their own pinned tasks, the system SHALL remove that player's pin
for the task, unless that player has disabled the complete-to-unpin behavior in their per-player
settings. Removing the pin SHALL affect only the completing player; another player who pinned
the same task SHALL keep their pin (its snapshot then reflecting the now-completed state).

#### Scenario: Completing a pinned task unpins it for that player
- **WHEN** a player whose settings leave complete-to-unpin enabled completes one of their pinned
  tasks
- **THEN** the task is marked completed in the document and that player's pin for it is removed

#### Scenario: Opting out keeps the pin after completion
- **WHEN** a player who has disabled complete-to-unpin completes one of their pinned tasks
- **THEN** the task is marked completed and that player's pin for it remains (its snapshot
  reflecting the completed state)

#### Scenario: Completion unpins only for the completing player
- **WHEN** two players have each pinned the same task and one of them completes it
- **THEN** the completing player's pin is removed (per their setting) and the other player's pin
  remains, with its snapshot reflecting the completed state

### Requirement: An orphaned pin is removed when actioned
Because an orphaned pin has no live document to complete, actioning an orphaned pin (the same
"check it off" gesture that completes a live pinned task) SHALL remove that player's pin rather
than attempt to complete a task. This keeps the surface behavior uniform — checking a pinned
entry makes it leave the player's set — whether or not its source still exists.

#### Scenario: Checking off an orphaned pin removes it
- **WHEN** a player actions an orphaned pin (whose owning task has been permanently deleted)
- **THEN** that player's pin is removed, and no task-completion is attempted

### Requirement: Per-player settings are persisted and synced
The system SHALL maintain a small per-player settings record — including at least whether
completing a task unpins it — persisted with the save game and synced to the owning player. A
player's settings SHALL default to complete-to-unpin enabled. The settings record SHALL leave
room for additional per-player display preferences (for example, a collapsed-HUD flag) without a
format break.

#### Scenario: Settings default and persist
- **WHEN** a new player joins and later the world is saved and restarted
- **THEN** that player's settings exist with complete-to-unpin enabled by default and are
  restored after the restart

#### Scenario: A changed setting is synced to its owner
- **WHEN** a player changes one of their settings
- **THEN** the updated settings are persisted and re-delivered to that player's client

### Requirement: Pinning is lock-free and independent of the document
Pinning or unpinning a task SHALL NOT modify the task's document, acquire the document's edit
lock, or mark the document dirty. A player SHALL be able to pin or unpin a task regardless of
whether another player holds the document's edit lock.

#### Scenario: Pinning does not change the document
- **WHEN** a player pins or unpins a task
- **THEN** the task's document bytes are unchanged and no edit lock is required or acquired

#### Scenario: Pinning while another player edits
- **WHEN** one player holds a lectern's edit lock and another player pins a task in that lectern
- **THEN** the pin is recorded successfully without disturbing the editor's lock or edit

### Requirement: Pins are persisted per player and survive a restart
The system SHALL persist every player's pin set with the save game, so that a player's pins are
restored after the server (or single-player world) is restarted.

#### Scenario: Pins survive a restart
- **WHEN** a player pins a task, then the world is saved and the server is restarted
- **THEN** that player's pin set still contains the task after the restart

### Requirement: A player's pins are synced to that player's client
The system SHALL deliver a player their own pin set when they have fully joined, and SHALL
re-deliver an updated set to a player whenever their pins change (including snapshot or orphan
changes to a task they have pinned). A player's client SHALL NOT derive pins locally; the
server is authoritative.

#### Scenario: Initial delivery on join
- **WHEN** a player finishes joining the world
- **THEN** the server sends that player their current pin set

#### Scenario: Re-delivery on change
- **WHEN** a player's pin set changes (a pin added, removed, orphaned, or its snapshot refreshed)
- **THEN** the server re-sends that player their updated pin set

### Requirement: A pin carries a snapshot and a pinned-time
Each pin SHALL carry a last-known snapshot of its task's text and completed state, and the game
time at which it was pinned. The snapshot SHALL be refreshed from the authoritative document
whenever that document is edited, so a client that cannot currently resolve the task (its chunk
is unloaded) can still display the last-known content.

#### Scenario: Snapshot refreshes on edit
- **WHEN** a pinned task's text or completed state changes via a saved document edit
- **THEN** the corresponding pin's last-known snapshot is updated to match

### Requirement: A pin is soft-orphaned when its task is permanently deleted
When a pinned task is permanently removed — its owning block is broken or removed, or the task
is deleted from a saved edit — the system SHALL mark the corresponding pin orphaned and retain
its last-known snapshot, rather than deleting the pin. The system SHALL NOT orphan a pin merely
because its target is temporarily unresolvable (e.g. the owning block's chunk is unloaded).

#### Scenario: Orphan on block removal
- **WHEN** a lectern holding pinned tasks is broken or removed
- **THEN** each pin referencing that lectern's document is marked orphaned and keeps its
  last-known snapshot

#### Scenario: Orphan on task deletion
- **WHEN** a pinned task is deleted from its document via a saved edit
- **THEN** the pin referencing that task is marked orphaned and keeps its last-known snapshot

#### Scenario: An unloaded chunk does not orphan a pin
- **WHEN** the chunk containing a pinned task's owning block is unloaded
- **THEN** the pin is NOT marked orphaned and remains a normal (resolvable-when-loaded) pin

### Requirement: Per-player pin count is bounded
The system SHALL enforce an upper bound on the number of pins a single player may hold, so that
a malformed or hostile payload cannot grow a player's persisted/synced pin set without limit.

#### Scenario: Deserializing an over-limit pin set fails safely
- **WHEN** a persisted or received pin set claims more pins than the allowed maximum
- **THEN** deserialization reports failure rather than allocating the oversized set
