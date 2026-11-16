## Purpose

Defines how a Scribe task carries an optional calendar date, how that date is saved and exported, how the world's calendar shape is read, the status and occurrence rules for dated tasks, and how dates are displayed in the player's chosen order.

## ADDED Requirements

### Requirement: A task can carry an optional date and repeat flag

A task SHALL be able to carry an optional date made of a year, a month (1 to 12) and a day of the month, and
a "repeat yearly" flag. A task without a date SHALL behave exactly as before. Adding, changing or removing a
date SHALL NOT change the task's identity, kind, text, done state or pins. The repeat flag SHALL have no
effect on a task without a date.

#### Scenario: Removing a date keeps the task
- **WHEN** a dated task has its date removed
- **THEN** it is the same task with the same identity, and pins to it still resolve

#### Scenario: Undated tasks are unchanged
- **WHEN** a document contains only undated tasks
- **THEN** every existing behavior of those tasks is unchanged

### Requirement: Dates survive saving, syncing and export

A task's date and repeat flag SHALL survive world save and load, network sync, document copy, and the
Scriptorium's JSON and TSV export and import. Documents saved before dates existed SHALL load with no dates
and no other change. Import from an export without dates SHALL produce undated tasks.

#### Scenario: Save and reload
- **WHEN** a world containing a dated, repeating task is saved and reloaded
- **THEN** the task has the same date and repeat flag

#### Scenario: Old saves load
- **WHEN** a document saved by a Scribe version without dates is loaded
- **THEN** it loads with all its tasks undated and otherwise unchanged

#### Scenario: Export round trip
- **WHEN** a document with dated tasks is exported as JSON or TSV and imported again
- **THEN** each task keeps its date and repeat flag

### Requirement: The calendar shape follows the world

The system SHALL read the calendar shape from the world: 12 months per year, each with the world's days per
month. A stored day beyond the last day of its month SHALL be shown and calculated as that month's last day,
without changing the stored value.

#### Scenario: Short months clamp
- **WHEN** a task is dated day 25 and the world has 9 days per month
- **THEN** the task is shown and treated as day 9 of that month

### Requirement: Years count from 1

Every year Scribe stores for a task or displays SHALL count from 1, where Year 1 is the world's first
calendar year, regardless of the game's internal year numbering.

#### Scenario: First year of a world
- **WHEN** a player views any Scribe date during a new world's first year
- **THEN** it shows Year 1

#### Scenario: Consistent across Scribe
- **WHEN** a History entry, a Guest Book visit and a dated task all fall on the same day
- **THEN** all three show the same year

### Requirement: A dated task has a status relative to today

For a dated task the system SHALL determine one status relative to the world's current date: **done** when
checked, otherwise **due today** when its date is today, **upcoming** with a count of days until it when its
date is later, or **overdue** with a count of days since it when its date has passed. Undated tasks SHALL
have no status.

#### Scenario: Overdue count
- **WHEN** an unchecked task is dated 3 days before today
- **THEN** its status is overdue by 3 days

#### Scenario: Checked task is done
- **WHEN** a task dated before today is checked
- **THEN** its status is done, not overdue

### Requirement: A repeating task's next occurrence is defined

For a checked task with "repeat yearly" whose date is before today, the system SHALL define its next
occurrence as the same month and day in the earliest year that puts it after today, unchecked. A checked
repeating task whose date is today or later SHALL have no rollover yet.

#### Scenario: Rollover after the date passes
- **WHEN** a repeating task dated 5 May, Year 3 is checked and today is 6 May, Year 3
- **THEN** its next occurrence is 5 May, Year 4, unchecked

#### Scenario: Checked early
- **WHEN** a repeating task dated 5 May, Year 3 is checked on 2 May, Year 3
- **THEN** it stays checked with its date unchanged until its date has passed

### Requirement: Dates display in the player's chosen order

Every date Scribe displays SHALL use month names, never month numbers, and SHALL follow the player's
**Date order** setting: Language default (the language's own template), Day-Month-Year, Month-Day-Year or
Year-Month-Day. The default SHALL be Language default. The setting SHALL apply to all of Scribe's dates,
including History, Guest Book, Assignment and tooltip dates.

#### Scenario: Month-first order
- **WHEN** a player sets Date order to Month-Day-Year
- **THEN** a date that was shown as "5 May, Year 3" is shown as "May 5, Year 3" everywhere in Scribe

#### Scenario: Default follows the language
- **WHEN** a player has not changed Date order
- **THEN** dates use their language's own date template
