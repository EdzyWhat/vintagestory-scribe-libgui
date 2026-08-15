## ADDED Requirements

### Requirement: Craftable, placeable Scriptorium block

The system SHALL provide a Scriptorium block that can be obtained (via a crafting recipe or
creative inventory), placed in the world, and broken to be recovered. The block SHALL be a
distinct block from the Lectern with its own block code (`scribe:scriptorium`), its own 3D
model and textures, and its own name in tooltips and the handbook. Its recipe SHALL be cheap
relative to the Lectern (ordinary planks + nails, no iron or metal tier gating).

#### Scenario: Place and break the Scriptorium
- **WHEN** a player places the Scriptorium block and later breaks it
- **THEN** the block appears in the world when placed and is returned to the player's inventory
  when broken

#### Scenario: Craft from the grid recipe
- **WHEN** a player arranges the Scriptorium's recipe ingredients in a crafting grid
- **THEN** a Scriptorium block item is produced

#### Scenario: Distinct from the Lectern
- **WHEN** a player looks at a placed Scriptorium
- **THEN** its name and model identify it as a Scriptorium, not a Lectern

### Requirement: Scriptorium hosts a Scribe document and opens the shared dialog

The Scriptorium SHALL host a `ScribeDocument` and, on right-click, open the existing LibGUI
Scribe dialog for that document, reusing the shared document-host contract
(`IScribeDocumentHost`) and dialog shell rather than introducing a new GUI surface. All
document behaviors already defined for a Scribe writing station — Read and Edit views, adding
and editing tasks, toggling completion by identity, deleting and reordering blocks, pinning,
the document title header, the shift+right-click quick-add gesture, and the guestbook — SHALL
be available on the Scriptorium exactly as they are on the Lectern, because they flow through
the same shared machinery. The Scriptorium's Assign & History and Inbox views are NOT part of
this capability and SHALL NOT appear.

#### Scenario: Open by right-click
- **WHEN** a player right-clicks a placed Scriptorium
- **THEN** the Scribe dialog opens showing that Scriptorium's document in the Read view

#### Scenario: Quick-add by shift+right-click
- **WHEN** a player shift+right-clicks a placed Scriptorium
- **THEN** the dialog opens in the Edit view with a fresh empty task inserted at the top and its
  caret focused (the unified quick-add gesture)

#### Scenario: Edit and complete a task
- **WHEN** the player adds a task and marks it complete through the Scriptorium's dialog
- **THEN** the Scriptorium's document contains that task shown as completed

#### Scenario: No assignment surfaces are exposed
- **WHEN** a player opens the Scriptorium's dialog
- **THEN** no Assign & History view and no Inbox nav-rail view are present (these are deferred to
  the v1.3 assignment system)

### Requirement: Scriptorium persistence and sync follow the vanilla Sign pattern

The Scriptorium's document SHALL be server-authoritative and persisted/synced through the
vanilla Sign flow (`ToTreeAttributes`/`FromTreeAttributes`, `SendBlockEntityPacket`,
`MarkDirty`), matching the Lectern. Edits made in the client dialog SHALL be sent to the
server, applied there, saved with the world, and survive save/reload. Only one player at a time
SHALL hold a given Scriptorium's editor lock, released on close, view-switch, or disconnect. The
block entity SHALL register in the `DocId → IScribeDocumentHost` registry on initialize and
unregister on removal.

#### Scenario: Edits persist across reload
- **WHEN** a player edits a Scriptorium's document, then the world is saved and reloaded
- **THEN** reopening that Scriptorium shows the same blocks in the same order

#### Scenario: Client edits are not trusted directly
- **WHEN** the client dialog changes a block
- **THEN** the change takes lasting effect only after the server applies it

#### Scenario: One editor at a time
- **WHEN** player A has a Scriptorium's editor open and player B tries to open the same block's
  editor
- **THEN** player B is refused with the "one person at a time" message, and the lock releases
  when player A closes the editor or disconnects

#### Scenario: Each Scriptorium is independent
- **WHEN** two Scriptoriums are placed and each is given different content
- **THEN** each shows only its own document

### Requirement: Floor placement facing the player

The Scriptorium SHALL be placeable only on a solid ground surface (floor-only, never wall or
ceiling), rejected with the vanilla `requiresolidground` failure message otherwise, and SHALL
orient to face the placing player, snapped to 22.5° steps — the same placement idiom as the
Lectern. The placement facing SHALL persist and drive both the rendered mesh and the
collision/selection box, and SHALL survive save/reload and world-edit rotation.

#### Scenario: Rejected on a non-floor surface
- **WHEN** a player tries to place the Scriptorium where the cell below is not a solid ground
  surface
- **THEN** placement is refused with the "requires solid ground" message

#### Scenario: Faces the placing player
- **WHEN** a player places the Scriptorium
- **THEN** the block's front faces the player, snapped to the nearest 22.5° step, and its hitbox
  tracks that facing

#### Scenario: Facing persists across reload
- **WHEN** a placed Scriptorium is saved and the world reloaded
- **THEN** the block renders and collides at the same facing it was placed with

### Requirement: A Scriptorium's document survives break and re-placement

Breaking a Scriptorium SHALL carry its document — including the document's and tasks' stable
identifiers — onto the dropped item (and onto the middle-click picked stack), and placing that
item SHALL restore the document. Because the document's identifier is preserved, per-player pins
referencing tasks in that document SHALL continue to resolve after re-placement. Content SHALL
be lost only when the dropped item itself disappears.

#### Scenario: Break then re-place preserves the document
- **WHEN** a Scriptorium holding a document is broken and the resulting item is placed again
- **THEN** the placed Scriptorium's document has the same content and the same `DocId` and
  `TaskId`s as before it was broken

#### Scenario: Pins resolve after relocation
- **WHEN** a player has pinned a task in a Scriptorium, and that Scriptorium is broken and
  re-placed
- **THEN** the player's pin still resolves to the same task in the re-placed Scriptorium

### Requirement: The Scriptorium's tooltip shows its document title

The Scriptorium SHALL display a title line on its tooltip — as the placed block's look-at
tooltip (from the live document) and the block item's held/inventory hover (from the document
carried on the stack) — formatted `Title: "<title>"`, falling back to `Title: "(untitled)"`
when there is no meaningful title, matching the Lectern's tooltip treatment. It SHALL NOT
advertise burn/combustion stats.

#### Scenario: Titled Scriptorium shows its quoted title on hover
- **WHEN** a player looks at a placed Scriptorium whose document title is "Field Notes"
- **THEN** the block tooltip includes a line reading `Title: "Field Notes"`

#### Scenario: Untitled Scriptorium shows the placeholder
- **WHEN** a player looks at a placed Scriptorium whose document has never been titled
- **THEN** the block tooltip includes a line reading `Title: "(untitled)"`

#### Scenario: No burn lines on the Scriptorium tooltip
- **WHEN** a player views the Scriptorium's tooltip (placed or in inventory)
- **THEN** no "Burn temperature" or "Burn duration" line is shown

### Requirement: Handbook entry and world-interaction hints

The Scriptorium SHALL have a handbook entry describing what it is and how it is used, and SHALL
show world-interaction hints on look-at for its right-click (open) and shift+right-click
(quick-add/edit) gestures, matching the hint pattern the Lectern uses.

#### Scenario: Interaction hints on look-at
- **WHEN** a player looks at a placed Scriptorium
- **THEN** the interaction help lists the right-click "open" and shift+right-click "edit"
  gestures

#### Scenario: Handbook entry exists
- **WHEN** a player opens the handbook entry for the Scriptorium
- **THEN** it describes the block as a Scribe writing station
