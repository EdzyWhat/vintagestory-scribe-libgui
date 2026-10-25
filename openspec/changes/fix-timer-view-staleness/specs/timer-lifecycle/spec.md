## ADDED Requirements

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
