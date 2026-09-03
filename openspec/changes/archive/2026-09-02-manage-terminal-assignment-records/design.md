## Context

The Assignment Inbox and Sent History share one render path (`ScribeInboxContent.cs`). Today an
assignment record, once created, is never removed — `ScribeAssignmentStore` (`src/Core/`) is
explicitly append-only; only `State` changes. Playtesting has shown terminal-state clutter
(Declined/Cancelled/Discarded/Completed rows accumulating with no way to clear them) and slow
manual expand-by-expand scanning of long lists. Both features are scoped to Inbox + Sent History
only, gated behind the row caret (delete) or the active view mode (toggle).

Per-row expand state is currently a private `bool` on each row's own `State` object, set by that
row's own chevron tap. There is no shared record of which rows are expanded, which the
expand/collapse-all toggle needs in order to know its own icon direction and to bulk-mutate every
visible row at once.

## Goals / Non-Goals

**Goals:**
- Let a player permanently clear a terminal-state assignment record from their Inbox or Sent
  History, restricted to that record's own Assigner or Assignee, with no confirmation step.
- Let a player expand or collapse every currently-visible row in one tap, from a single title-bar
  toggle, using existing icon assets.
- Keep the store's mutation surface narrow: deletion is a new, explicit, terminal-only operation,
  not a repurposing of any existing state transition.

**Non-Goals:**
- No confirmation/undo for the delete (matches every other delete in the mod today).
- No cross-view "expand all" (Inbox and Sent History each toggle only their own visible rows).
- No schema/version bump to the store's binary format — removing a record doesn't change the
  shape of the records that remain.
- No change to which assignments filter chips show or hide — deletion and expand/collapse-all
  both operate only on whatever the filter already leaves visible.

## Decisions

### Deletion is a new store method, not a seventh state
`ScribeAssignmentStore` gets `TryDelete(Guid assignmentId, string actingPlayerUid)`, removing the
record from `_records` outright when `actingPlayerUid` matches either the record's `AssignerUid`
or `AssigneeUid` and the record's `State` is one of the four terminal states; otherwise it no-ops
and returns false. Modeling this as removal (not a `Deleted` state) keeps the state machine's
existing "six states, terminal states accept no further transition" invariant untouched — deletion
is deliberately outside that machine, not a seventh branch of it.

**Alternative considered:** add `Deleted` as a seventh terminal state and just filter it from
every view. Rejected — it keeps dead records around forever for no benefit, doubles the terminal
states the state-machine spec has to reason about, and every future reader of the store would need
to remember to filter it, whereas an actual `Remove` gives one clear place that enforces the rule.

### Network shape mirrors `ScribeDeleteHistoryEntryMessage` / `ScribeDeleteTaskMessage`
New `ScribeDeleteAssignmentMessage { AssignmentId }` sent client→server; handler lives alongside
the existing `TryApplyAction` handlers in `ScribeModSystem.Assignment.cs`, calls `TryDelete` with
the sending player's UID, and on success re-syncs `Sent`/`Received` lists to affected clients the
same way any other assignment mutation does today. No client-side optimistic removal — the row
disappears once the resynced list arrives, consistent with `DeleteManualEntry`'s existing pattern.

### Terminal-state test gets one shared helper
Add a static helper (e.g. `ScribeAssignmentState.IsTerminal(this ScribeAssignmentState state)` in
`src/Core/`) returning true for Declined/Cancelled/Discarded/Completed, used by both the store's
`TryDelete` guard and the Mod-layer code deciding whether to render the delete button — replacing
what would otherwise be the same four-state check duplicated at both call sites.

### Delete button placement: its own row below the transition-date lines
Inside `BuildExpandedDetail`, the terminal branch currently renders only `metaLines` (assigner,
transition date) with no action row (`ScribeInboxContent.cs:385-404` only adds `actions` for
Unaccepted/Accepted). For a terminal row, append one `ActionButton`-styled row below the existing
metaLines, right-aligned, containing only the delete control — reusing the same
`ActionButton(labelKey, ButtonVariant, onTap, colors)` helper already used for Accept/Decline, so
a terminal row's layout looks structurally like a non-terminal row's action area, just with one
button instead of two. Icon/color: `ScribeRowButton` with icon `"scribeclose"`,
`iconColor: colors.Error` (matches the mod's existing delete convention). Label/tooltip text:
"Remove Terminal Record".

### Expand/collapse-all: lift state into a shared `HashSet<Guid>`
`ScribeInboxContentState` gains `HashSet<Guid> expandedIds` (idiom precedent:
`GuiDialogScribeAssignmentDesk.cs`'s `selectedTaskIds`). Each row's chevron `onTap` now adds/removes
its own id from that set (via a callback passed down) instead of flipping a private bool; a row
reads its expanded/collapsed render from `expandedIds.Contains(id)` instead of its own field. The
title-bar toggle computes `allVisibleIds.All(expandedIds.Contains)`: true → render the up-chevron
and collapse (remove all visible ids from the set) on tap; false → render the down-chevron and
expand (add all visible ids to the set) on tap. "Visible" means the rows currently passing the
existing state-filter chips in whichever view (Inbox or Sent History) is active — the two views
don't share one set of visible ids, but they can share the same `expandedIds` field type/shape
since only one view is ever active at a time in a given dialog instance.

### Toggle icon and placement
Icon-only `TitleButton(iconName, tooltipKey, ...)` (existing helper,
`ScribeDialogBase.Layout.cs:549-551`), inserted immediately before the grip handle's entry in
`BuildTitleBar`'s trailing `Widget[]` array (both the pencil-present and pencil-absent branches),
gated on `viewMode is ScribeLecternView.Inbox or ScribeLecternView.SentHistory`. Reuses the
existing `scribetriangleup`/`scribetriangledown` icons — no new SVG asset. Tooltip text switches
between "Expand all" and "Collapse all" based on the same `allVisibleIds.All(...)` check that picks
the icon, so icon and tooltip always agree.

## Risks / Trade-offs

- **[Risk] Lifting expand state changes existing per-row toggle behavior.** → Mitigation: the
  lift is a pure storage-location change (bool field → set membership); the per-row chevron tap
  keeps exactly the same visible effect (that one row's expand/collapse), so existing manual-test
  coverage of the chevron should still pass unmodified.
  A visible timing question: the current key is `ValueKey<Guid>(r.TaskId)`; the toggle's "delete
  a terminal record" and "this record's expanded state" now both use `Guid` ids on the same
  content state, so no double-bookkeeping is introduced.
- **[Risk] Deleting a record a moment before another player (e.g. the Assigner) opens their own
  Sent History still showing it.** → Mitigation: resync on delete follows the exact same
  server-authoritative push used for every other assignment mutation; the deleting player's own
  view updates from that same resync, so there's no special-cased staleness beyond what already
  exists for any other concurrent assignment edit.
- **[Trade-off] No confirmation on a permanent delete.** → Accepted per explicit product decision
  (matches existing delete conventions in the mod); the two-step gate (expand caret, then tap
  delete) plus the terminal-only restriction is the agreed-upon friction.
