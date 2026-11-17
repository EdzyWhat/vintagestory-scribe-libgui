## Purpose

Gives every document row in a Scribe dialog one right-click menu for its actions, alongside the existing hover controls, so new per-row actions have a consistent home.

## ADDED Requirements

### Requirement: Right-clicking a document row opens its action menu

The system SHALL open a floating action menu at the row when a player right-clicks a document row of any
block kind in a Scribe dialog's read view, editor view or Pin Tab. The menu SHALL open below the row at the
pointer's horizontal position. It SHALL flip above the row or shift left when it would otherwise extend past
the dialog's bottom or right edge. The existing hover controls on the row SHALL remain available.

#### Scenario: Right-click opens the menu
- **WHEN** a player right-clicks a task row in a Notebook's read view
- **THEN** a menu opens below that row, at the pointer, listing the actions allowed for it

#### Scenario: Right-click on a note opens the menu
- **WHEN** a player right-clicks a Text note row in an editor view
- **THEN** a menu opens with Pin or Unpin and Delete

#### Scenario: Menu near the dialog edge
- **WHEN** a player right-clicks the last visible row near the dialog's bottom edge
- **THEN** the menu opens above the row and stays inside the dialog

#### Scenario: Hover controls still work
- **WHEN** a player hovers a document row
- **THEN** the row's existing hover controls appear and work as before

### Requirement: Surfaces without a row menu

The HUD, the Task Notice, Inbox previews, and Guestbook and History rows SHALL NOT open a row menu.

#### Scenario: No menu on the HUD or Task Notice
- **WHEN** a player right-clicks a pinned row on the HUD, or a row in a Task Notice
- **THEN** no menu opens and nothing else happens

### Requirement: A right-click does only one thing

A right-click on a row, including on its checkbox, link or tracker name, collapse caret, hover buttons or
text, SHALL NOT perform that control's left-click action and SHALL NOT move the text caret or change the
text selection. It SHALL only open the row's menu, or do nothing on a surface without one.

#### Scenario: Right-click on a checkbox
- **WHEN** a player right-clicks an unchecked task's checkbox
- **THEN** the task stays unchecked and the menu opens

#### Scenario: Right-click on a link name
- **WHEN** a player right-clicks a Link row's name
- **THEN** the Handbook does not open and the menu opens

#### Scenario: Right-click on editor text
- **WHEN** a player right-clicks inside the text of an editor row with text selected in another row
- **THEN** the caret does not move, no selection drag starts, and the menu opens

#### Scenario: Right-click on an empty new row
- **WHEN** a player right-clicks an empty editor row they just added
- **THEN** the row is not purged and the menu opens

### Requirement: The menu offers Pin or Unpin and Delete when allowed

The menu SHALL offer **Pin** for an unpinned row or **Unpin** for a pinned one, wherever the view's pin
control is live, and **Delete** wherever the player may delete the row. Each entry SHALL act on the row's
current state when chosen, identified by its `TaskId`. Editor and Pin Tab entries SHALL use the same path
and server messages as the row's existing hover control. The menu MAY also offer further entries where
`add-notebook-task-dating` offers them. When no entry is allowed, the menu SHALL NOT open.

#### Scenario: Editor view entries
- **WHEN** a player right-clicks a task row in the editor view
- **THEN** the menu shows Pin or Unpin and Delete, plus the date entries where `add-notebook-task-dating`
  offers them, and no Edit entry

#### Scenario: Pin Tab entry acts by identity
- **WHEN** a player chooses Unpin from the menu on a Pin Tab row
- **THEN** the pin is removed through the same identity-addressed unpin the row's unpin control uses

#### Scenario: Read-only Tablet hides editing entries
- **WHEN** a player right-clicks a task on a hard or fired Tablet
- **THEN** the menu shows only Pin or Unpin

#### Scenario: Pin state changed while the menu is open
- **WHEN** a menu labelled Pin is open and the row is pinned from another surface before the player chooses
- **THEN** choosing the entry leaves the row pinned rather than unpinning it

### Requirement: Read-view Delete respects the editor lock

On an editable read view, the menu SHALL offer **Delete**, which deletes the row by its identity, applying
the player's Subtask Behavior. When another player holds the document's editor lock, the delete SHALL be
refused with the standard locked message and the row SHALL remain. The server SHALL enforce the refusal.
The Pin Tab's existing delete SHALL keep working regardless of the lock.

#### Scenario: Read-view Delete
- **WHEN** a player chooses Delete on a read-view task of a Lectern nobody is editing
- **THEN** the task is removed from the document for everyone

#### Scenario: Read-view Delete while locked
- **WHEN** a player chooses Delete on a read-view task while another player holds that Lectern's editor lock
- **THEN** the standard locked message appears and the task remains

### Requirement: Edit opens the editor on that row

On an editable read view, the menu SHALL offer **Edit** whether or not another player holds the editor lock.
Choosing it SHALL enter the editor view through the dialog's existing editor entry and focus the chosen
row, putting the caret in its text when it has a text field. If the editor lock is refused, the standard
locked feedback SHALL appear, the dialog SHALL stay in the read view, and no focus SHALL carry into a later
editor session.

#### Scenario: Edit enters the editor on that row
- **WHEN** a player chooses Edit on a read-view task row of a block document that nobody else is editing
- **THEN** the dialog enters the editor view with that task's row focused

#### Scenario: Edit while locked shows the existing feedback
- **WHEN** a player chooses Edit while another player holds the document's editor lock
- **THEN** the standard locked feedback appears and the dialog stays in the read view

#### Scenario: Later editor entry after a refusal
- **WHEN** Edit was refused for a row and the player later enters the editor through the nav button
- **THEN** the editor opens without focusing that row

### Requirement: The menu closes predictably

The menu SHALL close when the player chooses an action entry, clicks or right-clicks outside the menu,
presses Escape, switches the dialog's view, closes the dialog, scrolls the row out of view, or the row's
`TaskId` is no longer shown in the view. Escape while a menu is open SHALL close only the menu. A rebuild
that keeps the row on screen SHALL NOT close the menu. Only one row menu SHALL be open per dialog. A menu
whose entries all disappear while its row is still shown SHALL close. Choosing a toggle entry (an entry that
shows a checked or unchecked state) SHALL NOT close the menu; choosing an action entry SHALL.

#### Scenario: A menu with no entries left closes
- **WHEN** a menu is open and every entry it offered stops being allowed while its row is still shown
- **THEN** the menu closes

#### Scenario: A toggle entry keeps the menu open
- **WHEN** a menu is open and the player chooses a toggle entry
- **THEN** the entry shows its new state and the menu stays open

#### Scenario: Clicking outside closes the menu
- **WHEN** the menu is open and the player clicks elsewhere in the dialog
- **THEN** the menu closes and no action runs

#### Scenario: Escape closes only the menu
- **WHEN** the menu is open and the player presses Escape
- **THEN** the menu closes, the dialog stays open, and no action runs

#### Scenario: Right-clicking another row
- **WHEN** a menu is open and the player right-clicks a different row
- **THEN** the first menu closes and the other row's menu opens at the pointer

#### Scenario: The row disappears
- **WHEN** a menu is open and its row is removed (for example another player deletes the task)
- **THEN** the menu closes

#### Scenario: An unrelated update keeps the menu open
- **WHEN** a menu is open and another player checks off a different task in the same document
- **THEN** the menu stays open on its row

#### Scenario: Switching views closes the menu
- **WHEN** a menu is open in the read view and the player clicks the Pin Tab nav button
- **THEN** the menu closes
