## 1. Core date model

- [x] 1.1 Add `ScribeDate` (year >= 1, month 1-12, day >= 1) with `TryCreate`, and `IScribeCalendarShape` with `UniformCalendarShape`, including day-ordinal conversion and clamping; verify with Core tests for 3, 9 and 30 days per month and for rejected values (year 0, month 0 and 13, day 0)
- [x] 1.2 Add `ScribeDateRules.Classify` returning `ScribeDateStatus?` (record struct of `Kind` and `Days`) in `src/Core/ScribeDateRules.cs`; verify Core tests for done, due today, upcoming, overdue, a year-boundary count, a clamped day due today, and null for undated, Text and QuestObjective blocks
- [x] 1.3 Add `ScribeDateRules.NextOccurrence` (on or after today, stored day carried, completable blocks only, unchecked past dates untouched); verify Core tests for the spec's rollover, checked-early, anniversary, several-years, unchecked-overdue and stored-day scenarios
- [x] 1.4 Add `ScribeBlock.Date` (nullable) and `RepeatYearly` as constructor parameters and properties, where setting `Date` to null clears `RepeatYearly` and `RepeatYearly` cannot be set without a date; copy both in `ScribeDocument.CloneBlockWithNewTaskId`; verify Core tests that `CloneWithNewIdentity` and `AppendClonedBlocksFrom` keep both, that a cleared date keeps the TaskId, and that clearing the date clears the flag
- [x] 1.5 Keep the date in `ScribeImportValidator`'s Degrade rebuild; leave the Assignment Desk capture and assignment placement rebuilds without it; verify a test that Degrade keeps a date

## 2. Save format and export

- [x] 2.1 Bump `ScribeDocumentCodec` to v14 with the date fields appended after v13, read through `ScribeDate.TryCreate` (invalid values give an undated block and the stream stays aligned); update the doc-comment field history and accepted window; add a v13 older-blob test and a null-date assertion to the existing v5, v6, v7, v11 and v12 older-blob tests; add a test for an out-of-range month; refresh the stale `Version=8` comment in `ScribeDocumentCodecTests.cs`
- [x] 2.2 Add `date` and `repeatYearly` to `ScribeDocumentJsonCodec` (v3) for every kind; verify a round-trip test with several kinds, that a v2 payload imports undated, and that a malformed date and a lone `repeatYearly` import undated
- [x] 2.3 Carry the date in the TSV `Special` cell of plain tasks only (`date:Y-M-D[,yearly]`); verify a round-trip test, that a dated tracker imports undated with its item and count intact, and that a malformed date cell imports undated
- [x] 2.4 Update `docs/CODEC-MIGRATION.md`: binary v14 fields and window, the JSON section and summary row (correct the stale v1 to v3, noting v2's `extraInfo`), the TSV date payload, and the omitted-by-design note for non-task TSV dates and assignment rebuilds; verify by reading that every version number matches the code constants

## 3. Formatting and settings

- [x] 3.1 Add `ScribeDateOrder` and `ScribePlayerSettings.DateOrder` (default LanguageDefault) with normalization of unknown values; verify the settings tests
- [x] 3.2 Add `ScribeDateText.TemplateKey(order)`, which only picks the key; verify Core tests for all four orders
- [x] 3.3 Add the nine lang keys to `en.json` (`date-format-dmy`, `-mdy`, `-ymd`, the Date order label and helptext, and four option labels) plus a `_comment-*` note on the fixed placeholder order, and translate them in every locale file; extend `build/check-locales.py` to check the placeholder order of the three templates; verify the locale check in `build/verify.sh` passes and fails on a reordered `-mdy`

## 4. Mod adapter and display

- [x] 4.1 Add `ScribeWorldCalendar` building the calendar shape and today's `ScribeDate` from `Calendar.TotalDays`, with years counted from 1; verify an Atlas scenario that its month matches `Calendar.MonthName` and its year equals `Calendar.Year + 1` (`Calendar.Year` counts from 0)
- [x] 4.2 Give `FormatDateFromTimestamp` a `ScribeDateOrder` parameter and route it through the adapter (`ScribeWorldCalendar.Format` replaces `FormatCalendarDate`); leave `FormatDate(ICoreServerAPI)` output unchanged; verify an Atlas scenario that `FormatDate(sapi)` returns the same text as before the change for a fixed day, and that `FormatDateFromTimestamp(world, TotalDays, LanguageDefault)` shows Year 1 in a new world
- [x] 4.3 Pass `MySettings.DateOrder` from every client display caller (History, Guest Book, Inbox and Sent Assignment History, Task Notice and held-item lines, Pin Tab and HUD tooltips, document slot tooltip), and switch the History draft preview from `FormatDate` to `FormatDateFromTimestamp` with today's `TotalDays`; verify with a grep that no client display path calls `FormatDate`
- [x] 4.4 Add the Date order dropdown to the Window Appearance section of Scribe Settings with label, helptext and option labels; confirm each date surface in 4.3 refreshes when `UpdateMySettings` fires `MyPinsChanged`, and wire any that don't; verify with a TESTING.md item (task 5.2)

## 5. Verification

- [x] 5.1 Run `build/verify.sh` with the client closed and verify Core and Atlas are green
- [x] 5.2 Add playtest items to `TESTING.md`: an old save loads with tasks unchanged; each Date order shows correctly in History, Guest Book, Inbox and a pin tooltip, and changes live with those views open; the History draft preview shows Year 1-based years; revisiting a lectern on the same day after the upgrade adds no second Guest Book entry
