## MODIFIED Requirements

### Requirement: Right-click opens the Scribe document dialog

Right-clicking (or using the interaction key) while holding a tablet SHALL open the Scribe document
editing dialog for the document stored in that specific tablet stack. A shift + right-click SHALL
pass through to the base collectible behaviors (including GroundStorable) rather than opening the
dialog. The dialog opened SHALL be the bespoke `GuiDialogScribeTablet` (see `tablet-dialog`),
constructed with a `TabletHost` for that stack.

#### Scenario: Right-click opens the document

- **WHEN** a player right-clicks while holding a tablet
- **THEN** the Scribe document editing dialog opens showing that tablet's document

#### Scenario: The bespoke tablet dialog is opened

- **WHEN** a tablet is opened
- **THEN** the dialog shown is `GuiDialogScribeTablet` (the always-edit, no-tabs tablet dialog), not
  the interim `GuiDialogScribeNotebook` used before Proposal C

#### Scenario: Shift+right-click stores on the ground

- **WHEN** a player shift + right-clicks while holding a tablet
- **THEN** the base ground-storage behavior runs and the dialog does not open
