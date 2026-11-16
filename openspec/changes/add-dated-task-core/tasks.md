## 1. Core date model

- [ ] 1.1 Add `ScribeDate` (year, month, day) and `IScribeCalendarShape` with `UniformCalendarShape`, including day-ordinal conversion and clamping; verify with Core tests for 3, 9 and 30 days per month
- [ ] 1.2 Add the status function (done, due today, upcoming with days until, overdue with days late) and the repeat next-occurrence function; verify Core tests cover each status, clamping, early and late check-off, and year boundaries
- [ ] 1.3 Add `ScribeBlock.Date` (nullable) and `RepeatYearly`, copied by `CloneWithNewIdentity`; verify a Core test that a clone keeps both and a cleared date keeps the TaskId

## 2. Save format and export

- [ ] 2.1 Bump `ScribeDocumentCodec` to v14 with the date fields appended after v13, update the doc-comment field history and accepted window, and add a v13 older-blob test; verify v5 to v13 fixtures still load with null dates
- [ ] 2.2 Add `date` and `repeatYearly` to `ScribeDocumentJsonCodec` (v3); verify a round-trip test and that a v2 payload imports undated
- [ ] 2.3 Carry the date in the TSV `Special` cell of plain tasks (`date:Y-M-D[,yearly]`); verify a round-trip test and that malformed date cells import undated
- [ ] 2.4 Update `docs/CODEC-MIGRATION.md` for v14; verify it lists the new fields

## 3. Formatting and settings

- [ ] 3.1 Add `ScribeDateOrder` and `ScribePlayerSettings.DateOrder` (default LanguageDefault) with normalization; verify the settings tests
- [ ] 3.2 Add the Core composer that picks the template key for an order and arranges day, month name and year; verify Core tests for all four orders
- [ ] 3.3 Add `scribe:date-format-dmy`, `-mdy` and `-ymd` to `en.json` and every locale file; verify the locale check in `build/verify.sh` passes

## 4. Mod adapter

- [ ] 4.1 Add `ScribeWorldCalendar` building the calendar shape and today's `ScribeDate` from `Calendar.TotalDays`, with years counted from 1; verify an Atlas scenario that its month and day match `Calendar.Month` and the day of month, and its year is 1 in a new world
- [ ] 4.2 Route `NotebookHost.FormatDate`, `FormatCalendarDate` and `FormatDateFromTimestamp` through the adapter and formatter; verify in an Atlas scenario that both give the same date for the current day (Year 1 in a new world)
- [ ] 4.3 Add a Date order row to Scribe Settings; verify changing it updates History and Guest Book dates

## 5. Verification

- [ ] 5.1 Run `build/verify.sh` with the client closed and verify Core and Atlas are green
- [ ] 5.2 Add playtest items to `TESTING.md`: an old save loads with tasks unchanged, and each Date order shows correctly in History, Guest Book and Assignment dates
