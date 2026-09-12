## Purpose

Lets another installed mod create a task on a player's behalf through one public, server-side
Scribe method, without that mod needing to understand Scribe's internal document model.

## ADDED Requirements

### Requirement: External task creation entry point
Scribe SHALL expose one public, server-side method that any other mod can call to create a task on
a specified player's behalf: `TryCreateExternalTask(player, title, bodyText, extraInfo)`, returning
a boolean success indicator. The method SHALL be usable by a calling mod that only holds Scribe as
an optional dependency — Scribe SHALL NOT require a calling mod to be present, and calling this
method SHALL require no network round trip (it runs entirely within the server process).

#### Scenario: A mod with Scribe installed calls the method
- **WHEN** another mod resolves Scribe's mod system and calls `TryCreateExternalTask` with a valid
  player, a title, and no other optional content
- **THEN** Scribe creates a task for that player and the call returns `true`

#### Scenario: Scribe is not installed
- **WHEN** a calling mod checks for Scribe's presence before calling and Scribe is absent
- **THEN** the calling mod never invokes the method, and Scribe requires no code changes to support
  this — the method's existence is the only contract

### Requirement: Title and body text mapping
`title` and `bodyText` SHALL each be optional, but at least a meaningful mapping SHALL exist for
every combination Scribe is willing to accept:
- When `title` is present, it SHALL become a checkbox task at the top level.
- When `bodyText` is also present, it SHALL become a freeform, non-checkbox note nested one level
  beneath the title task.
- When `title` is absent and `bodyText` is present, the note SHALL become a standalone top-level
  item instead of an orphaned nested note.
- Both `title` and `bodyText` SHALL be truncated (not rejected) when they exceed Scribe's existing
  per-kind text length limits, matching every other text field's clipping behavior.

#### Scenario: Title and body both supplied
- **WHEN** a caller supplies both a title and body text
- **THEN** the player's document gains a top-level checkbox task showing the title, with the body
  text visible as a non-checkbox note nested beneath it

#### Scenario: Title only
- **WHEN** a caller supplies a title and omits body text
- **THEN** the player's document gains a single top-level checkbox task with no nested note

#### Scenario: Body text only, no title
- **WHEN** a caller omits the title and supplies only body text
- **THEN** the player's document gains a single top-level non-checkbox note carrying that text, not
  a nested note with no parent

#### Scenario: Over-length input
- **WHEN** a caller supplies a title or body text longer than Scribe's existing text length limits
- **THEN** Scribe truncates the text to the limit rather than rejecting the call

### Requirement: Extra info hover detail
A caller MAY supply an opaque `extraInfo` string whose content and format Scribe SHALL NOT interpret
or validate beyond length. When supplied and non-empty, it SHALL be attached to whichever block ends
up at the top level (the title task, or the promoted body note when there is no title) and SHALL be
shown to the viewing player as hoverable detail on that row, distinct from and independent of
Scribe's existing player-to-player assignment marker. When `extraInfo` is absent or empty, no
hoverable-detail affordance SHALL appear on the row at all.

#### Scenario: Extra info supplied
- **WHEN** a caller supplies a non-empty `extraInfo` string alongside a title
- **THEN** the created top-level task shows a hoverable-detail affordance, and hovering it reveals
  exactly the text the caller supplied

#### Scenario: Extra info omitted
- **WHEN** a caller omits `extraInfo` or supplies an empty string
- **THEN** the created task shows no hoverable-detail affordance of any kind

#### Scenario: Extra info visibility across surfaces
- **WHEN** a task created with `extraInfo` is pinned
- **THEN** the hoverable-detail affordance SHALL remain visible in the player's Pin Tab, but SHALL
  NOT appear in the HUD's pinned-task display

### Requirement: Target resolution
Scribe SHALL resolve which of the target player's own carried Scribe items receives the new task
without prompting the player to choose. Scribe SHALL prefer the Scribe item that player most
recently opened, if it is currently carried and writeable; otherwise Scribe SHALL fall back to the
first writeable Scribe item found in that player's own hotbar and backpack. Ground, chest, and
creative inventories SHALL NOT be considered.

#### Scenario: Player carries their last-opened item
- **WHEN** the target player is carrying the same writeable Scribe item they most recently opened
- **THEN** the new task is added to that item's document with no prompt shown

#### Scenario: Last-opened item unavailable, another writeable item carried
- **WHEN** the target player's last-opened Scribe item is not currently carried, but they carry a
  different writeable Scribe item
- **THEN** the new task is added to that other item's document with no prompt shown

#### Scenario: No eligible item
- **WHEN** the target player carries no writeable Scribe item at all
- **THEN** the call fails and no task is created

### Requirement: Failure notification
When `TryCreateExternalTask` cannot place the task, Scribe SHALL notify the target player directly
with a reason distinguishing at least: no Scribe item carried at all, every carried Scribe item is
locked (hardened/fired), and the resolved target document has no room left. The calling mod is not
required to build any user-facing error handling of its own — Scribe's own notification SHALL be
sufficient, and the method's boolean return value exists only for the calling mod's own internal
control flow.

#### Scenario: No Scribe item carried
- **WHEN** the target player carries no Scribe item and a call is made on their behalf
- **THEN** the call returns `false` and the target player receives a notice that they have no
  Scribe item

#### Scenario: Every carried item is locked
- **WHEN** every Scribe item the target player carries is hardened or fired (read-only)
- **THEN** the call returns `false` and the target player receives a notice distinguishing this
  from having no Scribe item at all

#### Scenario: Target document full
- **WHEN** the resolved target document has already reached its task capacity
- **THEN** the call returns `false` and the target player receives a notice that the target is full
