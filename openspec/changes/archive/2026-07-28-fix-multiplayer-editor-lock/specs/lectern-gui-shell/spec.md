## MODIFIED Requirements

### Requirement: Editor view is rendered by the LibGUI dialog
The lectern's editor view SHALL be rendered by the same LibGUI dialog that renders the read view
(`GuiDialogScribeLecternLibGui`), NOT by the native `GuiComposer`-based `GuiDialogScribeLectern`.
Switching between the read view and the editor view SHALL be an internal view swap within that single
dialog — no separate native dialog SHALL be opened for editing. Returning from the editor view (by
finishing editing) SHALL return to the LibGUI read view, and entering the editor view SHALL acquire the
lectern's single-editor lock through the existing server flow, releasing it when the editor view is left.

The dialog SHALL enter the editor view ONLY after the server has actually granted the single-editor
lock. It SHALL NOT enter the editor view optimistically (before the grant reply) nor on a refused
reply. This closes the defect where a second player could open the editor and type while the server
still held the lock for another player, and where an editing player's own edits were rejected because
the client entered the editor before the lock was confirmed granted.

#### Scenario: Switching to editor stays in the LibGUI dialog
- **WHEN** the player activates "switch to editor" from the LibGUI read view
- **THEN** the same dialog swaps to an editor view rendered on LibGUI, and no native editor dialog opens

#### Scenario: Finishing editing returns to the LibGUI read view
- **WHEN** the player finishes editing and leaves the editor view
- **THEN** the dialog returns to the LibGUI read view (not a native read view), and the editor lock is
  released

#### Scenario: Entering the editor acquires the editor lock
- **WHEN** the editor view is entered
- **THEN** the single-editor lock is acquired through the existing server flow, and is released when the
  editor view is left

#### Scenario: Editor view is entered only after the lock is granted
- **WHEN** the player activates "switch to editor" and the server grants the lock
- **THEN** the dialog swaps to the editor view only upon receiving the granted reply, and the editing
  player's subsequent autosave edits are accepted (the server's lock check passes because the client
  holds the lock it entered on)

## ADDED Requirements

### Requirement: A contended editor lock blocks entry and reports it to the player
When another player already holds the lectern's single-editor lock, the dialog SHALL NOT allow a second
player to enter the editor view. The second player's "switch to editor" affordance SHALL reflect the
unavailable lock (visibly disabled/inert while the lock is held by another player), and activating it
SHALL NOT open the editor view — the second player remains in the read view. When editor access is
refused (or otherwise unavailable), the dialog SHALL surface Vintage Story's native in-game error
notification with player-facing copy indicating another player is editing (e.g. "Another player is
making edits."). A refused request SHALL NOT leave the second player in an editor view whose edits are
silently discarded.

#### Scenario: Second player is blocked while the first edits
- **WHEN** player 1 holds the lectern's editor lock and player 2 activates "switch to editor" on the same lectern
- **THEN** player 2 does not enter the editor view, remains in the read view, and sees a native in-game
  error indicating another player is editing

#### Scenario: Contended editor affordance reflects the held lock
- **WHEN** player 2 views a lectern whose editor lock is held by player 1
- **THEN** player 2's "switch to editor" affordance is shown as unavailable/inert rather than appearing
  freely usable

#### Scenario: Lock releases so the next player can edit
- **WHEN** player 1 leaves the editor view (or disconnects) and releases the lock, and player 2 then
  activates "switch to editor"
- **THEN** player 2 is granted the lock and enters the editor view normally

#### Scenario: A sole editor is never spuriously refused
- **WHEN** no other player holds the lock and a player activates "switch to editor"
- **THEN** the player enters the editor view and their edits persist (no revert), because the lock is
  granted and retained for the duration of the editor session
