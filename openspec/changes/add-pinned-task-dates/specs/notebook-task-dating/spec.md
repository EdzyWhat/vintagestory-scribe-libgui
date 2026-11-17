## MODIFIED Requirements

### Requirement: Date chip content and status

A dated datable task SHALL show a chip under its text in the read and editor views containing the date, in
the player's Date order with years counted from 1, and its status: "Due today" on the day, "in 1 day" or "in
N days" before it, "Overdue (1 day)" or "Overdue (N days)" after it while unchecked, counted from the
task's date clamped to its month's end. A checked task SHALL show only the date. Undated tasks, other
block kinds, assigned tasks, and rows in the Task Notice, a Tablet or the Assignment Desk SHALL show no
chip. A pinned dated plain task SHALL also show the same chip on the Pin Tab (in a dialog that offers dates)
and on the HUD, built from the pin's snapshot; the pin-surface requirements live in `pinned-task-tab` and
`pinned-task-hud`. A depth-1 subtask's chip SHALL sit under its own text.

#### Scenario: Upcoming task
- **WHEN** a task is dated 3 days from today and unchecked
- **THEN** its chip shows "in 3 days" and the date

#### Scenario: One day away
- **WHEN** an unchecked task is dated tomorrow
- **THEN** its chip shows "in 1 day"

#### Scenario: Due today
- **WHEN** an unchecked task is dated today
- **THEN** its chip shows "Due today" and the date

#### Scenario: Overdue
- **WHEN** an unchecked task was dated 2 days ago
- **THEN** its chip shows "Overdue (2 days)" and the date

#### Scenario: One day overdue
- **WHEN** an unchecked task was dated yesterday
- **THEN** its chip shows "Overdue (1 day)"

#### Scenario: A stored day past the month end
- **WHEN** a task is stored as day 25 of a month with 9 days and today is that month's day 9
- **THEN** its chip shows "Due today"

#### Scenario: Completed task
- **WHEN** a dated task is checked off
- **THEN** its chip shows only the date

#### Scenario: No chip where dates are not offered
- **WHEN** a dated task appears on a Tablet or in a Task Notice
- **THEN** it shows no chip

#### Scenario: A pinned dated task shows its chip on the pin surfaces
- **WHEN** a dated plain task is pinned
- **THEN** its Pin Tab row (outside a Tablet or the Assignment Desk) and its HUD row show the same status
  and date the Notebook row shows

### Requirement: Date chip follows the Date order and the world's day

The chip SHALL show its date in the player's current Date order, refreshing when the setting changes without
reopening the dialog. The chip SHALL be computed when its row is built, so a dialog left open while the
world's day changes SHALL NOT change a chip until the dialog next rebuilds. The HUD is the exception: it
also rebuilds its chips when the in-game day changes.

#### Scenario: Date order setting
- **WHEN** the player changes the Date order setting
- **THEN** the chips show the dates in the new order without reopening the dialog

#### Scenario: Left open across midnight
- **WHEN** a dialog stays open while the world's day changes and has not rebuilt
- **THEN** the chip does not change until the dialog rebuilds

#### Scenario: A HUD left open across midnight
- **WHEN** the HUD stays open while the world's day changes
- **THEN** its chips update to the new day without any other action
