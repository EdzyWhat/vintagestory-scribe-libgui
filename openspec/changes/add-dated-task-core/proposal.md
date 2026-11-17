## Why

Dated tasks are the base of the calendar work (see `docs/calendar-roadmap.md`). Before any calendar UI,
Scribe needs a way to store a date on a task, a single source for the world's calendar shape, the rules
that classify a dated task (upcoming, due today, overdue, done), and one display formatter that respects the
player's date order. Building this first, as pure logic with tests, keeps the later UI changes small.

## What Changes

- Any document block can carry an optional date (year, month, day) and a "repeat yearly" flag. The date is
  a field on the block, not a new kind, so adding or removing it never changes the block's identity. Only
  plain tasks get date UI in later changes. Removing a date clears its repeat flag.
- The document save format gains these fields (binary codec version 14). Older saves load unchanged with no
  dates. Invalid stored dates load as undated. The Scriptorium's JSON export carries dates on every kind;
  TSV carries them on plain tasks only.
- A calendar shape: 12 months, each with the world's days per month, asked for through one interface so
  uneven months can be supported later.
- Rules for a dated task's status relative to today, days until or since its date, clamping a stored day
  that falls past its month's end, and a repeating task's next occurrence (on or after today) once it is
  checked off. Status applies only to completable blocks.
- One display formatter for every date Scribe builds from a timestamp or a block date. A new **Date order**
  setting (Language default, Day-Month-Year, Month-Day-Year, Year-Month-Day) in Window Appearance applies to
  existing History, Guest Book, Inbox, Task Notice and pin tooltip dates, and updates open views live.
- **Displayed years count from 1 everywhere in Scribe.** Every displayed date is built through one calendar
  adapter from `Calendar.TotalDays`. The draft History preview, the only display that used the game's
  0-based `Calendar.Year`, moves onto that rule.
- **Stored identity dates do not change.** The date text the server stores as an identity (Guest Book
  dedup, History, assignments, pins) keeps its current form and never reads Date order, so no duplicate
  Guest Book entries appear on upgrade. Dates stored as text before timestamps existed keep showing their
  stored text.
- No dated-task UI yet: nothing lets a player set a date until the Notebook dating change.

## Capabilities

### New Capabilities
- `dated-task`: how a block's date is stored, saved and validated, the calendar shape, status and
  occurrence rules, and date display order.

### Modified Capabilities
- `notebook-history`, `lectern-guestbook`, `inbox-tab`, `player-pins`, `scribe-item-hover-summary`,
  `task-note-document`: timestamp-built dates use the viewer's Date order template instead of always
  `scribe:date-format`. The Guest Book example date moves to Year 1.
- `settings-tab`: the Window Appearance section gains the Date order dropdown, which updates open views live.
- `scribe-locales`: the three fixed-order date templates are required and keep their placeholder order.
- `codec-migration`: the accepted-window requirement names the code's current constants instead of
  literal version numbers, which were already stale.

## Impact

- `src/Core`: new date, calendar-shape, status and formatting types; `ScribeBlock` gains two fields and
  constructor parameters; `ScribeDocument`'s clone helper copies them; `ScribeDocumentCodec` (v14),
  `ScribeDocumentJsonCodec` (v3) and `ScribeDocumentTsvCodec`; `ScribePlayerSettings` gains `DateOrder`.
  Core tests cover all of it.
- `src/Mod`: one world-calendar adapter; `NotebookHost`'s display helpers take the viewer's Date order
  while its server stamp helper stays as is; the Date order row in Scribe Settings; nine lang keys per
  locale (three templates, the setting's label and helptext, four option labels); `build/check-locales.py`
  checks the fixed placeholder order.
- `docs/CODEC-MIGRATION.md` updated for binary v14, JSON v3 (its stale v1 entries corrected) and the TSV
  date payload.
- Save compatibility: v13 documents load; once re-saved they are v14 and cannot be read by older Scribe
  builds (the normal one-way upgrade).
