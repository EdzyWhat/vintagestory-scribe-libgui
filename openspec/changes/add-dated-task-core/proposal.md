## Why

Dated tasks are the base of the calendar work (see `docs/calendar-roadmap.md`). Before any calendar UI,
Scribe needs a way to store a date on a task, a single source for the world's calendar shape, the rules
that classify a dated task (upcoming, due today, overdue, done), and one date formatter that respects the
player's date order. Building this first, as pure logic with tests, keeps the later UI changes small.

## What Changes

- A task can carry an optional date (year, month, day) and a "repeat yearly" flag. The date is a field on
  the task, not a new task kind, so adding or removing it never changes the task's identity.
- The document save format gains these fields (binary codec version 14). Older saves load unchanged with no
  dates. The Scriptorium's JSON and TSV export lanes carry the date too.
- A calendar shape: 12 months, each with the world's days per month, asked for through one interface so
  uneven months can be supported later.
- Rules for a dated task's status relative to today, days until or since its date, clamping a stored day
  that falls past its month's end, and a repeating task's next occurrence after it is checked off.
- One date formatter for every date Scribe shows. A new **Date order** setting (Language default,
  Day-Month-Year, Month-Day-Year, Year-Month-Day) applies to it, including today's History, Guest Book,
  Assignment and tooltip dates.
- **Years count from 1 everywhere in Scribe.** All dates come from one calendar adapter; the one helper that
  used the game's 1386-based year moves onto the same rule as the rest.
- No dated-task UI yet: nothing lets a player set a date until the Notebook dating change.

## Capabilities

### New Capabilities
- `dated-task`: how a task's date is stored and saved, the calendar shape, status and occurrence rules, and
  date display order.

### Modified Capabilities
None. The codec bump follows the existing append-only rules in `codec-migration`; its requirements do not
change.

## Impact

- `src/Core`: new date, calendar-shape, status and formatting types; `ScribeBlock` gains two fields;
  `ScribeDocumentCodec` (v14), `ScribeDocumentJsonCodec` (v3) and `ScribeDocumentTsvCodec`;
  `ScribePlayerSettings` gains `DateOrder`. Core tests cover all of it.
- `src/Mod`: one world-calendar adapter that builds the shape and today's date; `NotebookHost`'s date
  helpers route through the new formatter; a Date order row in Scribe Settings; three lang keys per locale.
- Save compatibility: v13 documents load; once re-saved they are v14 and cannot be read by older Scribe
  builds (the normal one-way upgrade).
