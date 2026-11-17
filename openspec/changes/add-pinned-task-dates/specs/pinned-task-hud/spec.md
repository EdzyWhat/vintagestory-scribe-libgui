## ADDED Requirements

### Requirement: HUD rows show the pinned task's date
A HUD row for a plain task that has a date SHALL show a date chip beneath the task text, built from the
pin's snapshot so it appears with the source document unloaded: for an unchecked task the status and the
date, and for a checked task the date alone, dimmed with the rest of a resting checked row. The date SHALL be
formatted in the player's Date order with years counted from 1. The chip SHALL use the HUD's own palette of
four colors (due today, overdue, upcoming and done) that are four different values and different from the
HUD's text color; the exact values are tuned in playtest. The chip is the Notebook chip's size scaled by the
HUD's existing font scale, wraps within the HUD's configured row width, and is part of the row, so a row with
a chip is taller than one without and a row without one is exactly as tall as before dates; the HUD's
row-count limit, anchoring and sizing apply unchanged. While a completion is pending in its undo window, a
dated row that was just checked shows the checked form (the date alone, dimmed) and returns to its status
form if the completion is undone. The chip fades with the row's text while a removal is pending, and the
row's collapsing snapshot keeps the chip so the collapse starts at the row's real height. Rows of other
kinds, assignment tasks, undated tasks and orphaned pins show no chip. The temporal-instability corruption
applies to the task text only, not to the chip.

#### Scenario: A pinned dated task shows its date on the HUD
- **WHEN** a player pins a task dated for tomorrow
- **THEN** its HUD row shows the status ("in 1 day") and the date beneath the text

#### Scenario: A checked task shows only the date
- **WHEN** a pinned dated task is checked off and rests at the bottom of the HUD
- **THEN** its chip shows the date alone and the row is dimmed

#### Scenario: A pending completion shows the checked form
- **WHEN** a player checks a pinned dated task and the undo window has not elapsed
- **THEN** its chip shows the date alone, dimmed, and returns to the status and date if the player unchecks it

#### Scenario: The chip wraps within the row width
- **WHEN** a pinned task's chip is longer than the configured row width allows on one line
- **THEN** the chip wraps beneath the text within the row width and the row grows to fit

#### Scenario: A row with a chip collapses from its true height
- **WHEN** a dated pinned task's row leaves the HUD after a removing completion
- **THEN** the row collapses from its full height including the chip

#### Scenario: Undated, other-kind and orphaned pins show no chip
- **WHEN** the HUD lists an undated task, a tracker, a note, an assignment task and an orphaned pin of a
  dated task
- **THEN** none of those rows shows a date chip, and the undated rows are the height they were before dates

### Requirement: HUD chips follow the world's day and the Date order
The HUD SHALL rebuild its chips when the in-game day changes while at least one displayed pin has a date, so
a HUD left open does not keep a stale status. The day is the whole in-game day of the world calendar, the
same day the Notebook chip counts from; a day that moves backward (for example by a time command) rebuilds
the same way. A closed HUD does not rebuild, and a HUD with no dated pins does not rebuild on a day change.
The chips SHALL also show the new order when the player changes the Date order setting, without any other
action.

#### Scenario: The status follows the day
- **WHEN** the HUD stays open while the in-game day passes the date of an unchecked pinned task
- **THEN** its chip changes from "Due today" to the overdue status without any other action

#### Scenario: A backward day change
- **WHEN** the in-game day moves back to before a pinned task's date
- **THEN** the chip shows the upcoming status again

#### Scenario: A day change with no dated pins
- **WHEN** the in-game day changes and no displayed pin is dated
- **THEN** the HUD does not rebuild

#### Scenario: A hidden HUD
- **WHEN** the in-game day changes while the HUD is not open
- **THEN** nothing is rebuilt, and the chips are current when the HUD next opens

#### Scenario: The Date order setting
- **WHEN** the player changes the Date order setting while the HUD is showing a dated pin
- **THEN** the HUD's chip shows the date in the new order
