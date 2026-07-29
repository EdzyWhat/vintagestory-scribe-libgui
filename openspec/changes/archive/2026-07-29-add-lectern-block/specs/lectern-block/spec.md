## ADDED Requirements

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
