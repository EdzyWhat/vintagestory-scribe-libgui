## ADDED Requirements

### Requirement: A fired timer is shown until cleared
When a player's Clockmaker's Notebook timer counts down to zero it SHALL enter a fired state and
SHALL be shown in the fired state on the player's Pinned Task HUD and on the Clockmaker's Notebook
Timer tab (a blinking countdown row / attention state), and SHALL remain shown until it is cleared.

#### Scenario: Timer fires at zero
- **WHEN** a running timer's remaining time reaches zero
- **THEN** the timer enters the fired state and the HUD shows the fired timer row

### Requirement: A player can dismiss a fired timer
A player SHALL be able to clear a fired timer at any time, by either clicking the fired timer row on
the Pinned Task HUD or pressing Stop Timer on the Clockmaker's Notebook Timer tab. Clearing the timer
SHALL remove it from the HUD and the Timer tab. Merely opening the Clockmaker's Notebook SHALL NOT by
itself clear a fired timer.

#### Scenario: Clicking the HUD row clears the timer
- **WHEN** a player clicks the fired timer row on the Pinned Task HUD
- **THEN** the timer is cleared and no longer shown on the HUD or the Timer tab

#### Scenario: Stop Timer clears the timer
- **WHEN** a player presses Stop Timer on the Clockmaker's Notebook Timer tab while a timer is fired
- **THEN** the timer is cleared and no longer shown on the HUD or the Timer tab

#### Scenario: Opening the notebook does not clear the timer
- **WHEN** a player opens the Clockmaker's Notebook (any tab) while a timer is fired but takes no
  further action
- **THEN** the fired timer remains shown and is not cleared

### Requirement: The auto-disappear of a fired timer is governed by a player preference
A fired timer's automatic disappearance SHALL be governed by the player's client-local
"Timer disappears" preference (default on, see the settings-tab capability). When the preference is on,
a fired timer SHALL be cleared automatically after approximately 30 seconds of being in the fired
state, without any player action. When the preference is off, a fired timer SHALL NOT auto-clear and
SHALL remain shown until the player dismisses it. Because the preference is client-local, the
automatic clear SHALL be driven by the player's own client rather than by server-side timing.

#### Scenario: Auto-disappear on clears after the window
- **WHEN** a timer has been in the fired state for about 30 seconds and the player's "Timer disappears"
  preference is on
- **THEN** the timer is cleared automatically with no player action

#### Scenario: Auto-disappear off keeps the timer
- **WHEN** a timer has been in the fired state for well over 30 seconds and the player's "Timer
  disappears" preference is off
- **THEN** the timer is still shown and has not been auto-cleared

#### Scenario: Changing the preference affects the current fired timer live
- **WHEN** a player turns the "Timer disappears" preference off while a timer is already fired and
  counting toward auto-clear
- **THEN** the pending auto-clear is cancelled and the fired timer remains shown until dismissed

### Requirement: A fired timer survives logout until dismissed or auto-cleared
A fired-but-undismissed timer SHALL persist across a player logging out and rejoining, so that it is
still shown in the fired state on rejoin. The time already spent in the fired state SHALL be preserved
across the session boundary, so that (when the player's preference allows auto-disappear) the remaining
auto-clear window resumes rather than restarting from zero.

#### Scenario: Fired timer is restored on rejoin
- **WHEN** a player whose timer is fired but not yet dismissed logs out and later rejoins
- **THEN** the timer is still shown in the fired state on the Pinned Task HUD

#### Scenario: Auto-clear window resumes rather than restarts
- **WHEN** a player with "Timer disappears" on logs out with a timer that has been fired for 20 seconds,
  and rejoins
- **THEN** the fired timer clears after roughly the remaining window rather than a fresh 30 seconds
