## Purpose

Defines how a Scribe document block carries an optional calendar date, how that date is saved and exported, how the world's calendar shape is read, the status and occurrence rules for dated tasks, and how dates are displayed in the player's chosen order.

## ADDED Requirements

### Requirement: A block can carry an optional date and repeat flag

Any document block SHALL be able to carry an optional date made of a year (1 or later), a month (1 to 12)
and a day of the month (1 or later), and a "repeat yearly" flag. A block without a date SHALL behave exactly
as before. Adding, changing or removing a date by a player SHALL NOT change the block's identity, kind,
text, done state or pins. The repeat flag SHALL only exist on a dated block: removing a date SHALL clear it.

#### Scenario: Removing a date keeps the task
- **WHEN** a dated task has its date removed
- **THEN** it is the same task with the same identity, and pins to it still resolve

#### Scenario: Removing a date clears the repeat flag
- **WHEN** a repeating dated task has its date removed and a date is later added again
- **THEN** the task does not repeat until the flag is set again

#### Scenario: Undated documents save identically
- **WHEN** a document with only undated blocks is saved, reloaded and exported as JSON and TSV
- **THEN** every block's fields are unchanged, and the JSON and TSV exports match those of the same
  document exported before dates existed, apart from the JSON version number

### Requirement: Dates survive saving, syncing and copying

A block's date and repeat flag SHALL survive world save and load, network sync, Scriptorium copy (replace
and append), and Scriptorium JSON export and import. Documents saved before dates existed SHALL load with
no dates and no other change. Import from an export without dates SHALL produce undated blocks.

#### Scenario: Save and reload
- **WHEN** a world containing a dated, repeating task is saved and reloaded
- **THEN** the task has the same date and repeat flag

#### Scenario: Old saves load
- **WHEN** a document saved by a Scribe version without dates is loaded
- **THEN** it loads with all its blocks undated and otherwise unchanged

#### Scenario: Copy keeps dates
- **WHEN** a Scriptorium copies a document with dated tasks, replacing or appending to the target
- **THEN** the copied tasks have new identities and the same dates and repeat flags

#### Scenario: JSON round trip
- **WHEN** a document with dated blocks of several kinds is exported as JSON and imported again
- **THEN** each block keeps its date and repeat flag

### Requirement: TSV export carries dates for plain tasks only

The TSV export SHALL carry the date and repeat flag of plain task rows. Dates on other block kinds SHALL
be dropped from TSV, because those kinds already use the TSV reference cell.

#### Scenario: TSV round trip for a plain task
- **WHEN** a dated, repeating plain task is exported as TSV and imported again
- **THEN** it keeps its date and repeat flag

#### Scenario: TSV drops a tracker's date
- **WHEN** a dated tracker row is exported as TSV and imported again
- **THEN** it imports as an undated tracker with its item and count intact

### Requirement: Invalid stored dates read as undated

When a saved document, a synced document or an import contains a date with a year below 1, a month
outside 1 to 12, a day below 1, or an unreadable date value, that block SHALL load as undated with the
repeat flag cleared, and the rest of the document SHALL load normally. A repeat flag without a date SHALL
read as not repeating. A day past its month's end is valid (see the calendar shape requirement).

#### Scenario: Bad month in a saved document
- **WHEN** a saved document contains a block dated month 13
- **THEN** the document loads, and that block is undated

#### Scenario: Malformed JSON date
- **WHEN** a JSON import contains a block whose date text is not a valid year-month-day
- **THEN** the import succeeds, and that block is undated

#### Scenario: Malformed TSV date
- **WHEN** a TSV import contains a plain task whose date cell is malformed
- **THEN** the import succeeds, and that task is undated

### Requirement: The calendar shape follows the world

The system SHALL read the calendar shape from the world: 12 months per year, each with the world's days per
month.

#### Scenario: Shape from the world
- **WHEN** a world is set to 9 days per month
- **THEN** every month in the calendar shape has 9 days

### Requirement: A day past its month's end is clamped

A stored day beyond the last day of its month SHALL be shown and calculated as that month's last day,
without changing the stored value.

#### Scenario: Short months clamp
- **WHEN** a task is dated day 25 and the world has 9 days per month
- **THEN** the task is shown and treated as day 9 of that month

### Requirement: Years count from 1

Every year Scribe builds from a timestamp or a block date SHALL count from 1, where Year 1 is the world's
first calendar year, regardless of the game's internal year numbering. A date Scribe stored as text before
timestamps existed SHALL keep showing its stored text unchanged.

#### Scenario: First year of a world
- **WHEN** a player views a History entry, Guest Book visit or dated task from a new world's first year
- **THEN** it shows Year 1

#### Scenario: Consistent across Scribe
- **WHEN** a History entry, a Guest Book visit and a dated task all fall on the same day and the first two
  have timestamps
- **THEN** all three show the same year

#### Scenario: A legacy stored date is unchanged
- **WHEN** a Guest Book entry stored before timestamps existed is shown
- **THEN** it shows its stored date text as written, which may use a different year number

### Requirement: Stored identity dates are unaffected

The date text the server stores as an identity (on History entries, Guest Book visits, assignments and
pins) SHALL keep its existing form, and SHALL NOT depend on any player's Date order or on the count-from-1
rule. Players see these dates through the display formatter, built from the stored timestamp.

#### Scenario: Revisit on upgrade day
- **WHEN** a player visits a lectern, the server is updated to this version, and the player revisits the
  same lectern on the same in-game day
- **THEN** the Guest Book still has one entry for that player and day

#### Scenario: Date order does not change identity
- **WHEN** a singleplayer player changes Date order and then revisits a lectern on the same in-game day
- **THEN** the Guest Book still has one entry for that player and day

### Requirement: A dated task has a status relative to today

For a dated completable block (a task, tracker, link or craft), the system SHALL determine one
status relative to the world's current date, using the clamped day: **done** when checked, otherwise
**due today** when its date is today, **upcoming** with a count of days until it when its date is later, or
**overdue** with a count of days since it when its date has passed. Undated blocks and non-completable
blocks (notes and quest objectives) SHALL have no status.

#### Scenario: Overdue count
- **WHEN** an unchecked task is dated 3 days before today
- **THEN** its status is overdue by 3 days

#### Scenario: Checked task is done
- **WHEN** a task dated before today is checked
- **THEN** its status is done, not overdue

#### Scenario: Due today
- **WHEN** an unchecked task is dated today
- **THEN** its status is due today

#### Scenario: Upcoming across a year boundary
- **WHEN** the world has 9 days per month, today is day 9 of month 12, Year 2, and an unchecked task is
  dated day 1 of month 1, Year 3
- **THEN** its status is upcoming in 1 day

#### Scenario: Clamped day is due today
- **WHEN** the world has 9 days per month, today is day 9 of May, and an unchecked task is dated 25 May
- **THEN** its status is due today

#### Scenario: A dated note has no status
- **WHEN** a Text note carries a date before today
- **THEN** it has no status and is never shown as overdue

### Requirement: A repeating task's next occurrence is defined

For a checked, completable task with "repeat yearly" whose date is before today, the system SHALL define
its next occurrence as the stored month and day in the earliest year that puts the clamped date on or after
today, unchecked. A checked repeating task whose date is today or later SHALL have no rollover yet. An
unchecked repeating task whose date has passed SHALL stay overdue on its date. Rollover is the only change
to a block's done state that a date causes.

#### Scenario: Rollover after the date passes
- **WHEN** a repeating task dated 5 May, Year 3 is checked and today is 6 May, Year 3
- **THEN** its next occurrence is 5 May, Year 4, unchecked

#### Scenario: Checked early
- **WHEN** a repeating task dated 5 May, Year 3 is checked on 2 May, Year 3
- **THEN** it stays checked with its date unchanged until its date has passed

#### Scenario: Late rollover lands on the anniversary
- **WHEN** a repeating task dated 5 May, Year 3 is checked, and its rollover is first evaluated on
  5 May, Year 4
- **THEN** its next occurrence is 5 May, Year 4, unchecked, and it is due today

#### Scenario: Several years skipped
- **WHEN** a repeating task dated 5 May, Year 3 is checked, and its rollover is first evaluated on
  1 June, Year 6
- **THEN** its next occurrence is 5 May, Year 7, unchecked

#### Scenario: Unchecked past date stays overdue
- **WHEN** a repeating task dated 5 May, Year 3 is unchecked and today is 6 May, Year 3
- **THEN** its date stays 5 May, Year 3 and it is overdue by 1 day

#### Scenario: Stored day carries forward
- **WHEN** the world has 9 days per month and a checked repeating task stored as 25 May, Year 3 rolls over
- **THEN** its next occurrence is stored as 25 May, Year 4 and shown as 9 May, Year 4

### Requirement: Dates are shown with month names

Every date Scribe builds from a timestamp or a block date SHALL show the month by its localized name, never
its number.

#### Scenario: Month name
- **WHEN** a dated task falls in the fifth month
- **THEN** its date shows "May", not "5"

### Requirement: Dates display in the player's chosen order

Every date Scribe builds from a timestamp or a block date SHALL follow the viewing player's **Date order**
setting: Language default (the language's own `date-format` template), Day-Month-Year, Month-Day-Year or
Year-Month-Day. The default SHALL be Language default. This SHALL cover History, Guest Book, Inbox and Sent
Assignment History dates, Task Notice and held-item "Assigned by" dates, and the Pin Tab and HUD assignment
tooltips. Dates shown from stored text (see "Years count from 1") are not reordered.

#### Scenario: Month-first order
- **WHEN** a player sets Date order to Month-Day-Year
- **THEN** a History date that was shown as "5 May, Year 3" is shown as "May 5, Year 3", and so are the
  Guest Book, Inbox and tooltip dates

#### Scenario: Default follows the language
- **WHEN** a player has not changed Date order
- **THEN** dates use their language's own date template
