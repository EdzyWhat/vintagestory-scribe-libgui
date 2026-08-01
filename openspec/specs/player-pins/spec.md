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

### Requirement: Pin messages carry DocId instead of BlockPos
All pin/unpin/complete-pin network packets SHALL carry the target task's `DocId` and
`TaskId` as the sole address fields (no `PosX/PosY/PosZ`). The server SHALL route these
packets through the host registry. This change does not alter any pin behavior visible to
the player.

#### Scenario: Pin packet routes by DocId
- **WHEN** a player pins or unpins a task in any Scribe GUI
- **THEN** the outbound packet contains the `DocId` (16-byte array) and `TaskId` only, and
  the server resolves the host via the registry

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

### Requirement: Player preferences are client-local and cross-world
The system SHALL maintain a small set of per-player display/behavior preferences — including at least a
**completion policy** (what happens to a task and its pin when the task is completed), the maximum
number of pinned tasks shown on the HUD, the HUD's screen anchor, its horizontal and vertical offsets,
its row width, a collapsed-HUD flag, a **HUD font-size scale**, and a **window font-size scale** —
stored **client-locally** for the player (not per world) so the same preferences apply across all of
that player's worlds, and persisted across sessions. All of these preferences SHALL be held in a
**single** client-local preference store; the mod SHALL NOT split them across more than one client
configuration file. These preferences SHALL NOT be synchronized to or authoritative on the server; they
are personal preferences with no shared-world effect. The completion policy SHALL be one of: *sink*
(the completed task stays pinned and is de-prioritized on the HUD), *keep* (the completed task stays
pinned and keeps its place — not de-prioritized), *unpin* (completion removes the pin), or *delete*
(completion deletes the underlying task). The font-size scales SHALL be multipliers that default to
`1.0` (no change), SHALL each be snapped to a discrete notch at 5% granularity within their range (i.e.
one of `0.80, 0.85, 0.90, … , 1.20`, shown as a percent), and SHALL be applied on top of the game's
global GUI scale rather than replacing it. The horizontal and vertical HUD offsets SHALL be interpreted
as nudges *relative to* the anchor's built-in pre-baked offset (so a stored `0` leaves the HUD at the
anchor's sensible default position, e.g. clear of the default top-right minimap), not as absolute
positions. Preferences SHALL default to the *sink* policy, a maximum HUD row count of 3, and a
font-size scale of `1.0`. On read, the system SHALL clamp each numeric preference (the maximum HUD rows, the row width, the
horizontal and vertical offsets to ±300 pixels, and both font-size scales) to its sane range (snapping
each font-size scale to its nearest allowed 5% notch) and treat an unrecognized
completion-policy or HUD-anchor value as its default, so a hand-edited or corrupted preference file
cannot produce an invalid state. The preference store SHALL leave room for additional personal
preferences without a format break.

#### Scenario: Preferences default and persist across sessions
- **WHEN** a player who has never changed a preference plays, and later restarts the game
- **THEN** their preferences read as the *sink* completion policy, a maximum HUD row count of 3, and
  font-size scales of `1.0`, and any change they made persists across the restart

#### Scenario: Preferences are shared across a player's worlds
- **WHEN** a player changes a preference while in one world and then joins a different world
- **THEN** the changed preference applies in the second world (the preference is client-local, not
  per-world)

#### Scenario: All preferences live in one client store
- **WHEN** a player changes any Scribe preference, including a font-size scale
- **THEN** the change is written to the single client-local preference store, and no separate client
  configuration file holds a competing copy of that preference

#### Scenario: A corrupted preference value is normalized on read
- **WHEN** the stored preferences carry an out-of-range numeric value (maximum HUD rows, row width, an
  offset beyond ±300, or a font-size scale outside `0.80`–`1.20`) or an unrecognized completion policy or
  HUD anchor
- **THEN** each out-of-range numeric value is clamped to its allowed range (each font-size scale snapped
  to its nearest 5% notch) and each unrecognized enumerated value falls back to its default (the
  completion policy to *sink*, the HUD anchor to its default corner)

### Requirement: Completing a pinned task from the HUD has a brief undoable window with animated feedback
When a player completes (checks off) a pinned task from the HUD, the system SHALL hold the completion for
a brief window before it takes effect on the server, during which the player MAY undo it by unchecking the
task; an undo within the window SHALL leave the task and its pin exactly as they were, with no completion
having been applied. All completion policies SHALL share the same window duration. The system SHALL give
animated feedback during the window that reflects the pending outcome: a completion under a policy that
removes the task or its pin SHALL visibly fade the affected row, and a completion under a policy that
keeps-and-sinks the task SHALL visibly settle the row toward its sunk position. The task's checkbox SHALL
remain operable throughout the window so the undo is always available. When the window elapses under a
policy that removes the task or its pin, the affected row SHALL collapse its height to zero — so the rows
below it move up smoothly to fill the space — and SHALL be removed from the HUD only after that collapse
completes, rather than disappearing in a single frame.

#### Scenario: Undo within the window applies no completion
- **WHEN** a player checks off a pinned task on the HUD and unchecks it before the window elapses
- **THEN** no completion is applied — the task's done-state and the player's pin are unchanged, and no
  removal or sink occurs

#### Scenario: Completion applies after the window
- **WHEN** a player checks off a pinned task on the HUD and does not undo before the window elapses
- **THEN** the completion is applied under the player's current policy (sink, keep, unpin, or delete)

#### Scenario: The window gives animated feedback
- **WHEN** a completion is pending within its window
- **THEN** the row animates to preview the outcome (a fade for unpin/delete, a settle toward the bottom
  for sink), while its checkbox stays operable for undo

#### Scenario: A removing completion collapses the row before it leaves
- **WHEN** the undoable window elapses for a completion under a policy that removes the task or its pin
  (unpin or delete)
- **THEN** the faded row's height collapses smoothly to zero and the rows below move up to meet it, and
  the row is removed from the HUD only after that collapse finishes

#### Scenario: A re-pin during or after a collapse is not left invisible
- **WHEN** a task's row is collapsing (or has just collapsed) on the HUD and that same task is pinned
  again before the HUD reconciles with the server
- **THEN** the task reappears in the HUD at full height, with no residual collapse hiding it

### Requirement: Edit a pinned task's text by stable identity

The system SHALL allow a player to change a pinned task's text addressed by `(DocId, TaskId)`, so a
surface listing a player's pins (the Pin Tab) can edit a task's text without knowing the
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
  identity through the Pin Tab
- **THEN** the text change is applied lock-free without disturbing the editor's lock, and the pin
  snapshot is updated

#### Scenario: Blank edit text is rejected
- **WHEN** a player edits a pinned task's text to a value that is empty or whitespace-only
- **THEN** the operation reports failure, the authoritative document is left unchanged, and no snapshot
  update that would blank the pin is applied

### Requirement: Delete a task by stable identity as a standalone action

The system SHALL allow a player to delete a task addressed by `(DocId, TaskId)` as a first-class
standalone action (not only as a side effect of a completion policy), so the Pin Tab can
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

The system SHALL allow a player to reorder their own pin list, addressed by pin identity, so the
Pin Tab can arrange pins in a player-chosen order. Reordering SHALL permute only that player's
per-player pin list; it SHALL NOT change any document's block order and SHALL NOT affect any other
player's pin list. The reordered list SHALL be persisted per-player (in the existing per-player pin
store) and re-synced to the owning player so the new order survives a restart and is reflected on every
surface that reads that player's pins.

#### Scenario: Reorder persists and re-syncs
- **WHEN** a player reorders their pin list through the Pin Tab
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

### Requirement: The sink completion order is shown on the Pinned view, not only the HUD
When a player's completion policy is *sink*, the resting display order in which completed tasks sink
below not-completed ones SHALL be applied to the Lectern's Pinned view, not only to the pinned-task HUD.
The Pinned view SHALL render the player's pins with completed pins ordered below not-completed pins
(preserving pin order within each group), using the same ordering rule the HUD uses. Completing a pinned
task under *sink* from any surface SHALL therefore move that task toward the bottom of the Pinned view,
matching what the HUD shows.

#### Scenario: A sunk task appears at the bottom of the Pinned view
- **WHEN** a player whose policy is *sink* completes a pinned task
- **THEN** that task is shown below the not-completed pins in the Pinned view (matching the HUD's sink
  order), rather than staying in its prior position

#### Scenario: Pinned view and HUD agree on sink order
- **WHEN** the same player views their pins on both the HUD and the Pinned view after a *sink* completion
- **THEN** both surfaces show the completed task sunk below the not-completed pins in the same order

### Requirement: A sink completion of an owned task reorders the owner's Read and Edit views
When a player completes a task under the *sink* policy and that task's document is resolvable, the task
SHALL be moved to the bottom of that document's order (the existing document reorder), and the acting
player's open Read and Edit views of that document SHALL reflect the new order promptly rather than
requiring the player to reopen or switch views. This makes "drop to bottom" visible on the same surface
the player completed the task from, for a task in a document they can edit.

#### Scenario: Read view reflects a sink reorder without reopening
- **WHEN** a player whose policy is *sink* completes a task from the Read view of a resolvable document
- **THEN** the completed task moves to the bottom of the Read list without the player reopening or
  switching views

#### Scenario: Editor reflects a sink reorder in place
- **WHEN** a player whose policy is *sink* completes a task from the editor view
- **THEN** the task moves to the bottom of the editor list while other rows' in-progress edits are
  preserved

