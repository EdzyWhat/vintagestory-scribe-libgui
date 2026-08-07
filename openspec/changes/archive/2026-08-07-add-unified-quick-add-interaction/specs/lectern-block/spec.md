## MODIFIED Requirements

### Requirement: Open the lectern's editor

The system SHALL let a player open the lectern's editing GUI. A plain **right-click** SHALL open the
lectern in Read view (from which the editor is reachable via the Editor nav tab). A
**Shift+Right-Click** SHALL perform quick-add (see `quick-add-interaction`): it opens the editor,
inserts a new empty task at the top of the document, and focuses the caret on it. The Shift+Right-Click
gesture SHALL NO LONGER open the plain editor view without adding a task; opening the editor without a
new task is reached through the Editor nav tab after a plain right-click.

#### Scenario: Open by right-click

- **WHEN** a player right-clicks a placed lectern
- **THEN** the Scribe editing GUI opens showing that lectern's document (its tasks and text sections)

#### Scenario: Shift+right-click quick-adds

- **WHEN** a player Shift+Right-Clicks a placed lectern
- **THEN** the lectern's editor opens with a new empty task inserted at the top of the document and the
  caret focused on it, ready for typing
