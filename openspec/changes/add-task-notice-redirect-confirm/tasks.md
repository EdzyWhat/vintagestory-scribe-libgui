## 1. Core: redirect data + transition

- [x] 1.1 Add `RedirectedFromUid` (string?) and `RedirectedDate` (string?) to `ScribeAssignment`
  (`src/Core/ScribeAssignment.cs`), copied by `Clone()`, and verify a new/updated Core unit test
  confirms both default to null and survive `Clone()`.
- [x] 1.2 Add `ScribeAssignmentStore.TryRedirectTarget(Guid assignmentId, string
  newTargetPlayerUid, string redirectedDate)` (design D1): requires `State ==
  ScribeAssignmentState.Unaccepted`, no-ops (returns false) if `newTargetPlayerUid` already equals
  the current `TargetPlayerUid`, otherwise stamps `RedirectedFromUid` from the current
  `TargetPlayerUid`, overwrites `TargetPlayerUid`, stamps `RedirectedDate`, and returns true.
  Verify with Core unit tests covering: successful redirect, no-op on matching uid, false on a
  non-Unaccepted record, false on an unknown id.
- [x] 1.3 Verify a subsequent `TryApplyAction(id, newTargetPlayerUid, Accept)` legally succeeds
  immediately after a `TryRedirectTarget` call in the same test, proving the existing transition
  matrix needs no changes (design D1/Goals).
- [x] 1.4 Bump the store codec from v7 to v8 (`ScribeAssignmentStore.cs`): append
  `WriteOptionalString` calls for `RedirectedFromUid` and `RedirectedDate` in `WriteRecordList`,
  read them (default null pre-v8) in `TryReadRecordList` via the existing `ReadOptionalString(r,
  version, minVersion: 8)` pattern, and add a `// v8 adds ...` comment line matching the existing
  version-history block. Verify with a round-trip Core unit test: serialize a redirected record,
  deserialize it, assert both fields survive; and a second test deserializing a hand-built v7 blob
  (no redirect bytes) still reads back with both fields null.

## 2. Mod: split the Accept/Decline identity gate

- [x] 2.1 In `ApplyTaskNoticeAction` (`src/Mod/ScribeModSystem.Delivery.cs`), move the existing
  recipient-identity check (lines ~167-173) so it only gates the Decline branch, no longer
  rejecting Accept. Verify by confirming (existing or new test) a non-recipient's Decline is
  still silently ignored exactly as today.
- [x] 2.2 For the Accept branch, before the existing `TryMarkReceived`/`TryApplyAction` loop, call
  `assignmentStore.TryRedirectTarget(block.TaskId, fromPlayer.PlayerUID, date)` for any block
  whose current `Assignment.TargetPlayerUid != fromPlayer.PlayerUID` (design D2). Verify with an
  integration test: a non-recipient Accept on a notice addressed to someone else succeeds, the
  resulting record's `TargetPlayerUid` is the accepting player, and `RedirectedFromUid` matches
  the original recipient.
- [x] 2.3 Verify the existing recipient-matches-already path (today's normal Accept) is unchanged
  — an integration test asserting `RedirectedFromUid` stays null and behavior is identical to
  before this change.

## 3. Client: confirm popup and Accept wiring

- [x] 3.1 Create `GuiDialogTaskNoticeRedirectConfirm` (new file, sibling to
  `GuiDialogTaskNotice.cs`) per design D4: `WindowFrame`-hosted, fixed-size, draggable, centered,
  `DrawOrder => 0.2`, constructed with the resolved recipient name and an `onConfirm` callback;
  body is the warning text plus Confirm/Cancel buttons; Cancel and the window's own close both
  just `TryClose()` with no callback invoked. Verify by building the project
  (`dotnet build`) with no compile errors — this class has no Core-testable logic.
- [x] 3.2 Add new lang keys for the popup's title/body/buttons (e.g.
  `scribe-tasknotice-redirect-confirm-title`, `-body` with a `{0}` recipient-name placeholder,
  `-confirm-button`, `-cancel-button`) to `src/Mod/assets/scribe/lang/en.json`, matching the
  existing key-naming convention. Verify the keys resolve (no missing-lang-key warning) when the
  dialog opens in a manual test.
- [x] 3.3 In `GuiDialogTaskNotice.AcceptOnto` (`GuiDialogTaskNotice.cs:353-356`), check whether
  `document.Blocks[0].Assignment?.TargetPlayerUid` differs from `capi.World.Player.PlayerUID`
  (design D3); if so, open `GuiDialogTaskNoticeRedirectConfirm` (resolving the recipient's name
  via the existing `ResolvePlayerName` helper) with `onConfirm` performing the original
  `SendTaskNoticeAction` + `TryClose()` call, instead of sending immediately. Verify manually:
  holding a notice addressed to another player and tapping Accept shows the popup instead of
  immediately sending.
- [ ] 3.4 Verify manually that Cancel on the popup leaves the notice sealed and unconsumed (no
  packet sent, notice dialog still open), and that Confirm proceeds through Accept exactly as the
  recipient path does (destination placement, item consumed).

## 4. Sent Assignment History trace

- [x] 4.1 Add `RedirectedFromUid`/`RedirectedDate` to `ScribeInboxRowData` and populate them from
  the assignment record in `ComputeSentAssignmentRows`/`ComputeReceivedAssignmentRows`
  (`src/Mod/ScribeDialogBase.ViewSwitching.cs`).
- [x] 4.2 Add a new lang key `scribe-assignment-redirected-on` (original-recipient name,
  new-recipient name, date) and a corresponding `metaLines.Add(...)` branch in
  `BuildExpandedDetail` (`src/Mod/ScribeInboxContent.cs:421-449`), following the existing
  `if (data.XDate is { } x)` shape, resolving both UIDs via the existing `ResolvePlayerName`
  helper (design D5). Verify manually: after a redirected Accept, the Assigner's expanded Sent
  Assignment History row shows the state as Accepted plus the new detail line naming both
  players.
- [ ] 4.3 Verify manually that the original recipient's Inbox shows no record at all for the
  redirected notice (spec: silent disappearance), and that the redirected-to player's own Inbox
  view of the same record also shows the new detail line (design D5's accepted side effect).

## 5. Full-flow verification

- [x] 5.1 Run the Core test suite (`dotnet test tests/Core.Tests`) and confirm all new and
  existing assignment tests pass.
- [ ] 5.2 Manual in-game pass covering: recipient Accept (unchanged), recipient Decline
  (unchanged), non-recipient Decline (still silently ignored), non-recipient Accept with Cancel
  (no-op), non-recipient Accept with Confirm (redirect succeeds, history trace visible, original
  recipient's Inbox stays silent).
