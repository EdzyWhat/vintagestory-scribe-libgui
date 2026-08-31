## Why

An accepted assignment currently shows only a single static line in its expanded row —
"Assigned by X - <date>" — with no record of what Scribe item it was accepted onto, or of
what happened to it afterward (completed, declined, cancelled, discarded). Once an
assignment leaves the Assigned state, the player has no way to see its history; the row
just reflects whatever the current state happens to be. Playtest feedback (2026-08-31)
asked for the expanded row to read as a short running log instead, so both the assigner
and assignee can see the full lifecycle of an assignment at a glance.

## What Changes

- Persist an ordered, server-authoritative log of lifecycle events per assignment. Unlike
  block entities (which follow the vanilla Sign `ToTreeAttributes`/`FromTreeAttributes`
  pattern), `ScribeAssignment` persists through its own hand-rolled binary codecs — this
  follows that existing shape (see `HistoryEntry`/`HistoryStore` for the closest structural
  precedent: an append-only, dated, per-record log with its own tiny versioned codec), not
  the tree-attribute one.
- Record a log entry when an assignment is Accepted, capturing which item/slot it was
  placed onto — rendered as `Accepted onto <Type> "<Title>" - <date>`, reusing the existing
  `FormatCandidateLabel` style already used for Accept-candidate labels.
- Record a log entry for each subsequent terminal/lifecycle action (Complete, Decline,
  Cancel, Discard), each with its own date.
- Change the shared inbox row's expanded rendering (`ScribeInboxContent`) to list these log
  entries in order underneath the existing "Assigned by X - <date>" line, instead of only
  ever showing the current state.
- Out of scope: no change to the row's collapsed/expanded interaction shape, no change to
  the assignment state machine's transitions or validity rules, no new UI for browsing or
  filtering the log itself (it renders inline, in order, with no separate view).

## Capabilities

### New Capabilities
- `assignment-activity-log`: the persisted, ordered per-assignment event log (Accepted/
  Completed/Declined/Cancelled/Discarded entries with timestamps and, for Accept, the
  target item label) and its rendering in the expanded inbox row.

### Modified Capabilities
(none in `openspec/specs/` — see Impact)

## Impact

- Builds directly on top of two capabilities defined by the not-yet-archived
  `add-assignment-and-quest-support` change (`assignment-state-machine` and `inbox-tab`,
  currently only delta specs under that change's own `specs/`, not yet merged into
  `openspec/specs/`). This proposal does not restate or modify those specs' requirements —
  it adds a new, additive capability that observes the same lifecycle actions and renders
  into the same expanded-row surface. If `add-assignment-and-quest-support` archives before
  this change lands, no action is needed here (its specs merge into `openspec/specs/`
  unchanged); if this change lands first, its dependency on those two capabilities should be
  re-checked against whatever the sibling change's specs settle on before *this* change
  archives.
- Code impact: `src/Mod/ScribeAssignment.cs` (or wherever the assignment record type lives)
  gains a log-entries field; the server-side assignment store/action handler
  (`ScribeAssignmentStore.TryApplyAction` and the Accept path) appends an entry per
  transition; `ScribeInboxContent`/`ScribeInboxRowData` gain the log data and render it;
  network sync messages carrying assignment state need the log entries added to their
  payload.
- No `src/Core/` involvement expected unless the log entry type is judged game-agnostic
  enough to belong there — default assumption is it stays in `src/Mod/` alongside the rest
  of the assignment plumbing, since it embeds player-facing formatted labels tied to game
  concepts (item stacks, `Lang`).
