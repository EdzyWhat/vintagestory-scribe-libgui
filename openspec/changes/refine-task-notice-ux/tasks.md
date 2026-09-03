## 1. Core: Sent/Received state machine

- [x] 1.1 Add `ScribeAssignmentState.Sent` to the enum (`ScribeAssignment.cs`); keep it OUT of
      `ScribeAssignmentTransitions.CanApply`'s matrix (store-only, like `Accepted`-via-notice
      already is).
- [x] 1.2 Add `ScribeAssignment.ReceivedDate` (nullable string), mirroring
      `AcceptedDate`/`CompletedDate`'s pattern; include it in `Clone()`.
- [x] 1.3 Add `ScribeAssignmentStore.TryCreateSent(...)`, parallel to `TryCreateAccepted`, creating
      a record in the `Sent` state.
- [x] 1.4 Add `ScribeAssignmentStore.TryMarkReceived(assignmentId, receivedDate)` transitioning
      `Sent -> Unaccepted` and stamping `ReceivedDate`; false for any other state or unknown id.
- [x] 1.5 Filter `ScribeAssignmentStore.Received(playerUid)` to exclude `State == Sent`.
- [x] 1.6 Core unit tests: `TryCreateSent` creates the record correctly; `Received()` excludes
      `Sent`-state rows but `Sent()` includes them; `TryMarkReceived` transitions correctly and
      rejects illegal states/unknown ids; `Clone()` round-trips `ReceivedDate`.

## 2. Mod: wire Sent-at-creation and Received-on-inventory-entry

- [x] 2.1 `SendBatchViaNotice`: after sealing the notice's document, also call `TryCreateSent` once
      per row (same `assignmentId` as the embedded block), then push the Assigner's Sent History.
- [x] 2.2 Extend the existing proximity-signal heartbeat tick: for each of a player's `Sent`-state
      records, check whether a sealed notice carrying that id is now in their own inventory
      (reuse `ScribeAcceptCandidates`'-style inventory scan); on a match call `TryMarkReceived` and
      push that player's Inbox.
- [x] 2.3 `OnServerReceivedTaskNoticeAction`: Accept now calls the existing `TryApplyAction`
      transition (record already exists from receipt) instead of `TryCreateAccepted`; Decline now
      calls `TryApplyAction` to Declined instead of being a pure item-consume no-op.
- [x] 2.4 `ScribeInboxContent`: add a "Sent"/"Received" chip + meta-line rendering for the new
      state and `ReceivedDate`, following the existing per-state chip/meta-line switch.
- [x] 2.5 Core + Atlas tests covering the full send -> receive -> accept/decline path end to end
      (notice sent, appears in Sent History as Sent, Inbox silent until a synthetic inventory
      move, then Received, then Accept/Decline transitions as normal).

## 3. Accept dialog: custom chrome, parchment backing, button layout

- [x] 3.1 Add the parchment/scroll pixel-art backdrop asset under
      `src/Mod/assets/scribe/textures/gui/` and a `ScribeBackdropSpec` for it. Compute the art
      box's `W`/`H` inline in `GuiDialogTaskNotice` (no host, so no `ScribeLayout`/`GetLayout`
      call): `AspectH` fixed at the PNG's own ratio (130/105 ≈ 1.238), `W` derived from
      `modSystem.MySettings.PixelArtSize` scaled by 2/3 (a first-pass tuning constant — see design
      Open Questions). Wire the bitmap via `ScribePixelArtBackdrop`, mirroring
      `ScribeDialogBase.Layout.WrapBackdrop`'s pattern (missing-asset flat-color fallback
      included).
- [x] 3.2 Replace the stock `WindowFrame` with a bespoke title bar (title text + close button)
      matching `ScribeDialogBase.BuildTitleBar`'s visual style, implemented as new code local to
      this file (not shared with `ScribeDialogBase`). Set `WindowConfig.DragHandleHeight` to the
      title band's height so `GuiBase`'s own band-drag covers it — no grip-drag reimplementation
      needed (this title bar has no grip tooltip to swallow the click).
- [x] 3.3 Build a 3-column inset frame (side margins sized proportionally to the art width, center
      column holding the existing header-text/divider/body/divider/action-row `Column`) matching
      `BuildSectionInnerBox`'s proportions, so content sits inset from the parchment art's border
      instead of touching its edge.
- [x] 3.4 Restructure `BuildActionRow`/`BuildAcceptControl` so the multi-candidate picker renders
      as its own full-width row ABOVE the Decline/Accept row, not stacked inside one of that row's
      cells; confirm Decline/Accept both size to their text.
- [x] 3.5 Give `ScribeRowWidgets.BuildTaskCheckbox` a muted/disabled `CheckboxStyle` when
      `onChanged is null`, so the notice's inert rows read as visibly non-interactive.

## 4. Manual verification

- [ ] 4.1 Manual playtest: send a notice, confirm it appears in Sent History immediately as
      "Sent"; confirm the Assignee's Inbox shows nothing until the notice is actually in their
      inventory, then shows "Received."
- [ ] 4.2 Manual playtest: Accept and Decline a received notice each behave exactly like an
      in-range assignment's Accept/Decline (same Sent History end-state, same no-active-notification
      behavior for Decline).
- [ ] 4.3 Manual playtest: open the Accept dialog and confirm the custom title bar drags/closes
      correctly, the parchment backing renders at the right aspect ratio, the dialog reads
      noticeably smaller than a full Notebook/Lectern page (tune the 2/3 scale factor if it
      doesn't), the checkboxes look visibly disabled, and — with 2+ eligible carried destination
      items — the picker sits above the buttons with neither button clipped.
