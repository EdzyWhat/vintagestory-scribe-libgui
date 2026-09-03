## Context

`ScribeAssignmentStore` keeps ONE canonical `ScribeAssignment` per assignment id (class doc:
"one canonical object"). `Sent(playerUid)`/`Received(playerUid)` are just filtered reads over
`_records` — the Assigner's Sent Assignment History and the Assignee's Inbox are two views of the
same underlying object, which is exactly why `manage-terminal-assignment-records`'s `TryDelete`
(authorized to "the Assigner OR the Assignee") removes the record outright: there was never a
notion of "my copy" vs. "their copy." That's correct for every OTHER mutation (state
transitions must stay locked-on-send and single-sourced), but deletion is different — a
terminal record is inert (no further transitions possible), so each party's copy can now safely
diverge in exactly one dimension: whether THEY still want to see it.

## Goals / Non-Goals

**Goals:**
- Deleting from the Inbox (as Assignee) only removes it from that player's Inbox.
- Deleting from Sent Assignment History (as Assigner) only removes it from that player's Sent
  History.
- This holds even when Assigner == Assignee (a self-assignment) — the two views are independent
  regardless of whether the same real player controls both.
- The record is fully purged from the store once neither side can see it anymore (no orphaned
  data lingering forever).

**Non-Goals:**
- No change to the delete button's UI, label, terminal-only gating, or no-confirmation behavior.
- No change to any state-machine transition (Accept/Decline/Cancel/Discard) — those remain
  single-sourced on the one shared record, exactly as today.
- No "undo" for a per-side delete — same as today's permanent deletion, just now scoped.

## Decisions

**1. Two independent `bool` flags on `ScribeAssignment`, not a per-role visibility abstraction.**
Add `HiddenFromAssignee` and `HiddenFromAssigner` (default `false`). Considered a single
`ScribeAssignmentActor?` "deleted by" field, but a self-assignment needs BOTH sides
independently deletable on the same record — a single field can't represent "hidden from
Assignee AND hidden from Assigner" as two separate facts when Assigner == Assignee. Two flags
also read directly as "is this visible in view X," which is exactly what `Sent`/`Received`
need to check.

**2. `TryDelete` takes an explicit `ScribeAssignmentActor side` parameter instead of inferring
role from the acting player's uid.** Today's signature (`TryDelete(Guid assignmentId, string
actingPlayerUid)`) tries Assignee-or-Assigner and takes whichever matches — fine when deletion
was one shared action, but now ambiguous for a self-assignment: the acting player matches BOTH
roles, and only the CLIENT knows which view the delete button was pressed in. The server still
independently authorizes: `side == Assignee` requires `actingPlayerUid == TargetPlayerUid`;
`side == Assigner` requires `actingPlayerUid == AssignerUid`. A caller claiming a side they
don't actually hold is rejected exactly as before — the new parameter disambiguates INTENT for
a dual-role player, it does not loosen authorization.

**3. Fully purge the record once both flags are true.** Once hidden from both sides, nothing can
ever read it again (no third view exists), so there's no reason to keep it — same "the one
deliberate hole in the append-only store" contract, just now triggered by the second delete
instead of the first.

**4. `Sent`/`Received` filter on the respective flag; `TryGet` stays unfiltered.** `TryGet` is
used by the delete/action handlers themselves (which need the record to exist to authorize
against it, even if hidden from the requester's own side) and by places resolving a record by id
for legitimate non-viewing reasons. Only the two player-facing list views need to hide a
side-deleted record.

**5. `ScribeDeleteAssignmentMessage` gains a `byte Side`.** The client already knows which view
it's deleting from (`ScribeDialogBase.DeleteAssignmentRecord` runs while `viewMode` is either
`Inbox` or `SentHistory`, since the delete control only ever renders inside those two content
builders) — map `Inbox → Assignee`, `SentHistory → Assigner`. This is a hint the server verifies,
never trusts outright (Decision 2's authorization check still applies).

**6. Store version bump 5 → 6.** `AcceptedIntoLabel` (v5) landed this session immediately before
this change; this is the next available version. A pre-v6 blob's records get both new flags
defaulted to `false` on read — nothing was ever hidden, which is exactly correct (not lossy).

## Risks / Trade-offs

- **[Risk]** A self-assignment's record now needs BOTH `TryDelete` calls (once from each view)
  before it's actually gone — a player might delete from the Inbox, see it vanish there, and be
  surprised it still shows in Sent Assignment History. → **Mitigation**: this is the explicitly
  requested behavior ("I want each side to have a distinct record they have control over") — the
  surprise is now "why is it still in the other tab," which is a much smaller, more honest
  surprise than "why did it vanish from both."
- **[Risk]** Existing tests call `TryDelete(id, uid)` with the old two-argument signature — this
  is a breaking signature change, not additive. → **Mitigation**: single-assembly, no external
  callers; update every call site (production and test) in the same change. No back-compat
  overload needed since nothing outside this repo calls it.

## Migration Plan

Purely additive at the data layer (new fields default false, version bump is progressive-read
backward-compatible) but a breaking API change to `TryDelete`'s signature within the codebase.
Deploy as a normal version bump: ship the v6 codec change; existing v1-v5 save blobs keep loading
(their records simply start with nothing hidden from either side). Restage client and server
together (a mismatched pair could otherwise misread the `Side` byte's absence/presence on the
wire if one side is stale — same discipline as every prior assignment-store version bump).
