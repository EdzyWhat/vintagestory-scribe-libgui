## 1. Core: terminal-state helper + store deletion

- [x] 1.1 Add a static/extension helper (e.g. `ScribeAssignmentState.IsTerminal(this ScribeAssignmentState state)`)
      in `src/Core/` returning true for Declined, Cancelled, Discarded, and Completed.
- [x] 1.2 Add `ScribeAssignmentStore.TryDelete(Guid assignmentId, string actingPlayerUid)`: removes
      the record from `_records` only when `actingPlayerUid` matches the record's Assigner or
      Assignee UID AND `IsTerminal(record.State)` is true; returns false (no-op) otherwise.
- [x] 1.3 `Core.Tests`: cover `TryDelete` — Assignee deletes a terminal record (succeeds), Assigner
      deletes a terminal record (succeeds), delete attempt on Unaccepted/Accepted (rejected,
      record unchanged), delete attempt by an uninvolved player (rejected, record unchanged).
- [x] 1.4 `Core.Tests`: cover `IsTerminal` for all six states.

## 2. Network: delete message + server handler

- [x] 2.1 Add `ScribeDeleteAssignmentMessage { AssignmentId }` in `src/Mod/`, mirroring
      `ScribeDeleteHistoryEntryMessage.cs`.
- [x] 2.2 Register the new message type on the network channel alongside the existing assignment
      messages (wherever `ScribeAssignmentActionMessage` etc. are registered).
- [x] 2.3 Add a server-side handler in `ScribeModSystem.Assignment.cs`, alongside the existing
      `TryApplyAction` handling: resolve the sending player's UID, call `TryDelete`, and on success
      push the resynced `Sent`/`Received` lists to affected clients the same way other assignment
      mutations already do. On failure (no-op), do nothing further.

## 3. Mod UI: delete control on terminal rows

- [x] 3.1 In `ScribeInboxContent.cs`'s `BuildExpandedDetail`, for rows whose `data.State` is
      terminal, append one action row below the existing metaLines containing a single
      `ActionButton`-styled delete control (`ScribeRowButton`, icon `"scribeclose"`,
      `iconColor: colors.Error`), right-aligned like the existing Accept/Decline action row.
      Implemented as a labeled `ActionButton` (Danger variant), not a bare icon button — matches
      the visible-text convention every sibling Accept/Decline/Cancel/Discard control already uses,
      rather than relying on a hover-only tooltip.
- [x] 3.2 Wire the delete control's `onTap` to send `ScribeDeleteAssignmentMessage` for that row's
      assignment id (no client-side optimistic removal — wait for the resynced list, matching
      `DeleteManualEntry`'s existing pattern).
- [x] 3.3 Confirm non-terminal rows (Unaccepted, Accepted) never render this control. (By
      construction: the check is `data.State.IsTerminal()`, independent of the ViewerRole/State
      branches that populate Unaccepted/Accepted actions — code-reviewed, not yet manually played;
      see 6.3.)
- [x] 3.4 Confirm the control renders identically in both the Inbox view and the Sent History view
      (both render through the same `ScribeInboxContent.cs` path). (Both `BuildInboxContent` and
      `BuildSentAssignmentHistoryContent` now pass the same `onDelete` callback into the same
      `ScribeInboxContent`/`ScribeInboxRow` — code-reviewed; see 6.2 for the in-game check.)

## 4. Mod UI: lift expand/collapse state, add title-bar toggle

- [x] 4.1 Add `HashSet<Guid> expandedIds` to `ScribeInboxContentState` (or equivalent owning
      state object). Replace each row's private `expanded` bool with a read from
      `expandedIds.Contains(id)` and a callback that adds/removes that row's id from the shared
      set on chevron tap, instead of flipping a local field.
      **Deviation from plan:** the set (`expandedAssignmentIds`) and the filter-chip selection
      (`assignmentFilterGroup`, previously private to `ScribeInboxContentState`) both had to move up
      onto `ScribeDialogBase` itself, not `ScribeInboxContentState` — the title bar is built by a
      different method/build pass (`BuildTitleBar` in `ScribeDialogBase.Layout.cs`) with no reachable
      reference to a sibling widget's live `State` object, so "visible rows" and "is this row
      expanded" needed a home both `BuildTitleBar` and `BuildInboxContent`/`BuildSentAssignmentHistoryContent`
      can read in the same rebuild. `ScribeInboxContent`/`ScribeInboxRow` now take these as props
      (`IsExpanded`/`OnToggleExpand`, `ActiveFilterGroup`/`OnFilterGroupChanged`) instead of owning
      them; both tabs share one set/one filter selection (a TaskId is globally unique, so no
      collision — carrying the selection over when switching tabs is a harmless side effect).
- [x] 4.2 Add a helper that computes, for the currently-visible rows (post state-filter) in the
      active view, whether all of their ids are present in `expandedIds`.
      (`ScribeDialogBase.CurrentlyVisibleAssignmentRowIds` + `AllVisibleAssignmentRowsExpanded`,
      `ScribeDialogBase.ViewSwitching.cs`.)
- [x] 4.3 Add the title-bar toggle button in `ScribeDialogBase.Layout.cs`'s `BuildTitleBar`,
      inserted immediately before the drag-grip-handle entry in both the pencil-present and
      pencil-absent branches of the trailing `Widget[]` array, using the existing `TitleButton`
      helper. Gate its presence on `viewMode is ScribeLecternView.Inbox or ScribeLecternView.SentHistory`.
      Refactored the two hardcoded branches into one `List<Widget>` built conditionally (pencil and
      the new toggle are mutually exclusive view modes, so they never contend for the same slot).
- [x] 4.4 Icon/tooltip: show `scribetriangledown` + "Expand all" tooltip when not all visible rows
      are expanded; show `scribetriangleup` + "Collapse all" tooltip when all visible rows are
      expanded. Tapping bulk-adds (expand) or bulk-removes (collapse) the currently-visible ids
      from `expandedIds`.
      **Deviation:** used `scribetriangleright`/`scribetriangledown` (not `scribetriangleup`) to
      exactly match the per-row chevron's own existing convention (right = something to open, down =
      fully open) rather than introduce a third direction meaning "collapse" — same two icons, more
      consistent with the affordance players already see on every row.
- [x] 4.5 Confirm rows hidden by the active filter chips are unaffected by the toggle (their
      membership in `expandedIds` is left untouched either way). (By construction:
      `ToggleAllVisibleAssignmentRows` only ever adds/removes ids from `CurrentlyVisibleAssignmentRowIds()`
      — code-reviewed; see 6.5 for the in-game check.)

## 5. Lang keys

- [x] 5.1 Add lang keys for the delete control's label/tooltip ("Remove Terminal Record") and the
      two toggle tooltip states ("Expand all" / "Collapse all") to
      `src/Mod/assets/scribe/lang/en.json`, alongside the existing Inbox-row lang keys.

## 6. Manual verification

- [x] 6.1 Manual test: as the Assignee, expand a Completed row in the Inbox, tap
  - Confirmed 2026-09-01: TESTING.md `00000073` "(no note)" (submission 2026-09-01T17-35-07)
      "Remove Terminal Record" — confirm it disappears from the Inbox and does not reappear.
- [x] 6.2 Manual test: as the Assigner, expand a Declined/Cancelled/Discarded row in Sent
  - Confirmed 2026-09-01: TESTING.md `00000074` "(no note)" (submission 2026-09-01T17-35-07)
      History, tap "Remove Terminal Record" — confirm it disappears from Sent History.
- [x] 6.3 Manual test: confirm Unaccepted and Accepted rows never show a delete control, even
  - Confirmed 2026-09-01: TESTING.md `00000075` "(no note)" (submission 2026-09-01T17-35-07)
      when expanded.
- [x] 6.4 Manual test: with a mix of collapsed/expanded rows and at least one terminal record,
  - Confirmed 2026-09-01: TESTING.md `00000076` "(no note)" (submission 2026-09-01T17-35-07)
      tap the title-bar toggle — confirm every visible row expands, tap again — confirm every
      visible row collapses, and confirm the icon/tooltip reflects each state correctly.
- [x] 6.5 Manual test: apply a state filter chip that hides some rows, then use the toggle —
  - Confirmed 2026-09-01: TESTING.md `00000077` "(no note)" (submission 2026-09-01T17-35-07)
      confirm only the visible rows change expansion state.
- [x] 6.6 Manual test: confirm the toggle button is absent on the Read/Editor/Pinned/Settings
  - Confirmed 2026-09-01: TESTING.md `00000078` "(no note)" (submission 2026-09-01T17-35-07)
      views and only appears on Inbox and Sent History.
