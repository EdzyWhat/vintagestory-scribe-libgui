## Context

See `proposal.md` for motivation. Live-schema History already rebuilds Death / PvP / boss / storm
dates from `InGameTimestamp` in `HistoryDisplay.Date`. Crafted / PickedUp / Manual (and v3 baked
system rows) still paint `InGameDate`, which was formatted with `Lang.Get` on the server.
`SHST` v1/v2 rows received synthetic **negative** timestamps in `ApplyV2ToV3Migrations` so they
sort before every real `Calendar.TotalDays` value — those numbers are not calendar days.

Guestbook is `SGBK` v1 (`playerName`, `inGameDate`, `note`) with a strict `version != 1` reject.
`(PlayerName, InGameDate)` is the natural key for dedup, note edits (`ScribeEditGuestbookNoteMessage`),
and LibGUI focus (`GuestbookNoteKey`). There is no timestamp field today.

Constraints: `src/Core/` must not reference the VS API; assignment / pin / document date strings
are a later change.

## Goals / Non-Goals

**Goals:**
- History dates with a real timestamp follow `scribe:date-format` on the viewing client.
- New Guestbook visits store a timestamp and display that way; v1 visits keep their baked date.
- Leave Guestbook identity, caps, and note addressing on the stored date string.

**Non-Goals:**
- Assignment / delivery dates (store v8, document v12, pin snapshots).
- Re-keying Guestbook onto a locale-independent day number.
- Re-parsing v1 History or v1 Guestbook date strings into timestamps.
- Stopping the server from still writing `InGameDate` as a fallback / identity string.

## Decisions

**History: live-format when `InGameTimestamp >= 0`, else baked `InGameDate`.**
v3+ writes already store `Calendar.TotalDays` (including `0` on day one of a world). v1/v2
migrations store negatives. `FormatDateFromTimestamp` already clamps at 0, so feeding it a
synthetic negative would collapse every migrated row onto Year 1, Day 1 — that is why the
threshold is required. Alternative: only live-format `HistorySchema.Live` — rejected, because
Crafted / PickedUp / Manual and v3 baked Deaths already have real timestamps and would stay
English. Alternative: schema-byte for dates — rejected as a History codec bump we do not need.

`HistoryDisplay.Date` becomes that one rule. Manual rows currently pass `entry.InGameDate` into
`ScribeManualHistoryRow`; they use the same helper. Kind labels and Manual body stay as they are.
New History writes may keep stamping `InGameDate` (server locale) as a leftover fallback; display
ignores it when the timestamp is real.

**Guestbook: add nullable timestamp; keep `InGameDate` as identity.**
`GuestbookEntry.InGameTimestamp` is `double?` (`null` = v1 / unknown). `TryAddEntry` still
dedups on `(PlayerName, InGameDate)` and gains an optional timestamp argument the Mod layer fills
from `Calendar.TotalDays`. Display: timestamp present → `FormatDateFromTimestamp`; else stored
string. Note edits and `GuestbookNoteKey` keep using the stored identity string, so a Russian
viewer does not send a translated date the server cannot match.

Alternative: key on `floor(TotalDays)` and drop the formatted identity — cleaner for i18n, but
it changes the edit wire (`ScribeEditGuestbookNoteMessage.InGameDate`), the focus key, and every
existing Core test. Out of scope; identity stays opaque.

The identity string is still produced with `NotebookHost.FormatDate` on the server. That call is
locale-dependent, but it is no longer what the column shows. One dedicated-server locale per world
keeps “one visit per in-game day” stable.

**`SGBK v2` appends an optional timestamp; progressive reads of v1.**
Per-entry layout after the existing v1 fields: `bool hasTimestamp`, then `double inGameTimestamp`
only when true (same optional-field pattern as assignment dates). v1 reads leave
`InGameTimestamp` null. A mixed lectern (v1 rows plus a new visit) MUST round-trip those nulls
when rewritten as v2 — writing a bare `double` would collide with real `TotalDays == 0` on day
one of a world. Today `Deserialize` returns empty on `version != 1`; that MUST become an
accepted window `[1, 2]` (same lesson as `SPHS` v1 → v2). A v3+ or v0 blob fail-safes empty.

`SeedGuestbook` / `/scribe seed` pass `CalendarTotalDaysAgo` so seeded visits live-format too.

## Risks / Trade-offs

- **[Older client opens a v2 lectern]** → current code rejects version ≠ 1 and shows an empty
  Guest Book until the player updates. Same class of bump as `SHST` v4. Mitigation: ships with
  the rest of 1.4.x.
- **[v3 History Death date changes language]** → baked English *sentence* stays; the date
  follows the viewer. Intentional. Pre-v3 dates stay baked.
- **[Server locale change mid-world]** → same player, same calendar day, two identity strings
  if `FormatDate` output changes. Dedicated servers do not switch locale; accept.
- **[Timestamp `0` vs missing]** → `0` is a real TotalDays; only `null` means v1. Do not use
  `0` as a sentinel.

## Migration Plan

1. Ship `SGBK v2` with progressive reads of v1. History codec unchanged.
2. New Guestbook visits write a timestamp. Existing lectern attributes are untouched until the
   next visit rewrites the blob; old rows stay `InGameTimestamp == null`.
3. History display starts live-formatting any row with `InGameTimestamp >= 0` immediately
   (including existing v3 notebooks). No blob rewrite.
4. Rollback is "don't install the new build": v1 Guestbook readers will not parse v2 blobs
   (empty Guest Book on that lectern). No down-migration is provided.
5. Add a `GuestbookStore` row to `docs/CODEC-MIGRATION.md`.

## Open Questions

None. Assignment dates stay a follow-up change.
