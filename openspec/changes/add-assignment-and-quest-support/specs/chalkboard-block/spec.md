## ADDED Requirements

### Requirement: The Chalkboard exposes an Inbox nav button
The Chalkboard's dialog SHALL show a nav button that switches its view to the shared Inbox tab
(`inbox-tab` capability), matching the Lectern's equivalent addition. This is a functional
parity addition, not a further cosmetic/behavioral difference from the Lectern beyond the five
already specified (model, textures, dialog theme, GUI background, wall-mount placement).

#### Scenario: Opening the Inbox from a Chalkboard
- **WHEN** a player at a Chalkboard clicks its Inbox nav button
- **THEN** the Chalkboard's dialog switches to the same Inbox tab shown by the Assignment Desk
  and the standalone Inbox block

### Requirement: The Chalkboard shows the ambient unseen-assignment particle
A placed Chalkboard SHALL emit the ambient particle effect defined by the `inbox-tab` capability
when the viewing player has a New (unseen) assignment and is within range.

#### Scenario: A Chalkboard particles for a player with an unseen assignment
- **WHEN** a player with an unseen assignment is within range of a placed Chalkboard
- **THEN** that Chalkboard emits the ambient particle effect for that player's client
