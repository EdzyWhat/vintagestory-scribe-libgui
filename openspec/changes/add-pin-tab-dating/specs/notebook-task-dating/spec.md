## MODIFIED Requirements

### Requirement: Date entries in the row menu

The row menu SHALL offer **Add date** on an undated datable task and **Change date** and **Clear date** on a
dated datable task, in the read view and the editor view of a Notebook, Clockmaker's Notebook, Lectern,
Chalkboard or Scriptorium document. A task is datable when it is a plain Task (not a note, tracker, link,
craft or quest objective) that was not placed by an assignment. Date entries SHALL form their own group
between the pin entries and Delete. They SHALL NOT be offered when the task is not datable, in a read view
whose document is read-only (a hard or fired Tablet), on a wet Tablet, or in the Assignment Desk. A depth-1 subtask is datable. The Pin Tab offers the date entries on a pin of a datable task, without Repeat
yearly, as the pinned-task-tab capability states.

#### Scenario: Undated task
- **WHEN** a player right-clicks an undated task in a Notebook's read view
- **THEN** the menu lists Add date, and no Change date, Clear date or Repeat yearly

#### Scenario: Dated task
- **WHEN** a player right-clicks a dated task in an editor view
- **THEN** the menu lists Change date, Clear date and Repeat yearly, with Repeat yearly showing its state

#### Scenario: Other kinds and surfaces
- **WHEN** a player right-clicks a note, a tracker, or any row on a Tablet or in the Assignment Desk
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
