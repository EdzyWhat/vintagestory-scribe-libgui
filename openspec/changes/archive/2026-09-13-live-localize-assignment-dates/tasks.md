## 1. Core assignment model + store codec

- [x] 1.1 Add eight `double?` timestamp fields on `ScribeAssignment` (`AssignedTimestamp`, `AcceptedTimestamp`, `DeclinedTimestamp`, `CancelledTimestamp`, `DiscardedTimestamp`, `CompletedTimestamp`, `ReceivedTimestamp`, `RedirectedTimestamp`; default null), copy them in `Clone()`, and verify existing `ScribeAssignmentTests` still pass and a Clone of an assignment with `AssignedTimestamp = 0` keeps `0` (not null)
- [x] 1.2 Add optional timestamp arguments to `ScribeAssignmentStore.TryCreate` / `TryCreateSent` / `TryCreateAccepted` (after `batchId`, so existing positional callers keep compiling), `TryMarkReceived`, and `TryRedirectTarget`; stamp the matching `*Timestamp` fields; and verify a unit test that a create with timestamp `0` plus a received/redirect stamp round-trips in memory while a caller that omits the new args still leaves those timestamps null
- [x] 1.3 Bump `ScribeAssignmentStore` to v9 with progressive reads of v1–v9: after the v8 redirect strings, eight `bool has` + optional `double` fields (Guestbook sentinel rule: missing bool = null, `0` is a real day), and verify a hand-built v8 blob loads with all timestamps null, a v9 blob round-trips a mix of null and real values including `0`, and a v10 blob fail-safes empty

## 2. Document + pin codecs

- [x] 2.1 Bump `ScribeDocumentCodec` to v13 and append, after ExtraInfo (never inside the v9 assignment sub-blob), per-block `bool hasAssignedTimestamp` + optional `double`; apply onto `block.Assignment.AssignedTimestamp` when the block has an assignment; write `false` for unassigned blocks; and verify a hand-built v12 blob loads with `AssignedTimestamp == null`, a v13 assigned block round-trips `0` and a real day, an unassigned v13 block still has no assignment, and a v14 blob fail-safes false
- [x] 2.2 Add `AssignedTimestamp` / `AcceptedTimestamp` (`double?`, default null) on `ScribePinnedRef`, bump `ScribePinCodec` to v9 appending those two optional timestamps after ExtraInfo, and verify a hand-built v8 pin list loads with both timestamps null, a v9 pin round-trips a mix of null and `0`, and a v10 blob fail-safes false

## 3. Write path + pin snapshot

- [x] 3.1 Extend `StampTransitionDate` to take the in-game timestamp alongside the identity string, pass `FormatDate` + `Calendar.TotalDays` from `ScribeModSystem.Assignment.cs` and `Delivery.cs` into create / stamp / `TryMarkReceived` / `TryRedirectTarget`, and verify a newly sent, received, accepted, and redirected assignment persists non-null timestamps on the store record while `AssignedDate` / transition date strings remain the identity stamps
- [x] 3.2 Copy assigned/accepted timestamps onto the pin when snapshotting (`ScribePinStore` / `ScribeModSystem.PinOperations`) and when refreshing a pin whose source block already matches on identity strings (so a pre-v9 pin picks up timestamps without locale affecting dirty-check); keep HUD dirty-check on `AssignedDate` / `AcceptedDate` strings; and verify a pin of an accepted assignment with timestamps stores those timestamps and a string-only identity mismatch still marks the pin dirty

## 4. Display

- [x] 4.1 Add a Mod-side `AssignmentDisplay.Date(string baked, double? timestamp, IWorldAccessor world)` that returns `FormatDateFromTimestamp` when the timestamp is present and the baked string otherwise, and verify (by inspection plus the Core tests that only supply strings) that a null timestamp still displays the identity string
- [x] 4.2 Wire Inbox / Sent (`ScribeDialogBase.ViewSwitching` → `ScribeInboxContent`), editor / read “Assigned by” (`ResolveAssignmentTooltipInfo`, `ScribeEditorContent` / `ScribeReadContent` / `ScribeDocumentSlot`), Task Notice (`GuiDialogTaskNotice` + hover `AppendTaskNoticeLines`), and Pin Tab / HUD (`ScribeDialogBase.PinTab` / `ScribePinnedContent` / `ScribeRowWidgets` assignment-marker tooltip) through `AssignmentDisplay.Date`, and verify each of those call sites no longer passes a raw `AssignedDate` / transition date when a timestamp is present

## 5. Docs + changelog

- [x] 5.1 Add `ScribeAssignmentStore` v8→v9, `ScribeDocumentCodec` v12→v13, and `ScribePinCodec` v8→v9 rows to `docs/CODEC-MIGRATION.md` (document timestamp after ExtraInfo, not inside the assignment blob), update the three codec class doc-comment accepted-version tables, and verify they match the code
- [x] 5.2 Add a CHANGELOG entry under Unreleased describing live assignment / pin / Task Notice dates (pre-timestamp records keep baked strings), and verify `dotnet test` for `tests/Core.Tests` is green
