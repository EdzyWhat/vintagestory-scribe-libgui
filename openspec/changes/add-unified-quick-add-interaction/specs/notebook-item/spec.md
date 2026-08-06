## MODIFIED Requirements

### Requirement: Notebook is a carriable item with a full Scribe GUI
The system SHALL provide an item (`ItemScribeNotebook`) that the player can hold in their
inventory and interact with (right-click / use key) to open the full Scribe dialog (Read,
Editor, Pinned, **History**, Settings tabs). The Notebook GUI SHALL NOT include a Guestbook
tab. The dialog SHALL use the same visual backdrop and layout proportions as the Lectern.

The Notebook's held-interaction modifier map SHALL be:

- **Right-click** (no modifier): open the Scribe dialog in Read view.
- **Shift+Right-Click**: perform quick-add (see `quick-add-interaction`) — open the editor
  with a new empty task at the top and the caret focused.
- **Ctrl+Shift+Right-Click**: pass through to the base collectible behaviors (including
  GroundStorable) for ground placement, following the vanilla spear placement convention.

Ground placement SHALL NO LONGER trigger on plain Shift+Right-Click; it SHALL require the
Ctrl+Shift+Right-Click modifier combination.

#### Scenario: Player opens notebook from hotbar
- **WHEN** a player right-clicks (or uses the interaction key) while holding a Notebook item
- **THEN** the Scribe dialog opens in Read view, showing the document stored in that
  specific Notebook item

#### Scenario: No Guestbook tab
- **WHEN** the Scribe dialog opens for a Notebook
- **THEN** no Guestbook tab or nav button is present in the navigation column

#### Scenario: History tab is present
- **WHEN** the Scribe dialog opens for a Notebook
- **THEN** a History nav button is present in the navigation column and clicking it shows
  the notebook's history entries

#### Scenario: Shift+right-click quick-adds
- **WHEN** a player Shift+Right-Clicks while holding a Notebook
- **THEN** the Notebook's editor opens with a new empty task at the top of the document and
  the caret focused on it, and ground placement does not occur

#### Scenario: Ctrl+Shift+right-click stores on the ground
- **WHEN** a player Ctrl+Shift+Right-Clicks while holding a Notebook
- **THEN** the base ground-storage behavior runs and the dialog does not open
