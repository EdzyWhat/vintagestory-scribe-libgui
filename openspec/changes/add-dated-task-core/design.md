## Context

`ScribeBlock` already grows optional per-block fields (link target, extra info, assignment). Each is a
constructor parameter, and each is added to `ScribeDocumentCodec` as an append-only versioned field behind
a presence flag (now v13; see `docs/CODEC-MIGRATION.md`). The JSON (v2) and TSV codecs are the
Scriptorium's export lanes. TSV has fixed columns, and Tracker, Link and Craft rows already use its
`Special` cell for their reference.

Dates today exist only as text and timestamps, in two roles:

- **Identity stamps.** `NotebookHost.FormatDate(sapi)` runs on the server and produces the date text
  stored as `InGameDate` on History entries, Guest Book visits (deduplicated on `(PlayerName, InGameDate)`
  in `GuestbookStore`), assignment transitions and pin snapshots (pin dirty-checking compares them). It
  uses `Calendar.Year`, which starts at 1386, in the server's language.
- **Display.** Where a numeric `Calendar.TotalDays` timestamp was stored alongside the text, the viewing
  client rebuilds the date with `FormatDateFromTimestamp`, which counts years from 1, in its own language
  through `scribe:date-format`. Rows stored before timestamps existed show their stored text. The History
  draft preview (`GuiDialogScribeNotebook`) calls `FormatDate(capi.World)` directly, so it shows 1386.

`ScribePlayerSettings` is client-local and never synced, so the server never knows a player's preferences.

Vintage Story facts that shape this: a year is always 12 months; days per month is a single world setting;
there are no weekdays; `IGameCalendar.Year` starts at 1386 (`IGameCalendar.StartYear`); `TotalDays` counts
from January 1st of the first year, and a world starts part-way through it.

## Goals / Non-Goals

**Goals:**
- Pure, tested Core logic for dates, so every later change only adds UI.
- One display formatter and one calendar adapter, so all of Scribe's displayed dates agree.
- No change to stored identity text.

**Non-Goals:**
- Any UI for setting dates, the calendar block, HUD badges or reminders.
- Calling the repeat rollover (the reminders change does that).
- Uneven month lengths (the interface allows them; only the uniform shape ships).
- Times of day.
- Rewriting dates stored as text before timestamps existed.

## Decisions

- **`ScribeDate` is a value type of (Year >= 1, Month 1-12, Day >= 1).** Stored as written, never as a day
  count, so its label stays the same if the calendar shape changes. (Today's date is also derived from
  `TotalDays`, so a days-per-month change still moves "now" relative to stored dates; the stored label just
  stays readable.) `ScribeDate.TryCreate` rejects out-of-range values and is the only way codecs build one.
  Alternative: store `TotalDays`. Rejected because a shape change would relabel every stored date.

- **`ScribeBlock.Date` is nullable plus a separate `RepeatYearly` bool, on any kind.** Both are constructor
  parameters, defaulting to null and false. A null date is an undated block. `WithDate(null)` also clears
  `RepeatYearly`, so the flag never exists without a date and the codec's "flag only when `hasDate`" rule
  loses nothing. Only plain tasks get date UI in later changes; the field is on every kind so the model
  doesn't have to change when more kinds get dates.
  Alternative: a new `Dated` kind. Rejected because turning a task into an event would change its kind,
  and every kind-specific path (pins snapshot the kind) would need a migration.

- **Copy and rebuild paths.**
  - `ScribeDocument.CloneBlockWithNewTaskId`, used by `CloneWithNewIdentity` and `AppendClonedBlocksFrom`
    (Scriptorium replace and append), copies both fields.
  - `ScribeImportValidator`'s Degrade (`src/Mod/`) keeps the date when it rebuilds a block.
  - The Assignment Desk capture and assignment placement drop it on purpose. The roadmap has no dates on
    assignments, and leaving them out keeps assignment records unchanged.

- **Codec v14 appends `bool hasDate`, then `int year, byte month, byte day, bool repeatYearly` only when
  `hasDate`,** after the v13 timestamp. Reading v5 to v13 blobs leaves the date null. If
  `ScribeDate.TryCreate` rejects the values read, the block loads undated and the rest of the document reads
  normally (the field has a fixed size, so the stream stays aligned).

- **JSON v3** adds an optional `"date": "Y-M-D"` and `"repeatYearly": true` on every kind. A malformed or
  out-of-range `date` reads as undated, and `repeatYearly` without `date` reads false.

- **TSV** carries the date only in the `Special` cell of a plain task, as `date:Y-M-D` with an optional
  `,yearly`. The fixed columns stay. Other kinds' dates are dropped from TSV, the same way `LinkDescription`
  is (documented in `docs/CODEC-MIGRATION.md`). A malformed cell imports undated.

- **`IScribeCalendarShape` answers month count and days in a month for a year.** `UniformCalendarShape`
  implements it from days per month. The Mod builds it from the world calendar in one adapter
  (`ScribeWorldCalendar`), which also gives today's `ScribeDate`.

- **Day ordinals for arithmetic.** The shape converts a date to a day ordinal (days since the start of
  Year 1), which gives days-between, clamping, and next occurrence. A stored day past its month's end is
  clamped to the last day for display and arithmetic, and never rewritten.

- **Status is one function in `src/Core/ScribeDateRules.cs`:**
  `ScribeDateRules.Classify(block, today, shape)` returns `ScribeDateStatus?`, a record struct of
  `Kind` (`Done`, `DueToday`, `Upcoming`, `Overdue`) and `Days` (until or since; 0 for Done and DueToday).
  It returns null for undated or non-completable blocks (`!IsCompletable`: notes and quest objectives).

- **Repeat rollover rule.** `ScribeDateRules.NextOccurrence(block, today, shape)` is a pure function that
  nothing calls yet:
  - a checked, completable, repeating block whose clamped date is before today moves to the stored month and
    day in the earliest year whose clamped date is on or after today, and is unchecked;
  - "on or after" means a rollover first evaluated on an anniversary lands on today and is due today;
  - checking it off on its date keeps it checked until the date passes;
  - checking it early keeps it checked until its date passes;
  - an unchecked past date is left alone (it stays overdue).
  This needs no completion timestamp. The reminders change calls it lazily, on the server, whenever a
  document is loaded or viewed. That covers documents that sit unloaded for a long time (notebooks in
  chests, calendar blocks in unloaded chunks), and "on or after" makes a late evaluation safe.

- **Date order.** `ScribeDateOrder` is `LanguageDefault`, `DayMonthYear`, `MonthDayYear` or `YearMonthDay`
  (default `LanguageDefault`). Core's `ScribeDateText.TemplateKey(order)` only picks the key:
  `date-format`, `date-format-dmy`, `date-format-mdy` or `date-format-ymd`. Every template takes the same
  arguments (`{0}` = day, `{1}` = localized month name, `{2}` = year), and only the template text sets the
  order. The three fixed-order templates must keep their placeholder order (a scribe-locales rule that
  `build/check-locales.py` enforces), and `en.json` gets a `_comment-*` translator note for them. English
  values: `{0} {1}, Year {2}`, `{1} {0}, Year {2}` and `Year {2}, {1} {0}`.

- **Identity stamps never change.** `FormatDate(ICoreServerAPI)` keeps its current output: `Calendar.Year`,
  `scribe:date-format`, the server's language, and it never reads Date order. This avoids duplicate Guest
  Book entries on upgrade day, and it avoids identity text that changes with a display preference in
  singleplayer, where the server shares the client process. Every stamp written since timestamps were added
  sits beside a `TotalDays` timestamp, and players only ever see the display form rebuilt from it.

- **Display helpers take the order explicitly.** `NotebookHost.FormatDateFromTimestamp(world, totalDays,
  order)` and `FormatCalendarDate(..., order)` build every displayed date through `ScribeWorldCalendar` and
  the chosen template. Callers on the client (`HistoryDisplay`, `AssignmentDisplay`, the Guest Book view,
  the Task Notice and held-item lines, the Pin Tab and HUD tooltips, the document slot tooltip) pass
  `MySettings.DateOrder`. The History draft preview stops calling `FormatDate` and calls
  `FormatDateFromTimestamp` with today's `TotalDays`, so it counts years from 1 like everything else.
  Changing the setting goes through `ScribeModSystem.UpdateMySettings`, which fires `MyPinsChanged`. The
  HUD and open dialogs already rebuild on that event for other live settings. Task 4.4 confirms that every
  date surface refreshes, and wires any that don't.

- **Years count from 1 in every display.** The adapter derives every displayed date from
  `Calendar.TotalDays`: year = whole years elapsed + 1, month and day from the remainder. It never uses
  `Calendar.Year`. This is the rule `FormatDateFromTimestamp` already follows. Dates stored as text before
  timestamps existed keep their stored text, which notebook-history already requires (no reverse-deriving).
  Alternative: show the game's 1386-based year. Rejected for readability, by the author's decision.

## Risks / Trade-offs

- [Legacy rows without a timestamp show 1386-based years in the server's order] → Accepted and specified.
  Those rows predate timestamps and are rare in current worlds.
- [Identity text still contains 1386] → Accepted. It is never shown when a timestamp exists, and keeping it
  avoids an upgrade-day duplicate and any migration.
- [New save format is one-way] → Standard for every codec bump. Older-blob tests cover v13 and earlier.
- [Nine new lang keys per locale] → Authored with the existing locale workflow. The locale checker
  requires them in every shipped locale, so none are missing at release.
