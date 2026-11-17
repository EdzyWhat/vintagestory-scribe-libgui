## Context

The row menu's date group is built in `ScribeDialogBase.RowMenuItems` from the row's document block
(`RowMenuBlock`), which is null on the Pin Tab, and `canDate` excludes the Pin Tab. `ScribeRowMenuEntries.For`
returns only Unpin and Delete for the Pin Tab. `ApplyRowDate` applies a date to the dialog's own document
and sends `ScribeSetTaskDateMessage` (`DocId`, `TaskId`, operation, date). The server's
`SetTaskDateForPlayer` resolves the document by id, refuses while another player holds the editor lock or
when the document is read-only, requires the block to be datable, and then refreshes every holder's pin
date (`RefreshPinDates`). A pin (`ScribePinnedRef`) carries `OwnerDocId`, `TaskId`, `Kind`, `Orphaned` and
`Date`, so the Pin Tab has what it needs to build the message without the source document.

## Goals / Non-Goals

**Goals:**
- Add, change and clear a pinned task's date from the Pin Tab with the same picker and rules as a Notebook.
- Reuse the existing message, server rules and refresh push.

**Non-Goals:**
- Repeat yearly on the Pin Tab. The pin has no repeat flag.
- Dating from the HUD, or any change to the date message, save format or lang keys.
- Dates on assigned tasks (roadmap Later).

## Decisions

- **Eligibility is a pin-side predicate.** `ScribeDateRules.PinCanDate(pin)` is true for a Task pin that is
  not orphaned. It has no date clause (an undated pin can be dated) and no assignment clause (a date cannot
  be set on an assignment block, and the server re-checks). The dialog also requires `SupportsRowDates`.
  The server is the authority: `TrySetTaskDate` refuses a block that is not a datable Task, so a stale
  snapshot (the task was since turned into something else, or deleted) changes nothing.
- **`ScribeRowMenuEntries.For` offers the date group on the Pin Tab when `canDate`.** The order is Unpin,
  date group, Delete, and the existing rule puts a separator before Delete only. The group has Add date, or
  Change date and Clear date. It has no Repeat yearly, so the Pin Tab passes a flag that omits it
  (`allowRepeat: false`) and the existing read and editor calls are unchanged.
- **The pin's chip follows the server.** The Pin Tab does not edit the pin optimistically. The server
  applies the date and `RefreshPinDates` pushes the new snapshot to every holder, which rebuilds the row.
  An optimistic pin edit would need a client-side snapshot mutation and a rollback on refusal, for a delay
  of one network round trip.
- **The client sends by the pin's identity.** `ApplyRowDate` gains a Pin Tab path that sends the message
  with `pin.OwnerDocId` and does not touch the dialog's own document or read view. It does not call
  `RefuseWhileLockedByOther`, which checks the dialog's own host, not the pin's document. The server shows
  the locked error.
- **Picker start.** `OpenDatePicker` takes the start date from the pin's `Date`, or today.
- **The picker closes with the Pin Tab.** The existing view-change close covers leaving the Pin Tab. A pin
  that is removed or orphaned while the picker is open still sends on Set, and the server ignores it.

## Risks / Trade-offs

- A server refusal (lock, read-only, unresolvable) gives no visible change on success paths other than the
  locked message. Accepted, as in the Notebook's read view.
- A pin whose source is another player's unloaded Lectern can still be dated if the server can resolve the
  document by id. Unresolvable documents are ignored silently. Accepted.
- Round-trip delay before the chip changes. Accepted; no optimistic edit.

## Assumptions (user to confirm)

1. No Repeat yearly on the Pin Tab. The pin has no flag.
2. The date group has no divider beyond the existing one before Delete.
3. The chip updates only when the server's push arrives.
4. Any pin of a Task can be dated, including one whose document belongs to another player's block, as
   long as the server resolves it and no one else holds the editor lock.
5. No new lang keys.
