## ADDED Requirements

### Requirement: Persisted per-assignment log
Every `ScribeAssignment` SHALL carry an ordered, append-only list of log entries recording
its lifecycle. Each log entry SHALL record its kind (Accepted, Completed, Declined,
Cancelled, or Discarded) and a pre-formatted in-game-calendar date string, minted once at
the moment the event occurs (matching how `AssignedDate` is produced), never re-derived or
re-sorted afterward. The log SHALL be present on both the server's canonical assignment
record and any placed clone of that assignment inside a player's document, and SHALL
survive being written to and read back from both existing persistence codecs (the
assignment store's binary blob and the document codec's binary blob).

#### Scenario: Log entries persist through a server restart
- **WHEN** an assignment with one or more log entries is saved as part of the assignment
  store or a player's document, and the world is reloaded
- **THEN** every log entry (kind and date) is present, in the same order, after reload

#### Scenario: A pre-existing assignment from before this feature shipped has no log
- **WHEN** a save file created before this feature shipped is loaded
- **THEN** any assignment it contains deserializes successfully with an empty log list,
  rather than failing to load or fabricating retroactive entries

### Requirement: Accept records the placement target
When an assignee successfully accepts an assignment and it is placed onto a Scribe document
item, the system SHALL append an Accepted log entry recording the item's type name and
document title (in the `<Type> "<Title>"` shape already used for Accept-candidate labels),
alongside the entry's date. If the Accept action does not result in a successful placement
(no valid target resolved, the target item not writeable, or the target document full), no
Accepted log entry SHALL be appended.

#### Scenario: Accept onto a titled document
- **WHEN** an assignee accepts an assignment and it is placed onto a Notebook titled
  "The First Year"
- **THEN** the assignment's log gains an Accepted entry whose detail reads
  `Notebook "The First Year"` and whose date is the current in-game date

#### Scenario: Accept onto an untitled document
- **WHEN** an assignee accepts an assignment and it is placed onto a document that still
  has its default (untitled) title
- **THEN** the Accepted entry's detail is just the item's type name, with no title suffix,
  matching how Accept-candidate labels already omit an untitled document's title

#### Scenario: Accept fails to place
- **WHEN** an assignee's Accept request cannot be placed (e.g. the target document is full)
- **THEN** no Accepted log entry is appended, and the assignment's state is unaffected by
  the failed attempt

### Requirement: Terminal actions record a log entry
When an assignment transitions to Completed, Declined, Cancelled, or Discarded — through
any of the paths that can produce that transition, including completing the assignment's
underlying task (Completed) and deleting a placed, Accepted assignment block (Discarded) —
the system SHALL append a log entry of the matching kind with the current in-game date.

#### Scenario: Completing the underlying task logs Completed
- **WHEN** the assignee marks the assigned task's checkbox done, completing an Accepted
  assignment
- **THEN** the assignment's log (on both the canonical record and the placed clone) gains a
  Completed entry with the current in-game date

#### Scenario: Declining an assignment logs Declined
- **WHEN** the assignee declines an Unaccepted assignment via the Inbox
- **THEN** the assignment's log gains a Declined entry with the current in-game date

#### Scenario: Cancelling an assignment logs Cancelled
- **WHEN** the assigner cancels an assignment they sent
- **THEN** the assignment's log gains a Cancelled entry with the current in-game date

#### Scenario: Discarding via the Inbox logs Discarded
- **WHEN** a player discards an assignment via the Inbox's Discard action
- **THEN** the assignment's log gains a Discarded entry with the current in-game date

#### Scenario: Deleting a placed assignment block logs Discarded
- **WHEN** a player deletes a document block holding an Accepted assignment (rather than
  using the Inbox Discard action)
- **THEN** the assignment's log gains a Discarded entry with the current in-game date, the
  same as the Inbox Discard path

### Requirement: Expanded row renders the log in order
The shared Inbox row's expanded detail SHALL render every log entry on the viewed
assignment, in list order, beneath the existing "Assigned by" line — one line per entry,
worded per its kind (and, for Accepted, including its recorded placement detail) — on both
the Assignment Desk's Sent view and the Inbox's Received view.

#### Scenario: Expanding a row with a full lifecycle
- **WHEN** a player expands a row for an assignment that was accepted onto a Notebook titled
  "The First Year" and later completed
- **THEN** the expanded detail shows, in order: the "Assigned by" line, an
  `Accepted onto Notebook "The First Year" — <date>` line, and a `Completed — <date>` line

#### Scenario: Expanding a row with no lifecycle events yet
- **WHEN** a player expands a row for an assignment that is still Unaccepted
- **THEN** the expanded detail shows only the existing "Assigned by" line, with no log lines
  beneath it
