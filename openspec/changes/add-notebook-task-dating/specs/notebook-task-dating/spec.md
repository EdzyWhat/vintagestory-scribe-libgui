## Purpose

Lets players date plain tasks in notebook-style documents from the row menu, shows each dated task's date
and status on its row, and states that a repeating task completes like any other. The English strings
quoted below are examples; every menu, picker and chip string is localized.

## ADDED Requirements

### Requirement: Date entries in the row menu

The row menu SHALL offer **Add date** on an undated datable task and **Change date** and **Clear date** on a
dated datable task, in the read view and the editor view of a Notebook, Clockmaker's Notebook, Lectern,
Chalkboard or Scriptorium document. A task is datable when it is a plain Task (not a note, tracker, link,
craft or quest objective) that was not placed by an assignment. Date entries SHALL form their own group
between the pin entries and Delete. They SHALL NOT be offered when the task is not datable, in a read view
whose document is read-only (a hard or fired Tablet), on a wet Tablet, in the Assignment Desk or in the Pin
Tab. A depth-1 subtask is datable.

#### Scenario: Undated task
- **WHEN** a player right-clicks an undated task in a Notebook's read view
- **THEN** the menu lists Add date, and no Change date, Clear date or Repeat yearly

#### Scenario: Dated task
- **WHEN** a player right-clicks a dated task in an editor view
- **THEN** the menu lists Change date, Clear date and Repeat yearly, with Repeat yearly showing its state

#### Scenario: Other kinds and surfaces
- **WHEN** a player right-clicks a note, a tracker, or any row on a Tablet or in the Pin Tab
- **THEN** the menu has no date entries

#### Scenario: Scriptorium and Chalkboard documents
- **WHEN** a player right-clicks a task in the read view of a Scriptorium's document or a Chalkboard
- **THEN** the menu lists the date entries like a Notebook's

#### Scenario: A task placed by an assignment
- **WHEN** a player right-clicks an accepted assigned task in a Notebook
- **THEN** the menu has no date entries

#### Scenario: A subtask
- **WHEN** a player right-clicks a depth-1 subtask in the read view
- **THEN** the menu lists the date entries

### Requirement: The Repeat yearly entry

On a dated datable task the date group SHALL include **Repeat yearly**, a checkbox entry that shows whether
the task repeats and, when chosen, toggles it and leaves the menu open. The menu SHALL show the document's
current state of the flag and of the date entries at all times, including after another player changes
them, while Pin and Unpin keep the label the menu opened with. **Repeat yearly** SHALL NOT be offered on an
undated task.

#### Scenario: Repeat yearly stays open
- **WHEN** a player chooses Repeat yearly on a dated task
- **THEN** the task's repeat state flips, the entry shows the new state, and the menu stays open

#### Scenario: Two quick toggles
- **WHEN** a player chooses Repeat yearly twice in a row in the read view before the server replies
- **THEN** the task ends up as it started, not set twice

#### Scenario: Another player changes the date while the menu is open
- **WHEN** a menu is open on an undated task and another player dates it
- **THEN** the open menu shows Change date, Clear date and Repeat yearly instead of Add date

#### Scenario: The task loses its date while the menu is open
- **WHEN** a menu is open with Repeat yearly and another player clears the task's date
- **THEN** the open menu shows Add date and no Repeat yearly

#### Scenario: A refused toggle shows the true state
- **WHEN** a player chooses Repeat yearly in the read view while another player holds the editor lock
- **THEN** the standard locked message appears and the menu, still open, shows the state it had before

### Requirement: Date picker

Choosing Add date or Change date SHALL open a date picker at the row with a month stepper, a day stepper
and a year stepper. It SHALL start at the task's date, or today when the task has none. The day stepper
SHALL stop at day 1 and at the month's length, and SHALL NOT carry into the month. The month stepper SHALL
carry into the neighboring year and SHALL be blocked at Year 1 month 1 and at Year 9999 month 12. The year
stepper SHALL be limited to Year 1 through Year 9999 with no wrap, and a task dated after Year 9999 SHALL
start the picker at Year 9999. A task whose stored day is past its month's end SHALL start the picker on the
clamped day, and Set SHALL store the clamped day. Set SHALL apply the date and close the picker.

#### Scenario: Picker starts at today
- **WHEN** a player chooses Add date
- **THEN** the picker shows today's month, day and year

#### Scenario: Picker starts at the task's date
- **WHEN** a player chooses Change date on a task dated 5 Mist, Year 3
- **THEN** the picker shows that date

#### Scenario: A stored day past the month end
- **WHEN** a player chooses Change date on a task stored as day 25 in a world with 9-day months
- **THEN** the picker shows day 9, and Set stores day 9

#### Scenario: The day stepper stops at the month length
- **WHEN** the picker is on day 9 of a 9-day month and the player steps the day up, or on day 1 and steps it
  down
- **THEN** the day does not change and the month does not change

#### Scenario: Month stepper carries into the year
- **WHEN** the picker is on month 12 of Year 3 and the player steps to the next month
- **THEN** it shows month 1 of Year 4

#### Scenario: Month stepper bounds
- **WHEN** the picker is on month 1 of Year 1 and the player steps to the previous month, or on month 12 of
  Year 9999 and steps to the next
- **THEN** the month and year do not change

#### Scenario: Year bounds
- **WHEN** the picker is on Year 1 and the player steps the year down, or on Year 9999 and steps it up
- **THEN** the year does not change

#### Scenario: Set changes the date
- **WHEN** a player sets the picker to a new date and presses Set
- **THEN** the picker closes and the task has that date, keeping its repeat state

### Requirement: The date picker closes predictably

The picker SHALL close without changing the task when the player presses Cancel or Escape, left-clicks
outside it (the click does nothing else), switches views, closes the dialog, the editor closes, the row goes
away or scrolls out of view, or another player takes the document's editor lock while a read-view picker is
open. A right-click on another row SHALL close the picker and open that row's menu; a right-click on the same
row SHALL close it and open that row's menu again; a right-click on empty space SHALL only close it. The
picker does not handle mouse-wheel scrolling itself. Escape SHALL close the open picker or the open menu,
whichever is open.

#### Scenario: Today returns the picker to today
- **WHEN** a player has stepped the picker to another date and presses Today
- **THEN** the month, day and year show today's date and the task is unchanged until Set

#### Scenario: Cancel changes nothing
- **WHEN** a player changes the picker's fields and presses Cancel
- **THEN** the task's date is unchanged

#### Scenario: Escape closes the picker
- **WHEN** a picker is open and the player presses Escape
- **THEN** the picker closes, the dialog stays open, and the task is unchanged

#### Scenario: Click outside
- **WHEN** the picker is open and the player left-clicks a checkbox outside it
- **THEN** the picker closes and the checkbox is not toggled

#### Scenario: Right-click on another row
- **WHEN** the picker is open and the player right-clicks a different row
- **THEN** the picker closes and that row's menu opens

#### Scenario: Right-click on the same row
- **WHEN** the picker is open and the player right-clicks its own row
- **THEN** the picker closes and that row's menu opens

#### Scenario: Right-click on empty space
- **WHEN** the picker is open and the player right-clicks empty space
- **THEN** the picker closes and no menu opens

#### Scenario: The row is deleted while the picker is open
- **WHEN** another player deletes the task while its picker is open
- **THEN** the picker closes

#### Scenario: The row scrolls out of view
- **WHEN** the player scrolls the list so the picker's row is out of view
- **THEN** the picker closes without changing the task

#### Scenario: Another player takes the lock
- **WHEN** a read-view picker is open and another player starts editing the document
- **THEN** the picker closes without changing the task

### Requirement: Date chip content and status

A dated datable task SHALL show a chip under its text in the read and editor views containing the date, in
the player's Date order with years counted from 1, and its status: "Due today" on the day, "in 1 day" or "in
N days" before it, "Overdue (1 day)" or "Overdue (N days)" after it while unchecked, counted from the
task's date clamped to its month's end. A checked task SHALL show only the date. Undated tasks, other
block kinds, assigned tasks, and rows in the Pin Tab, the Task Notice, a Tablet or the Assignment Desk SHALL
show no chip. A depth-1 subtask's chip SHALL sit under its own text.

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
- **WHEN** a dated task appears on a Tablet, in the Pin Tab or in a Task Notice
- **THEN** it shows no chip

### Requirement: Date chip follows the Date order and the world's day

The chip SHALL show its date in the player's current Date order, refreshing when the setting changes without
reopening the dialog. The chip SHALL be computed when its row is built, so a dialog left open while the
world's day changes SHALL NOT change a chip until the dialog next rebuilds.

#### Scenario: Date order setting
- **WHEN** the player changes the Date order setting
- **THEN** the chips show the dates in the new order without reopening the dialog

#### Scenario: Left open across midnight
- **WHEN** a dialog stays open while the world's day changes and has not rebuilt
- **THEN** the chip does not change until the dialog rebuilds

### Requirement: Date chip appearance

The chip SHALL be steady text, not animated. It SHALL use the four row constants `DateChipDue`,
`DateChipOverdue`, `DateChipUpcoming` and `DateChipDone` for due today, overdue, upcoming and completed
tasks, and those four constants SHALL have four different values. A completed task's chip SHALL be dimmed.

#### Scenario: Four distinct colors
- **WHEN** four tasks are due today, overdue, upcoming and completed
- **THEN** their chips use four different colors

### Requirement: Changing a date follows the editor lock

In the editor view a date change SHALL be part of the player's edits and saved with them, and a date set in
the editor and then discarded or closed without saving SHALL be lost like any other editor edit. In the read
view a date change SHALL apply to the document for every viewer, and SHALL be refused with the standard locked
message, leaving the date unchanged, while another player holds the document's editor lock. The client SHALL
check the lock when Add date, Change date or Repeat yearly is chosen, and the picker SHALL NOT open when the
document is locked. The server SHALL enforce the refusal.

#### Scenario: Editor view
- **WHEN** a player sets a date in the editor view and saves
- **THEN** the date is stored on the task for everyone

#### Scenario: Editor view closed without saving
- **WHEN** a player sets a date in the editor view and closes the editor discarding their edits
- **THEN** the task's date is unchanged

#### Scenario: Read view while nobody is editing
- **WHEN** a player sets a date from the read view of a Lectern nobody is editing
- **THEN** the date appears on the task for every viewer

#### Scenario: Read view while another player edits
- **WHEN** a player chooses Add date from the read view while another player holds the editor lock
- **THEN** the standard locked message appears and the picker does not open

#### Scenario: The lock is taken after the picker opened
- **WHEN** a player's read-view picker is open and another player takes the lock before the player presses Set
- **THEN** the picker closes without changing the task

### Requirement: The server applies one date operation at a time

A read-view date change SHALL be sent as one operation: set a date (keeping the task's repeat state), clear
the date, or set the repeat state. The server SHALL refuse a change when the document cannot be resolved,
the task id is unknown, the task is not datable, the document is read-only, the date is invalid (month
outside 1 to 12, day outside 1 to 255, year outside 1 to 9999), or another player holds the editor lock. On
every refusal the server SHALL send the authoritative document to the player who sent the change, so the
player's view shows the true state, and a refusal for the editor lock SHALL also show the standard locked
message; the other refusals show no message. The server SHALL ignore a repeat change on an undated task. A
change that is not refused SHALL be persisted for every viewer.

#### Scenario: Setting a date keeps the repeat state
- **WHEN** a player changes the date of a task that repeats yearly from the read view
- **THEN** the task has the new date and still repeats yearly

#### Scenario: Concurrent clear then toggle
- **WHEN** another player clears a task's date and, before the first player's view updates, the first
  player chooses Repeat yearly
- **THEN** the task stays undated and its repeat state stays off

#### Scenario: Invalid date
- **WHEN** the server receives a set-date for month 13, day 0 or Year 0
- **THEN** the task is unchanged, no message is shown, and the sender's view shows the unchanged task

#### Scenario: Not a datable task
- **WHEN** the server receives a set-date for a note, a tracker or an assigned task
- **THEN** the task is unchanged and the sender's view shows the unchanged task

#### Scenario: Read-only document
- **WHEN** the server receives a set-date for a task on a hard or fired Tablet
- **THEN** the task is unchanged and the sender's view shows the unchanged task

#### Scenario: Unknown task
- **WHEN** the server receives a set-date for a task another player just deleted
- **THEN** nothing changes

#### Scenario: A refused change reverts the chip
- **WHEN** a player's read-view date change shows a chip optimistically and the server refuses it
- **THEN** the chip returns to the task's real date and status

#### Scenario: Persisted for everyone
- **WHEN** a player sets a date and the server accepts it
- **THEN** the date, the repeat state and any pins on the task survive saving and reloading the document

### Requirement: Clearing a date

Clear date SHALL remove the task's date and its repeat state, and the task SHALL keep its identity, so a
pin on it stays attached.

#### Scenario: Clear keeps the pin
- **WHEN** a player clears the date of a pinned, dated task
- **THEN** the task has no date, no repeat, no chip, and is still pinned

### Requirement: Repeating does not change completion

Completing a task SHALL NOT depend on whether it repeats yearly: a repeating task SHALL complete under the
player's Completion Policy and Subtask Behavior exactly as a task that does not repeat. Under the Delete
policy a checked repeating task SHALL be deleted. Deleting it removes only that task: the repeat flag marks
the task as one that will recur, and any other instance of it, earlier or later, is a different task that the
deletion does not touch.

#### Scenario: Delete policy deletes a repeating task
- **WHEN** a player whose Completion Policy is Delete checks off a task that repeats yearly
- **THEN** the task is deleted like any other task

#### Scenario: Other policies act as chosen
- **WHEN** a player whose Completion Policy is Keep, Sink, Unpin or UnpinSink checks off a task that
  repeats yearly
- **THEN** the task is kept in place, moved to the bottom, kept in place and unpinned, or moved to the
  bottom and unpinned, exactly as for a task that does not repeat

#### Scenario: Subtask behavior ignores the repeat flag
- **WHEN** a player completes a parent task or a subtask where a task in the group repeats yearly
- **THEN** the Subtask Behavior (Bound, Independent or Discard children) applies as for a group with no
  repeating task
