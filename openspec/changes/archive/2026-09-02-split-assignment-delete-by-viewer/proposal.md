## Why

`manage-terminal-assignment-records` let either party permanently delete a terminal assignment
record, but `ScribeAssignmentStore` only ever kept ONE canonical record per assignment — the
Inbox (Assignee's view) and Sent Assignment History (Assigner's view) are both just filtered
reads over that same record, so deleting it from either view removed it from both. Playtest
(2026-09-01) surfaced this directly: sending an assignment to yourself and deleting its terminal
record from one tab made it vanish from the other too, which read as a bug, not a feature —
"I thought I was only deleting it for myself from the one view, not from both." Each side should
control its own copy.

## What Changes

- `ScribeAssignment` gains two independent hidden-flags — one per side of the relationship — so a
  self-assignment (Assigner == Assignee) can have each side deleted independently even though
  it's the same underlying record and, in that case, the same real player.
- `ScribeAssignmentStore.TryDelete` takes an explicit `ScribeAssignmentActor side` parameter
  instead of authorizing against "Assigner OR Assignee" ambiguously — deleting sets only that
  side's hidden flag; the record is only actually removed from the store once BOTH sides have
  deleted their copy.
- `ScribeAssignmentStore.Received`/`Sent` filter out a record hidden from that respective side, so
  a party who deleted their copy simply stops seeing it — the other party's view is unaffected.
- Store version bump 5 → 6 (progressive read, backward-compatible: a pre-v6 blob's records default
  both flags to false — nothing was ever hidden).
- `ScribeDeleteAssignmentMessage` gains a `Side` byte so the server knows which view the delete
  request came from (still independently authorized against the acting player's real UID, never
  trusted alone).
- No UI/button changes — the delete control's label, terminal-only gating, caret-expand
  visibility, and no-confirmation behavior are all unchanged. This is a backend semantics fix to
  what deletion actually does to the shared record.

## Capabilities

### Modified Capabilities
- `assignment-state-machine`: the "A terminal assignment record can be permanently deleted by its
  Assigner or Assignee" requirement is replaced with per-side deletion — each party's deletion
  only removes their own view of the record; the underlying record is only fully purged once both
  sides have deleted it.

## Impact

- `src/Core/ScribeAssignment.cs` — two new hidden-flag fields, `Clone()` update.
- `src/Core/ScribeAssignmentStore.cs` — `TryDelete` signature change, `Received`/`Sent` filtering,
  version bump 5 → 6, serialization.
- `tests/Core.Tests/ScribeAssignmentStoreTests.cs` — updated/new coverage for per-side deletion,
  round-trip, and backward-compat.
- `src/Mod/ScribeDeleteAssignmentMessage.cs` — new `Side` field.
- `src/Mod/ScribeModSystem.Assignment.cs` — `OnServerReceivedDeleteAssignment` decodes the side.
- `src/Mod/ScribeDialogBase.ViewSwitching.cs` — `DeleteAssignmentRecord` sends the current
  `viewMode`'s side.
- `openspec/specs/assignment-state-machine/spec.md` — requirement text updated for per-side
  deletion semantics.
