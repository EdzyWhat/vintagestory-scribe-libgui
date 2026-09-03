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
remain operable throughout the window so the undo is always available. During the window the just-checked
row SHALL be held in its current position (not yet sunk), which follows from the completion not yet being
applied on the server. When the window elapses under a policy that removes the task or its pin, the
affected row SHALL collapse its height to zero — so the rows below it move up smoothly to fill the space —
and SHALL be removed from the HUD only after that collapse completes, rather than disappearing in a single
frame. When the window elapses under a keeps-and-sinks policy, the task SHALL settle to its resting sunk
position (below the not-completed pins); the resting order SHALL be a pure function of completion state,
so un-completing that task after the window SHALL return it to its prior position rather than holding it
at the bottom.

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

#### Scenario: Un-completing a sunk task after the window returns it to its prior position
- **WHEN** a keeps-and-sinks completion's window has elapsed (the task has settled to the bottom) and the
  player later un-completes that task
- **THEN** the task returns to its prior position among the not-completed pins, rather than remaining at
  the bottom for the session

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

A drag SHALL only be able to reorder a pin onto a target of the **same** `Depth`: a depth-0 pin's
drop target SHALL be another depth-0 pin, and a depth-1 pin's drop target SHALL be another depth-1
pin. Dropping on a pin of a different depth SHALL be rejected as a no-op — no permutation occurs and
no message is sent — the same validity check `task-subtasks`' editor reorder uses, applied here to
the pin list's own `Depth` values and position rather than a document's.

A drag of a depth-0 pin SHALL reorder that pin together with its owned-run cluster — the contiguous
run of depth-1 pins immediately following it in the current pin list — as one unit, so a pinned
parent's reorder can never strand its already-pinned children. Dropping the cluster onto one of its
own children SHALL NOT change order. A drag of a depth-1 pin remains a leaf (siblings do not follow).

While a drag is in progress on the Pin Tab, the grip's drop-target arrow (▶) SHALL render on a pin
row if and only if that row is a valid same-depth drop target for the pin currently being dragged,
mirroring the editor's arrow rule.

#### Scenario: Reorder persists and re-syncs
- **WHEN** a player reorders their pin list through the Pin Tab
- **THEN** their pin list is permuted into the new order, persisted per-player, and re-synced to their
  client, and the same order is restored after a restart

#### Scenario: Reordering does not touch document block order
- **WHEN** a player reorders their pins that reference tasks in one or more documents
- **THEN** no document's block order changes and no other player's pin list is affected — only the
  reordering player's own pin list order changes

#### Scenario: A depth-1 pin cannot be dropped among depth-0 pins
- **WHEN** the player drags a `Depth` 1 pin and releases it over a `Depth` 0 pin
- **THEN** no permutation occurs and no `ScribeReorderPinsMessage` is sent

#### Scenario: A depth-0 pin cannot be dropped among a different parent's depth-1 pins
- **WHEN** the player drags a `Depth` 0 pin and releases it over a `Depth` 1 pin that is not one of
  its own owned-run cluster
- **THEN** no permutation occurs and no message is sent

#### Scenario: Dragging a pinned parent keeps its pinned children with it
- **WHEN** a player drags a pinned depth-0 parent that has one or more already-pinned depth-1
  children immediately following it, and drops it elsewhere among the depth-0 pins
- **THEN** the parent and its pinned children move together, in their prior relative order, and no
  pinned child is left behind at the parent's old position

#### Scenario: The drop-target arrow only appears on same-depth pin rows
- **WHEN** the player is mid-drag on a `Depth` 0 pin and the pointer moves over a `Depth` 1 pin
  outside its own cluster
- **THEN** that row does not show the drop-target arrow

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
The Pinned view and the HUD SHALL render one and the same order, both derived from the single
persisted per-player pin list with the same ordering rule (completed pins ordered below not-completed
pins, preserving pin-list order within each group). Neither surface SHALL apply a surface-only or
session-only ordering overlay on top of that shared order. Completing a pinned task under *sink* from
any surface SHALL therefore move that task toward the bottom of both the Pinned view and the HUD in
the same order, and un-completing it SHALL return it to its prior position on both.

#### Scenario: A sunk task appears at the bottom of the Pinned view
- **WHEN** a player whose policy is *sink* completes a pinned task
- **THEN** that task is shown below the not-completed pins in the Pinned view (matching the HUD's sink
  order), rather than staying in its prior position

#### Scenario: Pinned view and HUD agree on sink order
- **WHEN** the same player views their pins on both the HUD and the Pinned view after a *sink* completion
- **THEN** both surfaces show the completed task sunk below the not-completed pins in the same order

#### Scenario: The Pinned view and HUD stay in sync across documents and sessions
- **WHEN** a player pins tasks from several documents, reorders them on the Pin Tab, completes and
  un-completes some, and rejoins in a later session
- **THEN** the HUD and the Pin Tab render the pins in the same order at every point, because both read
  the one persisted per-player pin list and apply the same ordering rule with no divergent overlay

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

### Requirement: The pinned HUD renders from persistent content updated by reconcile
The pinned-task HUD SHALL render its pin list from a persistent content `StatefulWidget` updated via
`SetState` on the pin-push, tick-expiry, and toggle paths, rather than rebuilding the whole HUD tree
via `ForceRebuild()` on each change. The 0⇄1-pin self-open/close remains a host concern
(`TryOpen`/`TryClose`), distinct from the in-place reconcile of the row list. HUD rows SHALL be keyed
by stable TaskId so that hover, animation controllers, and pointer-capture are preserved across an
in-place update.

#### Scenario: A pin push updates the HUD in place
- **WHEN** the server pushes a pin-set change that keeps the HUD open (still one or more pins)
- **THEN** the HUD updates its row list via `SetState`, preserving hover state and any in-flight row
  animation, rather than tearing down and recreating the whole HUD tree

#### Scenario: The HUD still opens and closes at the pin-count boundary
- **WHEN** the player's pin count crosses 0⇄1
- **THEN** the HUD opens or closes via the host `TryOpen`/`TryClose` path, independent of the in-place
  row-list reconcile

### Requirement: Pinning a subtask inserts it under its pinned parent
When a player pins a depth-1 row whose parent (the depth-0 row that owns its contiguous run in the
source document) is already in that player's pin list, the new pin SHALL be inserted immediately
after that parent's HUD cluster: the parent pin, then any already-contiguous pins whose tasks are in
that owned run. The child SHALL NOT be appended at the end of the list. Parent identity SHALL come
from the source document, never from “any depth-0 pin from the same notebook.” If the parent is not
pinned, or the source document cannot be resolved, the pin SHALL insert per the player's **Pin
Insert** setting (Top or Bottom), the same as an unrelated depth-0 pin. Pinning a child SHALL NOT
auto-pin the parent.

#### Scenario: Pinning a child under a pinned parent
- **WHEN** the player has pinned a Craft parent and then pins one of its ingredient children
- **THEN** the child pin sits directly under that parent in the pin list, not at the end

#### Scenario: Parent not pinned appends
- **WHEN** the player pins an ingredient child whose Craft parent is not pinned
- **THEN** the child is inserted at the Top or Bottom of the pin list per the player's Pin Insert
  setting (Bottom by default, matching the historical always-append behavior); the parent is not
  pinned automatically

### Requirement: Pinning a parent gathers its already-pinned children
When a player pins a depth-0 row, the pin SHALL be inserted per the player's **Pin Insert** setting
(Top or Bottom), then any of that player's existing pins whose `TaskId` is in that parent's current
document owned run SHALL be moved to sit immediately after it, preserving those children's relative
order. This clustering of already-pinned children happens regardless of which edge the parent pin
itself was inserted at.

#### Scenario: Pinning the parent later clusters children
- **WHEN** two ingredient children are already pinned and the player then pins their parent
- **THEN** the parent appears in the pin list with those two children directly under it in their
  prior relative order, at whichever edge (Top or Bottom) the player's Pin Insert setting places the
  parent

### Requirement: Notes can be pinned
A Text (note) row SHALL be pinnable by the same per-player `(DocId, TaskId)` pin as other rows.
Unpinning a note SHALL work from the Pin Tab and from the source surface's pin control. Completing
a note SHALL NOT apply (notes have no done flag).

#### Scenario: Pin a note from the editor
- **WHEN** the player activates the pin control on a Text row
- **THEN** that note is added to the player's pin set

### Requirement: A pin's snapshot carries enough assignment provenance to render its tooltip without the source document
When a pinned task is an accepted assignment, the pin's persisted/synced snapshot SHALL include the
assigner's player uid, the date the assignment was sent, and the date it was accepted — so the Pin
Tab can render the assignment marker's tooltip using only the snapshot, without resolving the task's
source document (which may not be loaded).

#### Scenario: Pinning an accepted assignment captures its provenance
- **WHEN** a player pins a task that is an accepted assignment
- **THEN** the pin's snapshot records the assigner's uid, the assigned date, and the accepted date
  alongside the existing accepted-assignment flag

#### Scenario: A pre-existing pin blob still loads
- **WHEN** the pin store reads a pin-list blob written before this field was added
- **THEN** it loads successfully, with the new fields defaulting to empty/absent for that pin

