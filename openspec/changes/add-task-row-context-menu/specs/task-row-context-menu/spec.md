## Purpose

Gives every task row in a Scribe dialog one right-click menu for its actions, alongside the existing hover controls, so new per-task actions have a consistent home.

## ADDED Requirements

### Requirement: Right-clicking a task row opens its action menu

The system SHALL open a floating action menu, anchored at the row, when a player right-clicks a task row
in a Scribe dialog's read view, editor view or Pin Tab. The menu SHALL stay inside the dialog's bounds. The
existing hover controls on the row SHALL remain available. Right-clicking a row that is not a task (a Text
note) SHALL NOT open a menu. The HUD SHALL NOT open a menu.

#### Scenario: Right-click opens the menu
- **WHEN** a player right-clicks a task row in a Notebook's read view
- **THEN** a menu opens next to that row listing the actions allowed for it

#### Scenario: Hover controls still work
- **WHEN** the menu feature is present and a player hovers a task row
- **THEN** the row's existing hover controls appear and work as before

#### Scenario: No menu on note rows or the HUD
- **WHEN** a player right-clicks a Text note row, or a pinned row on the HUD
- **THEN** no menu opens

### Requirement: The menu offers Edit, Pin or Unpin, and Delete when allowed

The menu SHALL offer these entries, each only when that action is allowed for the row in its current view:
**Edit** in the read view only, when the player may edit the document; **Pin** for an unpinned task or
**Unpin** for a pinned one, where pinning is live; and **Delete** where the player may delete the task. An
entry SHALL perform the same action, with the same permission checks and server messages, as the row's
existing control for it. When no entry is allowed, the menu SHALL NOT open.

#### Scenario: Read view shows Edit, Pin and Delete
- **WHEN** a player who may edit a Notebook right-clicks an unpinned task in its read view
- **THEN** the menu shows Edit, Pin and Delete

#### Scenario: Read-only document hides editing entries
- **WHEN** a player right-clicks a task on a Lectern whose access mode does not let them edit
- **THEN** the menu omits Edit and Delete, and shows Pin or Unpin only if pinning is live there

#### Scenario: Editor view omits Edit
- **WHEN** a player right-clicks a task row in the editor view
- **THEN** the menu shows Pin or Unpin and Delete, and no Edit entry

#### Scenario: Pin Tab entry acts by identity
- **WHEN** a player chooses Unpin from the menu on a Pin Tab row
- **THEN** the pin is removed through the same identity-addressed unpin the row's unpin control uses

#### Scenario: Edit enters the editor on that row
- **WHEN** a player chooses Edit on a read-view task row of a block document that nobody else is editing
- **THEN** the dialog enters the editor view with that task's row focused

#### Scenario: Edit while locked shows the existing feedback
- **WHEN** a player chooses Edit while another player holds the document's editor lock
- **THEN** the standard locked-editor feedback appears and the dialog stays in the read view

### Requirement: The menu closes predictably

The menu SHALL close when the player chooses an entry, clicks outside the menu, presses Escape, or the row
it belongs to is removed or rebuilt. Only one row menu SHALL be open at a time.

#### Scenario: Clicking outside closes the menu
- **WHEN** the menu is open and the player clicks elsewhere in the dialog
- **THEN** the menu closes and no action runs

#### Scenario: Opening a second menu closes the first
- **WHEN** a menu is open and the player right-clicks a different task row
- **THEN** the first menu closes and the new row's menu opens

#### Scenario: The row disappears
- **WHEN** a menu is open and its row is removed (for example another player deletes the task)
- **THEN** the menu closes
