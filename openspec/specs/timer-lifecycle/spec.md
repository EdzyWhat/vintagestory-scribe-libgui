# timer-lifecycle Specification

## Purpose
TBD - created by archiving change timer-auto-disappear-setting. Update Purpose after archive.

## Requirements

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
itself clear a fired timer. Dismissing SHALL also trigger `ScribeModSystem.DismissAlarm()` so that
any playing alarm sound fades out gracefully.

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

#### Scenario: Dismiss triggers alarm fade-out
- **WHEN** a player dismisses a fired timer while the alarm sound is still playing
- **THEN** `DismissAlarm()` is called and the alarm begins its 500ms easeInOutSine fade-out

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

### Requirement: The Timer tab shows the cleared state after a timer is cleared
After a fired or running timer is cleared (by Stop Timer or from the HUD), the Clockmaker's Notebook Timer tab
SHALL return to the new-timer form, regardless of whether the Notebook was opened before or after the timer
started or fired.

#### Scenario: Stop Timer after reopening the Notebook on a fired timer
- **WHEN** a player starts a timer, closes the Notebook, waits for the timer to fire, reopens the Notebook, and presses Stop Timer
- **THEN** the Timer tab shows the new-timer form and no blinking 00:00 or Stop Timer button remains

#### Scenario: Stop Timer on a running timer in a Notebook opened mid-run
- **WHEN** a player opens the Notebook while a timer is running and presses Stop Timer
- **THEN** the Timer tab shows the new-timer form

### Requirement: A new timer appears on the HUD after a previous one ended
Starting a timer SHALL show it on the Pinned Task HUD even if an earlier timer was cancelled or ended and the HUD
was closed in between, without requiring any HUD setting to be changed.

#### Scenario: Start a timer after the previous one was cleared
- **WHEN** a player clears a timer, then starts a new one
- **THEN** the new timer appears on the HUD at the configured anchor position

### Requirement: The new-timer form prefills with the last timer's values
While no timer is active, the Clockmaker's Notebook Timer tab's new-timer form SHALL be prefilled with the name, hours, minutes, seconds and mode of the timer the player most recently started in the current game session, however that timer ended (fired and dismissed, auto-cleared, or stopped while running). Before the player has started any timer in the session, the form SHALL use its normal defaults. The prefill SHALL NOT persist across game restarts.

#### Scenario: Form refills after a fired timer is dismissed
- **WHEN** a player starts a timer named "Bread" for 0 h 12 m 30 s in real-time mode, it fires, and the player dismisses it
- **THEN** the Timer tab's form shows "Bread", 0 h, 12 m, 30 s and real-time mode

#### Scenario: Form refills after a running timer is stopped
- **WHEN** a player stops a running timer from the Timer tab
- **THEN** the form shows that timer's name, duration and mode

#### Scenario: Form refills after the timer auto-clears
- **WHEN** a fired timer is cleared automatically by the "Timer disappears" preference
- **THEN** the next time the Timer tab shows the form, it holds that timer's values

#### Scenario: No timer started yet this session
- **WHEN** a player opens the Timer tab before starting any timer since launching the game
- **THEN** the form shows its defaults, with the mode taken from the player's preferred timer mode

### Requirement: The new-timer form has a clear button
The new-timer form SHALL show a borderless, backgroundless circle-X clear button hanging off the right of the name field, with the name field staying centered whether or not it is shown, but only while the form has content (a non-empty name or any non-zero hours, minutes or seconds). With an empty name and a zero duration the button SHALL NOT be shown. Pressing it SHALL empty the name, set hours, minutes and seconds to zero, and return the mode to the player's preferred timer mode. It SHALL NOT start, stop or clear any timer, and SHALL NOT affect the values the next prefill uses until a new timer is started.

#### Scenario: Clearing the prefilled form
- **WHEN** the form is prefilled and the player presses the clear button
- **THEN** the name is empty, the duration reads 0 h 0 m 0 s, and the mode is the preferred mode

#### Scenario: Button hidden on an empty form
- **WHEN** the name is empty and hours, minutes and seconds are all zero
- **THEN** no clear button is shown

#### Scenario: Button appears as the player types
- **WHEN** the player types a name or raises a duration field on an empty form
- **THEN** the clear button appears without the form losing focus

#### Scenario: The HUD has no clear button
- **WHEN** a timer is fired and shown on the Pinned Task HUD
- **THEN** the row is still cleared by clicking it and shows no clear button
