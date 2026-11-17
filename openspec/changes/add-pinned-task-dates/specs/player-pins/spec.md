## ADDED Requirements

### Requirement: A pin's snapshot carries the task's date
A pin's persisted and synced snapshot SHALL include the pinned task's date (year, month and day), so the Pin
Tab and the HUD can show it without resolving the task's source document. Only a plain task (a Task block,
not an assignment) carries a date. The snapshot SHALL capture the date when the task is pinned, taking it
from the authoritative document when it can be resolved (the document wins over anything the client sent)
and from the pinning client's copy otherwise. The client's optimistic pin SHALL carry it too, so the chip
does not appear late. A client-supplied date SHALL be accepted only if it is a valid date and the pinned
task is a plain task; any other value (a zero or partial triple, an out-of-range month, day or year) SHALL
be treated as undated. A pin saved before this field existed SHALL load as undated, and a stored date the
snapshot cannot represent (month or day out of range, year outside 1 to 9999) SHALL load as undated.

#### Scenario: Pinning a dated task captures its date
- **WHEN** a player pins a task that has a date
- **THEN** the pin's snapshot records that date

#### Scenario: Pinning an undated task
- **WHEN** a player pins a task with no date
- **THEN** the pin's snapshot has no date

#### Scenario: A pin saved before dates still loads
- **WHEN** the pin store reads a pin blob written before this field existed
- **THEN** it loads successfully, with the pin undated

#### Scenario: A stored invalid date loads undated
- **WHEN** the pin store reads a pin blob whose date has month 13
- **THEN** the pin loads undated and the rest of the blob reads correctly

#### Scenario: An unresolvable source still pins with its date
- **WHEN** a player pins a dated task whose document the server cannot resolve
- **THEN** the pin's snapshot takes the date from the pinning client's copy

#### Scenario: An invalid client date is dropped
- **WHEN** a pin request for an unresolvable task carries month 0 with year 5
- **THEN** the pin is created undated

#### Scenario: The resolved document wins
- **WHEN** a pin request carries a date that differs from the date the server's resolved document has
- **THEN** the pin's snapshot takes the document's date

#### Scenario: A client date for a non-task is ignored
- **WHEN** a pin request for a tracker or note carries a date
- **THEN** the pin has no date

#### Scenario: The optimistic pin shows the chip at once
- **WHEN** a player pins a dated task and the server's confirmation has not yet arrived
- **THEN** the Pin Tab and HUD row already show the date chip

### Requirement: Pin date snapshots follow the task's date for every holder
When a task's date changes through any server path that persists a document (a read-view date change, a
Lectern or Chalkboard editor save, a Notebook whole-document save, or an external append), the system SHALL
update the date in the snapshot of every player's pin for that task and push the new list to each affected
player who is connected, not only the player who made the change. A player who is not connected keeps the
updated stored pin and receives it when they join. This refresh SHALL change only the date; the text and done
state of another player's pin keep the rule that only that player's own edit refreshes them, and the refresh
SHALL NOT add, remove, orphan or reorder a pin. A pin whose task is not in the document is left as it is. A
change to a document the server cannot resolve refreshes nothing. A pin made before this field existed stays
undated until its document is next saved through one of these paths or the task is pinned again.

#### Scenario: Changing a date updates the pin
- **WHEN** a player sets, changes or clears the date of a task they have pinned
- **THEN** their pin's snapshot shows the new date (or none) and their HUD and Pin Tab update

#### Scenario: Another player's pin follows the date
- **WHEN** player A changes the date of a task that player B has pinned
- **THEN** player B's pin snapshot shows the new date, and B's text and done snapshot are unchanged

#### Scenario: An offline holder catches up
- **WHEN** player A changes the date of a task that player B has pinned while B is offline
- **THEN** B's stored pin carries the new date and B receives it on joining

#### Scenario: A refresh never changes the pin set
- **WHEN** a date refresh runs for a document
- **THEN** no pin is added, removed, orphaned or moved

#### Scenario: A saved edit refreshes other holders
- **WHEN** a player saves an editor edit that changes a pinned task's date
- **THEN** every other holder's pin for that task shows the new date
