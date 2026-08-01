## MODIFIED Requirements

### Requirement: A contended editor lock blocks entry and reports it to the player
The lectern's single-editor lock SHALL be **transient server-session state only**. The server's
authoritative lock holder SHALL be released whenever the holder leaves the editing session by ANY
path — closing the dialog, switching to the read view or another tab, or disconnecting — and SHALL
be cleared when the block entity is loaded. Consequently the lock SHALL NEVER survive the holder
leaving the editor, a second player's relog, or a server restart: it can prevent a *concurrent*
second editor, but it can never become a permanent lockout that bars a lectern from ever being
edited again. The lock MAY be mirrored to clients via the block-entity sync (to drive the
contended-editor affordance), but that synced value SHALL NOT be treated as authoritative across a
block load.

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

#### Scenario: Closing the dialog releases the lock even when not in editor mode
- **WHEN** player 1 acquired the editor lock, then switched to the read view (or another tab) without
  fully closing, and then closes the dialog
- **THEN** the server's lock holder is released on that close, so player 2 can subsequently enter the
  editor

#### Scenario: The lock does not lock out after the holder leaves or a second player relogs
- **WHEN** player 1 leaves the editing session (closes the dialog, switches to read, or disconnects),
  or a lectern is loaded whose in-memory holder was somehow still set
- **THEN** the loaded/updated lectern has no editor lock held, and any player — including one who
  relogs — may enter the editor view
