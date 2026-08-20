# chalkboard-block Specification

## Purpose
TBD - created by archiving change add-chalkboard-block. Update Purpose after archive.
## Requirements
### Requirement: Chalkboard is a placed Scribe document block

The chalkboard SHALL be a placeable block that hosts a shared Scribe document behaviorally
identical to the Lectern: it SHALL reuse the shared writing-station block, block-entity, and
dialog base classes so that placement, interaction, the server-authoritative editor lock,
autosave, the guestbook, title editing, and every task kind (Task, Text, Tracker, Link,
Craft, subtasks) behave exactly as on the Lectern. The chalkboard SHALL NOT introduce any
new task kind, drawing/stroke input, persistence format, or synchronization mechanism.

#### Scenario: Placing and opening the chalkboard

- **WHEN** a player places a chalkboard block and interacts with it
- **THEN** the block registers and renders its own model, and opening it presents the same
  Scribe document dialog (views, tabs, task kinds, guestbook) the Lectern presents

#### Scenario: Editing is server-authoritative like the Lectern

- **WHEN** a player edits a chalkboard document
- **THEN** editor access requires the same server lock round-trip as the Lectern, and the
  document persists and syncs via the inherited writing-station (Sign-block-pattern) path
  with no chalkboard-specific persistence code

### Requirement: Chalkboard differs from the Lectern only cosmetically and in placement

The chalkboard SHALL differ from the Lectern in exactly four cosmetic dimensions — (1) its 3D
block model, (2) its block textures, (3) its LibGUI dialog theme, (4) its GUI background
illustration — plus one behavioral dimension: (5) it SHALL mount on a wall like a vanilla
painting rather than stand on the floor. Its document, data, lock, and interaction behavior
SHALL NOT otherwise differ from the Lectern.

#### Scenario: Distinct model and textures

- **WHEN** the chalkboard is rendered in the world
- **THEN** it uses its own committed model (`block/chalkboard/chalkboard`) and block
  textures (chalk / slate / wood), and every shape texture key resolves through the
  blocktype `textures` dict (no untextured faces)

#### Scenario: Distinct dialog theme and background

- **WHEN** the chalkboard dialog is open
- **THEN** it paints its own GUI background (`textures/gui/scribe-chalkboard.png`) and
  applies its own LibGUI theme, while the player's global Light/Default theme preference
  for every other Scribe surface is unchanged

#### Scenario: Wall-mounted placement

- **WHEN** a player places a chalkboard against a vertical wall face
- **THEN** it attaches to the wall, orients to face outward from that face (north/east/
  south/west variant), and requires no floor beneath it; it breaks if its supporting wall is
  removed. The Lectern and Scriptorium retain their floor-only, face-the-player placement.

### Requirement: Chalkboard is obtainable and documented

The chalkboard SHALL be obtainable in survival (a crafting recipe) and creative, and SHALL
carry its own interaction-hint text, default document title, and handbook copy rather than
borrowing the Lectern's or Scriptorium's strings.

#### Scenario: Obtaining and inspecting the chalkboard

- **WHEN** a player crafts or spawns a chalkboard and opens its handbook entry
- **THEN** the item is obtainable, its placement/interaction hints and default document
  title read as chalkboard-specific, and the handbook entry describes the chalkboard (not
  the Scriptorium)

