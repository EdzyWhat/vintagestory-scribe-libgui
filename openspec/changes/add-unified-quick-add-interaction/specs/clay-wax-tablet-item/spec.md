## MODIFIED Requirements

### Requirement: Right-click opens the Scribe document dialog

Right-clicking (or using the interaction key) while holding a tablet SHALL open the Scribe document
editing dialog for the document stored in that specific tablet stack. The dialog opened SHALL be the
bespoke `GuiDialogScribeTablet` (see `tablet-dialog`), constructed with a `TabletHost` for that stack.

The tablet's held-interaction modifier map SHALL be:

- **Right-click** (no modifier): open the tablet dialog.
- **Shift+Right-Click aimed at a water block**: quench/soften a hard tablet (unchanged; the water-aim
  branch is the discriminator).
- **Shift+Right-Click NOT aimed at water**: perform quick-add (see `quick-add-interaction`) — open the
  dialog with a new empty task at the top and the caret focused.
- **Ctrl+Shift+Right-Click**: pass through to the base collectible behaviors (including GroundStorable)
  for ground placement, following the vanilla spear placement convention.

Ground placement SHALL NO LONGER trigger on plain Shift+Right-Click; it SHALL require the
Ctrl+Shift+Right-Click modifier combination.

#### Scenario: Right-click opens the document

- **WHEN** a player right-clicks while holding a tablet
- **THEN** the Scribe document editing dialog opens showing that tablet's document

#### Scenario: The bespoke tablet dialog is opened

- **WHEN** a tablet is opened
- **THEN** the dialog shown is `GuiDialogScribeTablet` (the always-edit, no-tabs tablet dialog), not
  the interim `GuiDialogScribeNotebook` used before Proposal C

#### Scenario: Shift+right-click on water quenches

- **WHEN** a player Shift+Right-Clicks while holding a hard tablet aimed at a water block
- **THEN** the tablet softens/quenches and the dialog does not open (unchanged behavior)

#### Scenario: Shift+right-click off water quick-adds

- **WHEN** a player Shift+Right-Clicks while holding a tablet and is NOT aiming at a water block
- **THEN** the tablet dialog opens with a new empty task at the top and the caret focused, and ground
  placement does not occur

#### Scenario: Ctrl+Shift+right-click stores on the ground

- **WHEN** a player Ctrl+Shift+Right-Clicks while holding a tablet
- **THEN** the base ground-storage behavior runs and the dialog does not open
