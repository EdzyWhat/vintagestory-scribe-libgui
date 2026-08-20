## MODIFIED Requirements

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
