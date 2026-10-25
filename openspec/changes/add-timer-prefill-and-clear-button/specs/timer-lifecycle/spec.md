## ADDED Requirements

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
The new-timer form SHALL show a borderless, backgroundless circle-X clear button beside the name field, but only while the form has content (a non-empty name or any non-zero hours, minutes or seconds). With an empty name and a zero duration the button SHALL NOT be shown. Pressing it SHALL empty the name, set hours, minutes and seconds to zero, and return the mode to the player's preferred timer mode. It SHALL NOT start, stop or clear any timer, and SHALL NOT affect the values the next prefill uses until a new timer is started.

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
