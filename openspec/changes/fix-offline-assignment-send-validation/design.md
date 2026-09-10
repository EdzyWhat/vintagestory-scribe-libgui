## Context

`SendAssignmentBatch` (`src/Mod/ScribeModSystem.Assignment.cs:46-81`) is the single server-side
entry point for both delivery paths (Local Inboxes and Send a Notice). Its target-validation guard
at line 51 runs before either path, and is the only place a target UID is checked. See
proposal.md - Why for the root cause.

The server already has a policy of never trusting client-supplied data about delivery mode (see
the doc comment on `SendAssignmentBatch`: "the server re-derives whether a Task Notice is required
from its OWN current `DeliveryMode`... never trusting `ScribeSendAssignmentBatchMessage.
DeliveryChoice` alone"). The same principle applies here: the fix must re-validate the target
against the server's own persisted registry, not simply trust that the client only ever sends a
UID it got from its own picker.

The second, unrelated fix (block particles firing after a Task Notice is already in inventory):
`ScribeAssignment.ReceivedDate` (`src/Core/ScribeAssignment.cs:77-81`) is stamped once, only by
`ScribeAssignmentStore.TryMarkReceived` (`src/Core/ScribeAssignmentStore.cs:198-206`), exactly when
a Sent-state notice record transitions to Unaccepted — i.e. the moment the sealed item actually
enters the recipient's inventory (`MarkReceivedForCarriedNotices`,
`src/Mod/ScribeModSystem.Delivery.cs:376-392`). It stays `null` forever for any Local-Inboxes
assignment, since that path never goes through the Sent state. This makes it the correct, already-
persisted signal for "was this delivered as a physical notice, and has that notice arrived" — no
new field is needed.

## Goals / Non-Goals

**Goals:**
- A send to a currently-offline, previously-known player succeeds through both delivery paths.
- The existing rejection of a garbage/unrecognized UID is preserved unchanged.
- No new persisted state, no wire format changes.
- Once a Task Notice item has reached the recipient's inventory, nearby Inbox-capable blocks stop
  emitting the ambient particle for that assignment, for that recipient.
- The ambient particle continues to fire, exactly as before, for a still-unseen Local-Inboxes
  assignment (no held item exists for that case).
- The Inbox nav-button shimmer's trigger (`HasUnseenAssignment`) is untouched.

**Non-Goals:**
- Fixing the send-button stamp animation firing before server confirmation (noted as a separate,
  pre-existing gap in proposal.md - Impact; not addressed here).
- Reconciling the archived-but-never-synced `assignment-delivery-mode` / `assignment-target-roster`
  capability specs into `openspec/specs/` (unrelated bookkeeping gap, noted in proposal.md).
- Any change to how the client's target-player picker sources its list — that's already correct.
- Changing `BlockEntityInbox`'s separate "notice sitting undiscovered in this Inbox block" particle
  signal, or the world-space proximity ping toward a dropped/not-yet-picked-up notice — neither is
  the bug being fixed (see proposal.md - Impact).

## Decisions

**OR the known-players check into the existing guard, rather than replacing `PlayerByUid`.**
`sapi.World.PlayerByUid` remains the cheap first check for an online (or session-connected)
target — no registry scan needed for the common case. `knownPlayersStore.Contains(targetUid)` is
only consulted when that first check misses, covering the offline-but-known case. This keeps the
change minimal and behavior-preserving for every case that already worked.
- *Alternative considered*: trust the client's target UID outright once it round-trips through the
  known-players sync (the client only ever offers UIDs from its own synced snapshot). Rejected —
  it would mean a target the client validated against a slightly stale sync could still be genuinely
  fake, and the mod's own precedent (delivery-mode re-derivation, above) is to always re-validate
  the security-relevant input server-side rather than trust the wire.

**Add `Contains(string uid)` to `ScribeKnownPlayersStore`, rather than scanning `Snapshot()`.**
`Snapshot()` allocates a new `List<(string,string)>` on every call; a per-send membership check
should be an O(1) dictionary lookup against the store's existing private `Dictionary<string,
string>` backing field.
- *Alternative considered*: call `Snapshot().Any(p => p.Uid == targetUid)` at the call site.
  Rejected as an unnecessary allocation for a check that will run on every assignment send.

**Add a new gate (e.g. `HasUnseenUndeliveredAssignment`) alongside `HasUnseenAssignment`, rather
than editing `HasUnseenAssignment` itself.** `HasUnseenAssignment` has two consumers
(`ScribeDialogBase.ShowInboxShimmer` and `OnAssignmentParticleTick`) and this fix should only
change the second. A new, more-specific property keeps the nav-button shimmer's existing behavior
byte-for-byte and makes the particle tick's new condition self-documenting at the call site.
- *Alternative considered*: narrow `HasUnseenAssignment` itself and accept the nav-button shimmer
  also going quiet for a received-but-unopened notice. Rejected — proposal.md scopes this fix to
  the world-space particle only; the shimmer isn't the "wasted time" problem reported, and folding
  both into one property would silently change UI behavior nobody asked to change.

**Put the actual `Seen && ReceivedDate is null` predicate in `src/Core/` as a small
`ScribeAssignment` member (e.g. a `NeedsAmbientParticle`-style computed property or a static
extension, mirroring the existing `IsTerminal` extension on `ScribeAssignmentState`), and have the
Mod-layer gate just call it.** This keeps the one bit of actual logic worth a regression test —
"is this record unseen-and-never-notice-delivered" — unit-testable in `Core.Tests` without a game
install, consistent with this repo's `src/Core/` invariant. The Mod-layer property itself
(iterating `myReceivedAssignments`, a client-only cache) still can't be unit-tested and is only
exercised in-game, but the predicate it delegates to can be.
- *Alternative considered*: write the predicate inline in `ScribeModSystem.cs` only (as sketched
  above). Rejected — it's pure boolean logic over two already-Core fields with no game dependency;
  leaving it untested in the Mod layer when it could trivially live in, and be tested from, Core is
  an avoidable gap.

## Risks / Trade-offs

- [A target who was known in a prior world save but whose savegame-persisted entry was somehow
  lost would still be rejected exactly as an unknown player.] → Acceptable: this matches existing
  behavior for any other lost-registry-entry case and is not a regression.
- [`knownPlayersStore` is null on a pure client / before `StartServerSide` runs.] → The guard
  already runs inside a server-only method gated by `if (sapi is null ...) return;`; the null-check
  pattern already used elsewhere in this file (`knownPlayersStore?.` or an early-return guard)
  applies identically here.
- [A player who redirects a Task Notice to someone else who never had it addressed to them
  (`add-task-notice-redirect-confirm`, `RedirectedFromUid`) — does the new redirect target's copy
  of the record already carry `ReceivedDate`?] → Yes: redirect happens via
  `ScribeAssignmentStore.TryRedirectTarget` on the *existing* record (same `ReceivedDate`, already
  stamped when the original holder received the physical notice) — it only overwrites
  `TargetPlayerUid`/`RedirectedFromUid`. The new target's copy already has `ReceivedDate` set, so
  the particle correctly stays off for them too, consistent with them also already holding the
  item.
