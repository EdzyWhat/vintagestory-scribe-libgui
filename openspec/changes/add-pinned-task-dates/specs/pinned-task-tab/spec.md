## ADDED Requirements

### Requirement: Pin Tab rows show the pinned task's date
In a dialog that offers dates (every Scribe dialog except a Tablet and the Assignment Desk), a Pin Tab row
for a plain task that has a date SHALL show the same date chip a Notebook row shows, built from the pin's
snapshot (so it appears with the source document unloaded): for an unchecked task the status and the date,
and for a checked task the date alone, dimmed. The date SHALL be formatted in the viewing player's Date
order with years counted from 1. The chip sits beneath the task text, wraps with it, and does not change the
row's other controls; a row without a chip is exactly as tall as it was before dates. Rows of other kinds,
assignment tasks, undated tasks and orphaned pins show no chip, and a Tablet or the Assignment Desk shows
none on any row. The chip is display-only. A deleted or unpinned row's departing animation keeps the chip so
the row collapses at its real height. The chip is computed when the Pin Tab builds, so a Pin Tab left open
across a day change updates on its next rebuild.

#### Scenario: A pinned dated task shows its date
- **WHEN** a player opens the Pin Tab with a pinned task dated for three days from today
- **THEN** its row shows the status (for example "in 3 days") and the date beneath the text

#### Scenario: A checked task shows only the date
- **WHEN** a pinned dated task is checked off and rests on the Pin Tab
- **THEN** its chip shows the date alone, dimmed

#### Scenario: The date changes while the Pin Tab is open
- **WHEN** a pinned task's date is changed from a notebook while the Pin Tab is open
- **THEN** the row's chip shows the new date without reopening the Pin Tab

#### Scenario: A pin whose source is unloaded still shows its date
- **WHEN** a pinned dated task's source document cannot be resolved
- **THEN** the row still shows the chip from the pin's snapshot

#### Scenario: Undated and other-kind pins show no chip
- **WHEN** the Pin Tab lists an undated task, a tracker, a note and an assignment task
- **THEN** none of those rows shows a date chip, and the undated rows are the height they were before dates

#### Scenario: An orphaned pin shows no chip
- **WHEN** a pinned dated task's task has been permanently deleted and its pin is orphaned
- **THEN** its row shows no chip

#### Scenario: A Tablet shows no chip
- **WHEN** the Pin Tab is shown in a Tablet or the Assignment Desk and lists a pinned dated task
- **THEN** its row shows no chip
