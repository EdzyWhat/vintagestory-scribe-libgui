## ADDED Requirements

### Requirement: Editor rows enforce a per-kind character limit with player feedback

Each editor row's text SHALL be bounded by a character limit that depends on its kind: a
Standard Task to the task limit (1,000 characters) and a Note to the larger note limit (10,000
characters). The limit SHALL be enforced live in the editor field — once a row is at its
limit, further typed input is ignored and an over-long paste is truncated to fit — so the text
committed to the document never exceeds the cap. The document codec SHALL apply the same limit
as a server-authoritative backstop by **clipping** an over-long row's text to its kind's limit
on read, for BOTH kinds; it SHALL NOT reject or drop the whole document because one row is
over-long.

When the player's input is blocked or truncated because a row is at its limit, the editor
SHALL surface a transient in-game error that names the kind and its limit (e.g. "Tasks are
limited to 1,000 characters." / "Notes are limited to 10,000 characters."), via the same
in-game error channel used for other editor refusals (tablet-full, editor lock). The character
count shown in the message SHALL be derived from the enforced limit constant rather than being
written literally into the message text, so the message and the enforced cap cannot drift
apart.

#### Scenario: Typing at a task's limit is prevented with feedback

- **WHEN** a task row already contains its maximum characters and the player types another
  character
- **THEN** the extra input is ignored (the stored text is unchanged) and a transient in-game
  error stating the task character limit is shown

#### Scenario: Typing at a note's limit is prevented with feedback

- **WHEN** a note row already contains its maximum characters and the player types another
  character
- **THEN** the extra input is ignored and a transient in-game error stating the note character
  limit is shown

#### Scenario: An over-long paste is truncated to the limit

- **WHEN** the player pastes text that would push a row past its kind's limit
- **THEN** only the portion that fits up to the limit is inserted, and the limit-feedback
  message is shown

#### Scenario: The codec clips an over-limit note instead of dropping the document

- **WHEN** a document is read whose note row exceeds the note limit
- **THEN** that note's text is clipped to the note limit and the rest of the document loads
  normally, rather than the whole document being rejected

## MODIFIED Requirements

### Requirement: New tasks are created empty
When the player adds a block in the editor view — via the footer add control (the kind
picker) or by committing a row with Enter (insert-below) — the new block SHALL be created
with empty text rather than seeded with a placeholder literal (e.g. "New task"). This applies
to both kinds the picker creates: a Standard Task and a Note are each created empty. The new
row SHALL be focused so the player can type into the empty field immediately, with no
boilerplate text to select and delete first. Enter (insert-below) SHALL continue to insert a
task, matching the surrounding task-editing flow.

#### Scenario: Add task creates an empty focused row
- **WHEN** the player uses the add control to add a Standard Task
- **THEN** a new task row is added with empty text and receives focus, and its text field contains
  no pre-filled placeholder characters

#### Scenario: Add note creates an empty focused row
- **WHEN** the player uses the add control to add a Note
- **THEN** a new text-section row (no checkbox) is added with empty text and receives focus, and its
  text field contains no pre-filled placeholder characters

#### Scenario: Enter inserts an empty task below
- **WHEN** the player presses Enter (without Shift) while editing a non-empty task row
- **THEN** the current row is committed and a new empty task is inserted directly beneath it and
  focused, containing no pre-filled placeholder characters

### Requirement: An empty task row is removed when it loses focus
While in the editor view, when a row whose text is empty or whitespace-only loses focus (by
clicking away, moving to another row, switching to the read view, or closing the dialog), the
editor SHALL remove that block from the document rather than persisting it, and SHALL move focus to
the row immediately above the removed row when one exists. This applies to any empty editor row —
a task **or** a note — whether just created and abandoned without typing, or an existing block whose
text the player cleared (e.g. with select-all then Delete) — so that abandoned empty rows never grow
the list and a cleared row can be removed from the keyboard alone. Removal SHALL be applied through
the existing lock-gated server edit path and SHALL NOT leave an empty task or note visible in the read
view or persisted across reload. (The Core document model still stores text verbatim for both kinds;
this removal is an editing-layer behavior, not a model invariant.)

#### Scenario: Abandoned empty new task is removed on blur
- **WHEN** the player adds a task, types nothing, and moves focus away from that empty row
- **THEN** the empty task is removed from the document and does not appear in the read view or
  after reload, and the list is not grown by the abandoned add

#### Scenario: Abandoned empty new note is removed on blur
- **WHEN** the player adds a note, types nothing, and moves focus away from that empty row
- **THEN** the empty note is removed from the document and does not appear in the read view or
  after reload, and the list is not grown by the abandoned add

#### Scenario: Clearing a task's text then blurring removes the row
- **WHEN** the player selects all of an existing task row's text, deletes it, and then moves focus
  away from the now-empty row
- **THEN** the task is removed from the document and focus moves to the row directly above it

#### Scenario: Clearing a note's text then blurring removes the row
- **WHEN** the player selects all of an existing note row's text, deletes it, and then moves focus
  away from the now-empty row
- **THEN** the note is removed from the document and focus moves to the row directly above it

#### Scenario: Focus moves to the row above
- **WHEN** an empty row that is not the first row is removed on losing focus
- **THEN** focus moves to the task/note row that was directly above the removed row

#### Scenario: Switching to read or closing does not persist an empty row
- **WHEN** a task or note row is empty and the player switches to the read view or closes the dialog
- **THEN** the empty row is removed rather than saved, and the read view / reloaded document shows
  no empty row
