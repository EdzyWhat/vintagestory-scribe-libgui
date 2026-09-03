## MODIFIED Requirements

### Requirement: The Chalkboard exposes an Inbox nav button

The Chalkboard's dialog SHALL show a nav button that switches its view to the shared Inbox tab
(`inbox-tab` capability), matching the Lectern's equivalent addition. This is a functional
parity addition, not a further cosmetic/behavioral difference from the Lectern beyond the five
already specified (model, textures, dialog theme, GUI background, wall-mount placement). Per
`inbox-tab`'s assignment-history gating requirement, this nav button SHALL be shown only once the
viewing player has received at least one assignment, ever; before that, it SHALL NOT appear.

#### Scenario: Opening the Inbox from a Chalkboard
- **WHEN** a player at a Chalkboard clicks its Inbox nav button
- **THEN** the Chalkboard's dialog switches to the same Inbox tab shown by the Assignment Desk
  and the standalone Inbox block

#### Scenario: No Inbox button before any assignment history
- **WHEN** a player who has never received an assignment opens a Chalkboard's dialog
- **THEN** no Inbox nav button is present
