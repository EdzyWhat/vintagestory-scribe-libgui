## Why

History and Guestbook dates now follow the viewing client, but assignment dates (`AssignedDate`, `AcceptedDate`, and the other transition stamps) are still formatted on the server and stored as opaque strings. A Russian player on a typical English dedicated server therefore still sees English “Assigned by … — {date}” lines in Inbox, Pin Tab, Task Notice hover, and item cards. We are about to ship translations; those leftover stamps have to resolve through `scribe:date-format` the same way History and Guestbook already do.

## What Changes

- New assignment writes persist a numeric in-game timestamp next to each existing date string. Inbox, Sent history, Task Notice, pin-tab tooltips, and item-hover cards format from that timestamp in the viewing player's locale.
- Pre-this-change records keep their baked date strings (no timestamp to rebuild from). Pin HUD sync still compares the stored identity strings, not the displayed locale form.
- **Not breaking for saves:** assignment store `v8 → v9`, document codec `v12 → v13` (assigned timestamp appended after ExtraInfo — cannot insert inside the assignment blob, because v11/v12 already follow it), pin codec `v8 → v9`. Progressive reads of every previously accepted version. Older clients cannot read the new blobs (empty Inbox / pins / document until they update) — same class of bump as `SHST` v4 and `SGBK` v2.
- Explicitly out of scope: shipping locale files; player-authored task/note text; re-parsing already-baked assignment date strings; changing `BatchId` / `DeriveLegacyBatchId`.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `inbox-tab`: Inbox / Sent assignment dates with a timestamp render in the viewer's locale; v8 store rows keep baked strings.
- `task-note-document`: an assigned block persists an assigned timestamp alongside the identity date string; editor/read “Assigned by” dates follow the viewer when the timestamp is present.
- `player-pins`: accepted-assignment pin snapshots include assigned/accepted timestamps; Pin Tab / HUD dates follow the viewer.
- `scribe-item-hover-summary`: sealed Task Notice hover “Assigned by” date follows the viewer when a timestamp is present.

## Impact

- **Core:** `ScribeAssignment` and `ScribePinnedRef` gain nullable timestamp fields; `ScribeAssignmentStore` v9, `ScribeDocumentCodec` v13, `ScribePinCodec` v9. `Clone()` copies timestamps. Dedup / `BatchId` / pin dirty-check stay on the existing strings. Core stays free of `Lang` / VS API.
- **Mod:** write path (`FormatDate` + `Calendar.TotalDays`) stamps both. `StampTransitionDate` / `TryMarkReceived` take a timestamp. Display goes through one helper (`timestamp` present → `FormatDateFromTimestamp`, else baked string). `ScribePinStore` copies timestamps onto pins.
- **Tests:** Core v8/v12/v8-pin compat blobs load with null timestamps; new blobs round-trip `0` and real values. Existing assignment tests that only pass baked strings still pass.
- **Docs:** `docs/CODEC-MIGRATION.md` rows for the three codecs. CHANGELOG Unreleased.
