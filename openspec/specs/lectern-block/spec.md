# lectern-block

## Purpose

TBD - created via spec sync from change `skeuomorphic-lectern-gui`. The base lectern-block
requirements are owned by the not-yet-synced `add-lectern-block` change; this file currently
holds only the requirements added by `skeuomorphic-lectern-gui`.

## Requirements

### Requirement: A lectern's document survives break and re-placement
Breaking a lectern SHALL carry its document — including the document's and tasks' stable
identifiers — onto the dropped item, and placing that item SHALL restore the document. The
document's content and identifiers SHALL be lost only when the dropped item itself disappears
(e.g. despawns), not merely because the block was broken. Because the document's identifier is
preserved, per-player pins referencing tasks in that document SHALL continue to resolve after
the block is re-placed.

#### Scenario: Break then re-place preserves the document
- **WHEN** a lectern holding a document is broken and the resulting item is placed again
- **THEN** the placed lectern's document has the same content and the same `DocId` and `TaskId`s
  as before it was broken

#### Scenario: Pins resolve after relocation
- **WHEN** a player has pinned a task in a lectern, and that lectern is broken and re-placed
  (possibly elsewhere)
- **THEN** the player's pin still resolves to the same task in the re-placed lectern

#### Scenario: Content is lost only on item disappearance
- **WHEN** the item dropped from a broken lectern disappears (despawns) rather than being placed
- **THEN** that document's content is gone (this is the only case in which breaking loses content)

### Requirement: The read-view checkbox completes a task by identity and honors complete-to-unpin
The lectern's read view SHALL let any viewer toggle a task's completed state without holding the
editor lock, addressing the task by its stable identity `(DocId, TaskId)`. When the viewer
completes a task they have pinned, the behavior SHALL follow the per-player complete-to-unpin
setting from the `player-pins` capability (removing that viewer's pin unless they opted out).
This is the in-lectern equivalent of the check-to-remove gesture a later HUD/Pinned-tab surface
will use.

#### Scenario: Read-view check completes by identity
- **WHEN** a viewer checks a task's checkbox in the lectern read view
- **THEN** that task's completed state is toggled in the authoritative document, addressed by its
  `(DocId, TaskId)`, without acquiring the editor lock

#### Scenario: Completing a pinned task from the read view unpins it (default)
- **WHEN** a viewer whose complete-to-unpin setting is enabled checks off a task they have pinned
- **THEN** the task is marked completed and that viewer's pin for it is removed

### Requirement: Pin a task from the GUI
The lectern's GUI SHALL let the player toggle whether a task is pinned for that player. Toggling
the pin SHALL record or remove a per-player pin referencing the task by stable identity, and
SHALL NOT modify the lectern's document or require its edit lock. The control SHALL NOT be
available for text-section blocks, and its visual state SHALL reflect whether the task is pinned
for the current player.

#### Scenario: Pin a task in the editor
- **WHEN** the player activates a task row's pin-toggle control in the editor view
- **THEN** a per-player pin for that task is recorded for the current player, and the control's
  visual state reflects that it is pinned

#### Scenario: Unpin a task in the editor
- **WHEN** the player activates a pinned task row's pin-toggle control again
- **THEN** the current player's pin for that task is removed, and the control's visual state
  reflects that it is no longer pinned

#### Scenario: Pinned state is per-player and persists across reload
- **WHEN** a player pins a task, then the world is saved and reloaded
- **THEN** reopening that lectern shows the task pinned for that player, and does not show it
  pinned for a different player who did not pin it
