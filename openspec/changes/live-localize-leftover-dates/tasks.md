## 1. Core Guestbook model + codec

- [ ] 1.1 Add `double? InGameTimestamp` (default null) to `GuestbookEntry`, add an optional timestamp argument to `GuestbookStore.TryAddEntry` that does not affect `(PlayerName, InGameDate)` dedup, and verify existing `GuestbookStoreTests` still pass and a new add with a timestamp (including `0`) round-trips in memory with `InGameTimestamp` set
- [ ] 1.2 Bump `GuestbookStore` to `SGBK v2` with progressive reads of v1–v2 (`bool hasTimestamp` + optional `double` after the v1 fields), and verify a unit test that a hand-built v1 blob loads with `InGameTimestamp == null`, a v2 blob round-trips a mix of null and real timestamps (including `0`), and a v3 blob fail-safes empty

## 2. History leftover dates

- [ ] 2.1 Change `HistoryDisplay.Date` to format from `InGameTimestamp` via `NotebookHost.FormatDateFromTimestamp` whenever the timestamp is `>= 0`, otherwise return stored `InGameDate`, and verify it no longer gates on `IsLiveSystemRow` (Crafted / PickedUp / Manual / baked v3 rows with a real timestamp live-format; a negative synthetic timestamp keeps `InGameDate`)
- [ ] 2.2 Wire `ScribeManualHistoryRow` to take `HistoryDisplay.Date(entry, world)` instead of `entry.InGameDate`, leave Manual body and kind labels unchanged, and verify the Manual draft preview still uses the client `FormatDate` path

## 3. Guestbook write + display

- [ ] 3.1 Pass `Calendar.TotalDays` from `RecordVisitor` into `TryAddEntry`, extend `SeedGuestbook` / `/scribe seed` to supply `CalendarTotalDaysAgo` alongside the identity date string, and verify a newly recorded visit and a seeded visit persist a non-null timestamp while still deduping on the identity date string
- [ ] 3.2 Paint Guestbook Date of visit from `FormatDateFromTimestamp` when `InGameTimestamp` is present, else the stored identity string; keep `GuestbookNoteKey` and `ScribeEditGuestbookNoteMessage.InGameDate` on the stored identity string, and verify a note edit on a timestamped row still addresses that row by `InGameDate`

## 4. Docs + changelog

- [ ] 4.1 Add a `GuestbookStore` row to `docs/CODEC-MIGRATION.md` (`SGBK v2`, v1–v2 progressive reads) and update the store's class doc-comment accepted-version table, and verify they match the code
- [ ] 4.2 Add a CHANGELOG entry under Unreleased describing live History leftover dates and live Guestbook visit dates (v1 guestbook dates unchanged; assignment dates still baked), and verify `dotnet test` for `tests/Core.Tests` is green
