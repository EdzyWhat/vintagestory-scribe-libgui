## Context

See `proposal.md` for motivation. History leftover dates and Guestbook already live-format from
`Calendar.TotalDays` when a timestamp is present. Assignment dates are still `NotebookHost.FormatDate`
stamped in `ScribeModSystem.Assignment.cs` / `Delivery.cs` and stored as strings on
`ScribeAssignment` (eight fields), on the placed document block (`AssignedDate` only), and on
`ScribePinnedRef` (`AssignedDate` / `AcceptedDate`).

`BatchId` already replaced grouping-by-date-string. Pin dirty-check still compares those strings.
`DeriveLegacyBatchId(assigner, target, assignedDate)` only runs for pre-v3 store blobs.

`ScribeDocumentCodec` cannot insert a field *inside* the v9 assignment sub-blob: v11
`LinkDescription` and v12 `ExtraInfo` already follow it on the block. Append-only means the
assigned timestamp is a **trailing per-block field** (v13), applied onto `block.Assignment` when
present.

Constraints: `src/Core/` must not reference the VS API.

## Goals / Non-Goals

**Goals:**
- New assignment / transition / pin / document assigned dates follow `scribe:date-format` on the
  viewing client.
- Keep existing date strings as identity (pin dirty-check, legacy batch id, baked fallback).
- Three codec bumps with progressive reads of every currently accepted version.

**Non-Goals:**
- Shipping locale files.
- Re-keying pins or batches onto timestamps.
- Re-parsing baked date strings.
- Putting Accepted/Declined/… timestamps on the document block (those live only on the store;
  the document only ever persisted `AssignedDate`).

## Decisions

**Keep strings; add nullable timestamps; format at display.**
Same Guestbook rule: `double?`, `null` = missing (old blob), `0` = real world-day-one
`TotalDays`. Display: timestamp present → `FormatDateFromTimestamp`; else stored string. Pin
dirty-check keeps comparing `AssignedDate` / `AcceptedDate` strings so a Russian viewer does not
mark every pin dirty against an English server string.

**One Mod-side helper, stamp both at write.**
`AssignmentDisplay.Date(string baked, double? timestamp, IWorldAccessor world)`. Write path
keeps calling `FormatDate` for the identity string and passes `Calendar.TotalDays` into
`StampTransitionDate` / `TryMarkReceived` / constructors. Core tests that only supply strings
keep working (null timestamps → baked display).

**Store v9 appends eight optional timestamps.**
After the v8 redirect fields: for each of Assigned, Accepted, Declined, Cancelled, Discarded,
Completed, Received, Redirected — `bool has` + `double` when true. v8- reads leave them null.
AssignedDate remains a required string (it always was); its timestamp is still optional so a
v8 row that has the string but no timestamp displays baked.

**Document v13 appends after ExtraInfo, not inside the assignment blob.**
Per assigned-or-not block: `bool hasAssignedTimestamp` + optional `double`. If the block has an
assignment, copy onto `assignment.AssignedTimestamp`. Unassigned blocks write `false`. v12-
documents leave the timestamp null.

**Pin v9 appends assigned + accepted timestamps after ExtraInfo.**
`bool` + optional `double` twice. `ScribePinStore` copies timestamps from the source block's
assignment when snapshotting. Pre-v9 pins: strings only.

**Older client / newer blob.**
Assignment store, document, and pin readers reject version > current (fail-safe empty / false).
Same accepted tradeoff as History v4 and Guestbook v2; ships with the rest of 1.4.x.

## Risks / Trade-offs

- **[Three codecs in one change]** → more tests, but one display helper and one stamp path.
  Splitting store vs document vs pins would leave Task Notice hover English while Inbox is live.
- **[Document v13 shape looks unrelated to assignment]** → necessary append-only placement.
  Documented in the codec table and class comment.
- **[Server locale change mid-world]** → two identity strings for the same calendar day.
  Dedicated servers do not switch locale; accept (same as Guestbook).

## Migration Plan

1. Ship store v9 / document v13 / pin v9 with progressive reads of current windows.
2. New writes stamp timestamps. Existing blobs are untouched until next serialize; old rows stay
   `timestamp == null`.
3. Rollback is "don't install the new build." No down-migration.
4. Update `docs/CODEC-MIGRATION.md` for all three.

## Open Questions

None. Locale-file work stays a later change.
