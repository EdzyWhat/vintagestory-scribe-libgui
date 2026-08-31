# lectern-block

## Purpose

TBD - created via spec sync from change `skeuomorphic-lectern-gui`. The base lectern-block
requirements are owned by the not-yet-synced `add-lectern-block` change; this file currently
holds only the requirements added by `skeuomorphic-lectern-gui`.
## Requirements
### Requirement: Lectern registers in the host registry (replaces position-based routing)
The Lectern block entity SHALL register itself in the `DocId → IScribeDocumentHost` registry
on `Initialize` and unregister on `OnBlockRemoved`. All packets previously sent with
`PosX/PosY/PosZ` routing fields SHALL now carry only the `DocId` (16-byte array). No
player-visible behavior changes; this is purely an internal routing change.

#### Scenario: Lectern is reachable by DocId after chunk load
- **WHEN** a chunk containing a Lectern is loaded and the Lectern initializes
- **THEN** the host registry contains an entry for that Lectern's `DocId`

#### Scenario: Lectern is not reachable after block removal
- **WHEN** a Lectern block is broken or removed
- **THEN** the host registry no longer contains an entry for that Lectern's `DocId`

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

### Requirement: Delete a block from the GUI
The lectern's GUI SHALL let the player delete a block (a task or a text section) from the
editor view, removing it from the document while preserving the order of the remaining blocks.
The deletion SHALL be applied through the existing server-authoritative edit path and persist
across reload.

#### Scenario: Delete a task in the editor
- **WHEN** the player activates a row's delete control in the editor view
- **THEN** that block is removed from the lectern's document, the remaining blocks keep their
  relative order, and the change is applied server-authoritatively and re-synced

#### Scenario: Deletion persists across reload
- **WHEN** a block is deleted, then the world is saved and reloaded
- **THEN** reopening that lectern shows the block still gone

#### Scenario: Deleting the last block leaves an empty document
- **WHEN** the player deletes the only remaining block
- **THEN** the document becomes empty and the editor shows its empty-state hint, with no error

### Requirement: Reorder blocks from the GUI
The lectern's editor view SHALL let the player reorder blocks by dragging a row to a new
position with the mouse. Dropping a row SHALL move it to the drop position, shifting the
intervening blocks, and SHALL be applied through the existing server-authoritative edit path
and persist across reload. A drag that is released on the row's original position SHALL be a
no-op.

#### Scenario: Drag a row to a new position
- **WHEN** the player presses on a row's drag handle, moves the pointer over a different row
  position, and releases
- **THEN** the dragged block moves to that position, the other blocks shift to accommodate it,
  and the new order is applied server-authoritatively and re-synced

#### Scenario: Reorder persists across reload
- **WHEN** blocks are reordered, then the world is saved and reloaded
- **THEN** reopening that lectern shows the blocks in the reordered sequence

#### Scenario: Dropping in place changes nothing
- **WHEN** the player begins a drag and releases without moving to a different position
- **THEN** the document order is unchanged and no edit is sent

### Requirement: Craftable, placeable lectern block

The system SHALL provide a lectern block that reuses the vanilla "lecturn-book-open"
appearance (plain wood, not an "aged"/scavenged variant, since the block is meant to be
crafted from ordinary materials rather than found), can be obtained (via a crafting
recipe or creative inventory), placed in the world, and broken to be recovered.

#### Scenario: Place and break the lectern

- **WHEN** a player places the lectern block and later breaks it
- **THEN** the block appears in the world when placed and is returned to the player's inventory when broken

### Requirement: Open the lectern's editor

The system SHALL let a player open the lectern's editing GUI by right-clicking the block.

#### Scenario: Open by right-click

- **WHEN** a player right-clicks a placed lectern
- **THEN** the Scribe editing GUI opens showing that lectern's document (its tasks and text sections)

### Requirement: Edit the document through the GUI

The system SHALL let the player add tasks, edit block text, toggle task completion, delete
blocks, and reorder blocks from the lectern's GUI. Text-section blocks are a document
capability (see `task-note-document`) but the lectern's GUI does not expose a way to create
one; text-section blocks created by another means still render, edit, and delete normally.

#### Scenario: Add and complete a task

- **WHEN** the player adds a task "Build a forge" and then marks it complete in the GUI
- **THEN** the lectern's document contains that task shown as completed

#### Scenario: Reorder blocks in the GUI

- **WHEN** the player enters reorder mode and drags a block to a new position
- **THEN** the lectern's document reflects the new block order after saving

### Requirement: Collapsible, gating-ready tool panel

The GUI's tools/options SHALL live in a panel that can be collapsed/hidden, and each option
SHALL support a visibility condition so future tiers can gate options by technology. In v1
no options are gated (all are visible). Overall text size SHALL be adjustable as a
client-side display preference that is NOT stored in the document and NOT synced to others.

#### Scenario: Collapse the tool panel

- **WHEN** the player collapses the tool panel
- **THEN** the options are hidden and the document content remains visible

#### Scenario: Text size is a local preference

- **WHEN** one player changes the text size
- **THEN** the change affects only that player's display and does not alter the stored document or other players' views

### Requirement: Edit-mode toggle

The GUI SHALL provide a keybind that toggles editing on and off, so the dialog can rest in
a non-editing state until the player chooses to write (an immersive "take out the pen" beat).

#### Scenario: Toggle into editing

- **WHEN** the player presses the edit-toggle keybind while the lectern GUI is open and not editing
- **THEN** editing controls become active for that player

### Requirement: Server-authoritative persistence

The system SHALL treat the server as the source of truth for a lectern's document: edits
made in the client GUI are sent to the server, applied there, saved with the world, and
survive a save/reload.

#### Scenario: Edits persist across reload

- **WHEN** a player edits a lectern's blocks (tasks and text sections), then the world is saved and reloaded
- **THEN** reopening that lectern shows the same blocks in the same order

#### Scenario: Client edits are not trusted directly

- **WHEN** the client GUI changes a block (a task or text section)
- **THEN** the change is sent to the server and only takes lasting effect after the server applies it (a client that fails to reach the server does not permanently change the stored document)

### Requirement: One editor at a time

The system SHALL allow only one player to have a given lectern's editor open at a time.
While one player has it open, another player who tries to open the same lectern SHALL be
refused with a message such as "Only one person can use the lectern at a time," and the
lock SHALL be released when the first player closes the editor (or disconnects).

#### Scenario: Second player is refused while it's in use

- **WHEN** player A has a lectern's editor open and player B right-clicks the same lectern
- **THEN** player B's editor does not open and player B sees a "one person at a time" message

#### Scenario: Lock releases on close

- **WHEN** player A closes the lectern's editor and player B then right-clicks it
- **THEN** player B's editor opens normally

#### Scenario: Lock releases if the holder disconnects

- **WHEN** player A has the editor open and then disconnects without closing it
- **THEN** the lectern becomes available for another player to open

### Requirement: Multiplayer synchronization

The system SHALL synchronize a lectern's document to players in multiplayer, so a change
made by one player is seen by another who opens the same lectern.

#### Scenario: Two players see the same content

- **WHEN** player A edits a lectern and player B then opens the same lectern
- **THEN** player B sees player A's changes

### Requirement: Each lectern is independent

The system SHALL key each lectern's document to that block's position, so different
lecterns hold different documents.

#### Scenario: Separate lecterns hold separate documents

- **WHEN** two lecterns are placed and each is given different content
- **THEN** each lectern shows only its own document

### Requirement: All Lectern views render a document header above the central region
Every Lectern view (read, edit, pin) SHALL render a `BuildDocumentHeader(editable: bool)`
widget above the central region. The header is editable only in edit view (`editable: true`);
in all other views it is display-only (`editable: false`). The header widget is composed
from the title text (and, when editable, the pencil icon or inline input).

#### Scenario: Header rendered in all views
- **WHEN** a player navigates between the read, edit, and pin tabs
- **THEN** the title header remains visible above the central region in each view

#### Scenario: Editable flag controls pencil presence
- **WHEN** `BuildDocumentHeader(editable: false)` is composed
- **THEN** no pencil icon is included in the header layout

### Requirement: Lectern block entity persists and exposes a guestbook
`BlockEntityScribeLectern` SHALL hold a `GuestbookStore` instance alongside its document store.
The guestbook SHALL be serialized into tree attributes under a distinct key (e.g. `"guestbook"`)
and SHALL NOT overlap with the document's attribute keys.

#### Scenario: Guestbook serializes independently of the document
- **WHEN** `ToTreeAttributes` is called on a lectern with both document content and guestbook entries
- **THEN** each is stored under its own key and neither overwrites the other

#### Scenario: Guestbook deserializes on load
- **WHEN** `FromTreeAttributes` is called on a freshly-loaded block entity
- **THEN** the guestbook entries match what was written before the chunk was saved

### Requirement: Lectern GUI open triggers a server-side visitor record
When the Lectern GUI is opened by a client, the client SHALL send a "record visitor" packet
to the server. The server SHALL delegate to `GuestbookStore.TryAddEntry` and, if a new entry
was added, SHALL mark the block entity dirty and send an updated guestbook sync packet back to
the opening client.

#### Scenario: GUI open causes server to write entry and respond
- **WHEN** a client opens the Lectern GUI
- **THEN** the server records the visitor (if not duplicate) and sends the current guestbook
  state back to the opening client

#### Scenario: No dirty-mark on duplicate open
- **WHEN** a client opens the Lectern GUI a second time on the same in-game day
- **THEN** the server does not call `MarkDirty` (no new entry was written)

### Requirement: The Lectern's tooltip shows its document title
The Lectern SHALL display a title line on its tooltip, formatted as `Title: "<title>"` with the
title wrapped in double quotes, in BOTH forms: the placed block's look-at tooltip (sourced from the
block entity's live document title) and the block item's held/inventory hover (sourced from the
document carried on the ItemStack). When the document has no meaningful title (its stored title is
the model default `"Untitled"`, or the item carries no document yet), the line SHALL still appear
using the placeholder `Title: "(untitled)"`. The title line SHALL sit alongside the standard
mod/name lines, not replace them.

#### Scenario: Titled lectern shows its quoted title on hover
- **WHEN** a player looks at a placed Lectern whose document title is "Welcome to Ravenwood"
- **THEN** the block tooltip includes a line reading `Title: "Welcome to Ravenwood"`

#### Scenario: Untitled lectern shows the placeholder
- **WHEN** a player looks at a placed Lectern whose document has never been given a title (title is the default)
- **THEN** the block tooltip includes a line reading `Title: "(untitled)"`

#### Scenario: Lectern item in inventory shows its carried title
- **WHEN** a player hovers a Lectern block item in their inventory that was broken/picked up with the title "Welcome to Ravenwood"
- **THEN** the item tooltip includes a line reading `Title: "Welcome to Ravenwood"`

### Requirement: The Lectern does not advertise combustion stats
The Lectern SHALL NOT display burn/combustion information (burn temperature, burn duration) on its
tooltip, because those stats are irrelevant to how the block is used.

#### Scenario: No burn lines on the Lectern tooltip
- **WHEN** a player views the Lectern's tooltip (as a placed block or in inventory)
- **THEN** no "Burn temperature" or "Burn duration" line is shown

### Requirement: The Lectern exposes an Inbox nav button
The Lectern's dialog SHALL show a nav button (alongside its existing Guestbook/History/etc. nav
buttons) that switches its view to the shared Inbox tab (`inbox-tab` capability), so a player can
view and act on their assignments without leaving the Lectern. The Lectern SHALL NOT gain a
create-and-send affordance — only the Assignment Desk can create assignments.

#### Scenario: Opening the Inbox from a Lectern
- **WHEN** a player at a Lectern clicks its Inbox nav button
- **THEN** the Lectern's dialog switches to the same Inbox tab shown by the Assignment Desk and
  the standalone Inbox block

### Requirement: The Lectern shows the ambient unseen-assignment particle
A placed Lectern SHALL emit the ambient particle effect defined by the `inbox-tab` capability
when the viewing player has a New (unseen) assignment and is within range.

#### Scenario: A Lectern particles for a player with an unseen assignment
- **WHEN** a player with an unseen assignment is within range of a placed Lectern
- **THEN** that Lectern emits the ambient particle effect for that player's client

