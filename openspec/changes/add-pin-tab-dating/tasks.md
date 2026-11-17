## 1. Core

- [x] 1.1 Add `ScribeDateRules.PinCanDate(ScribePinnedRef)` (Kind is Task and not orphaned), with unit tests for a note, an orphaned pin, a dated and an undated task pin.
- [x] 1.2 Let `ScribeRowMenuEntries.For` offer the date group on the Pin Tab when `canDate`, between Unpin and Delete, with an `allowRepeat` flag (default true) that omits Repeat yearly; add matrix tests for the Pin Tab with and without a date, with `canDate` false, and with `allowRepeat` false.

## 2. Mod

- [x] 2.1 In `ScribeDialogBase.RowMenu.cs`, build the Pin Tab's `canDate` and `hasDate` from the pin snapshot (`PinCanDate`, `SupportsRowDates`), pass `allowRepeat: false`, and build the date items with no Repeat yearly.
- [x] 2.2 Route the Pin Tab's AddDate, ChangeDate and ClearDate through `RunPinRowAction`: open the picker at the pin's date or today, and send `ScribeSetTaskDateMessage` with `pin.OwnerDocId` and the pin's `TaskId`, without touching the dialog's own document.
- [x] 2.3 Confirm the picker closes when the dialog leaves the Pin Tab and when the pin disappears.

## 3. Tests and verification

- [x] 3.1 Atlas scenario: a second player pins a task; the first player sets, changes and clears its date by `(DocId, TaskId)` through `SetTaskDateForPlayer`; the pin's snapshot changes for the holder each time, and a lock held by another player refuses the change.
- [ ] 3.2 Run `./build/verify.sh --no-restage`; add `TESTING.md` items: date a pin from the Pin Tab (add, change, clear), no Repeat yearly on the Pin Tab, no date entries on an orphaned pin, a note pin and the Tablet's and Assignment Desk's Pin Tab, the locked-by-another-player message.
