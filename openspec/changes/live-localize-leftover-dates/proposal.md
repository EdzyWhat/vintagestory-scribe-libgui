## Why

Live History sentences already follow the viewing client, but Crafted / PickedUp / Manual History dates and Lectern Guestbook visit dates are still formatted on the server and stored as opaque strings. A Russian player on a typical English dedicated server therefore still sees English calendar dates on those rows. We are about to ship more languages; those leftover stamps have to resolve through `scribe:date-format` in the viewer's locale the same way live History already does.

## What Changes

- History rows that already carry a real `InGameTimestamp` (Crafted, PickedUp, Manual, and any other post-v3 row) rebuild their displayed calendar date from that timestamp on the viewing client. Pre-v3 migrated rows keep their baked `InGameDate` (their timestamps are synthetic negatives used only for sort order).
- Guestbook visits store a numeric in-game timestamp alongside the existing identity date string. The Guest Book column formats from that timestamp in the viewer's locale. Note edits, per-day dedup, and focus keys stay on `(PlayerName, InGameDate)` so “one visit per in-game day” does not depend on the displayed string.
- **Not breaking for saves:** `GuestbookStore` becomes `SGBK v2` with progressive reads of v1. Existing lecterns keep showing their baked visit dates until a new visit is recorded on that lectern; new visits are live-dated. History needs no codec bump.
- Explicitly out of scope: assignment / delivery dates (eight strings across three codecs — follow-up change); player-authored Manual / Guestbook note text; shipping locale files; rewriting already-baked v1 History or v1 Guestbook date strings; trimming English History pools.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `notebook-history`: Crafted, PickedUp, and Manual rows (and any other row with a real in-game timestamp) display their calendar date in the viewer's locale; pre-v3 baked dates stay as stored.
- `lectern-guestbook`: new visits persist a timestamp and display the date in the viewer's locale; identity, dedup, and note addressing stay `(PlayerName, InGameDate)`; `SGBK v2` reads v1 blobs with baked dates.

## Impact

- **Core:** `GuestbookEntry` gains an optional numeric timestamp; `GuestbookStore` `SGBK v2` with a v1–v2 accepted window (today it rejects any version ≠ 1). `TryAddEntry` still keys on the formatted date string the Mod layer supplies. Core stays free of `Lang` / VS API.
- **Mod:** `HistoryDisplay.Date` formats from timestamp whenever the timestamp is a real calendar day, not only for live-schema Death / PvP / boss / storm rows. Manual History rows use that helper. `RecordVisitor` / guestbook seeder write a timestamp. The Guestbook tab paints through `FormatDateFromTimestamp` when one is present. `ScribeEditGuestbookNoteMessage.InGameDate` is unchanged.
- **Tests:** Core v1-compat / v2 round-trip for Guestbook; History display tests that a non-negative timestamp live-formats and a negative migrated timestamp keeps `InGameDate`.
- **Docs:** add `GuestbookStore` to `docs/CODEC-MIGRATION.md`. CHANGELOG Unreleased.
