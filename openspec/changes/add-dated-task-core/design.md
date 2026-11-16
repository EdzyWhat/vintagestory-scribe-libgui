## Context

`ScribeBlock` already grows optional per-block fields (link target, extra info, assignment), each added to
`ScribeDocumentCodec` as an append-only versioned field behind a presence flag (now v13; see
`docs/CODEC-MIGRATION.md`). The JSON and TSV codecs are the Scriptorium's export lanes. Dates today are only
display strings: `NotebookHost.FormatCalendarDate` fills the `scribe:date-format` template, and
`FormatDateFromTimestamp` converts a stored `Calendar.TotalDays` back into a day, month and year.

Vintage Story facts that shape this: a year is always 12 months; days per month is a single world setting;
there are no weekdays; `IGameCalendar.Year` starts at 1386 (`StartYear`); `TotalDays` counts from January
1st of the first year, and a world starts part-way through it.

## Goals / Non-Goals

**Goals:**
- Pure, tested Core logic for dates, so every later change only adds UI.
- One formatter and one calendar adapter, so all of Scribe's dates agree.

**Non-Goals:**
- Any UI for setting dates, the calendar block, HUD badges or reminders.
- Uneven month lengths (the interface allows them; only the uniform shape ships).
- Times of day.

## Decisions

- **`ScribeDate` is a value type of (Year, Month 1-12, Day 1-n).** Stored as written, never as a day count,
  so it keeps its meaning if the calendar shape changes. Alternative: store `TotalDays`. Rejected because a
  change to days per month would silently move every task.
- **`ScribeBlock.Date` is nullable plus a separate `RepeatYearly` bool.** A null date is an undated task.
  `RepeatYearly` is ignored when there is no date. Alternative: a new `Dated` kind. Rejected because turning
  a task into an event would change its kind, and every kind-specific path (pins snapshot the kind) would
  need a migration.
- **Codec v14 appends `bool hasDate`, then `int year, byte month, byte day, bool repeatYearly` only when
  `hasDate`,** after the v13 timestamp. Reading v5 to v13 blobs leaves the date null. The JSON codec adds an
  optional `"date": "Y-M-D"` and `"repeatYearly": true` (v3). The TSV codec carries it inside the existing
  `Special` cell of a plain task as `date:Y-M-D` with an optional `,yearly`, keeping the fixed columns.
  `CloneWithNewIdentity` copies both fields.
- **`IScribeCalendarShape` answers month count and days in a month for a year.** `UniformCalendarShape`
  implements it from days per month. The Mod builds it from the world calendar in one adapter
  (`ScribeWorldCalendar`), which also gives today's `ScribeDate`.
- **Day ordinals for arithmetic.** The shape converts a date to a day ordinal (days since the start of the
  calendar), which gives days-between, clamping, and next occurrence. A stored day past its month's end is
  clamped to the last day for display and arithmetic, never rewritten.
- **Status is one function:** `Classify(date, done, today, shape)` returns `Done`, `DueToday`,
  `Upcoming(daysUntil)` or `Overdue(daysLate)`. Undated tasks have no status.
- **Repeat rollover rule (applied by a later change):** a checked repeating task whose date is before today
  moves to its next occurrence after today, unchecked. Checking it off on its date keeps it checked until
  the next midnight. Checking it early keeps it checked until its date passes. This needs no completion
  timestamp. Core exposes it as a pure function; nothing calls it yet.
- **Date order.** `ScribeDateOrder` is `LanguageDefault`, `DayMonthYear`, `MonthDayYear` or `YearMonthDay`.
  `LanguageDefault` uses `scribe:date-format`; the others use new templates `scribe:date-format-dmy`,
  `-mdy` and `-ymd`, so each locale still translates its own year word. Core composes the arguments and
  chooses the template key. The Mod resolves lang strings. Default is `LanguageDefault`.
- **Years count from 1 everywhere in Scribe.** The adapter derives every date from `Calendar.TotalDays`
  (which counts from January 1st of the first year): year = whole years elapsed + 1, month and day from the
  remainder. It never uses `Calendar.Year`, which starts at 1386. This is the rule `FormatDateFromTimestamp`
  already follows, so History, Guest Book and Assignment dates keep showing Year 1 for a world's first year.
  Alternative: show the game's 1386-based year. Rejected for readability.
- **All existing date helpers route through the formatter** (`FormatDate`, `FormatDateFromTimestamp`,
  History, Guest Book and Assignment displays), so the Date order setting applies everywhere at once.

## Risks / Trade-offs

- [`NotebookHost.FormatDate` uses `Calendar.Year`, which starts at 1386, while the timestamp path shows
  Year 1] → It moves onto the adapter's count-from-1 rule. Only places that showed the live date through
  `FormatDate` (the History draft preview and entries without a stored timestamp) change. A test checks the
  adapter's month and day match `Calendar.Month` and the day of month, to confirm `TotalDays` starts at
  January 1st.
- [New save format is one-way] → Standard for every codec bump; older-blob tests cover v13 and earlier.
- [Three new lang keys per locale] → Authored with the existing locale workflow; missing keys fall back to
  English.
