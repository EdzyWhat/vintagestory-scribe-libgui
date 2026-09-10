## Why

This change bundles two independent Assignment fixes.

Sending an assignment batch to a currently-offline target silently fails: the server drops the
whole batch before either delivery path (Local Inboxes or Send a Notice) runs, so no assignment
record and no Sealed Task Notice is ever created — while the client's send-button stamp animation
plays regardless, since it's a client-only optimistic effect not gated on server success. This
makes the failure invisible during play: the sender sees the same "sent" feedback as a successful
send.

Root cause: `SendAssignmentBatch` (`src/Mod/ScribeModSystem.Assignment.cs:51`) validates the
target with `sapi.World.PlayerByUid(targetUid) is null`, a check that only recognizes players who
have connected at least once since the *current server process started* — it is not backed by
persisted savegame player data. A legitimately offline player who simply hasn't reconnected since
the last server restart resolves to `null` there and the whole send is dropped.

This check predates offline targeting. The Assignment Desk's target-player picker was later
extended (`persist-known-players-for-assignment`) to offer offline, previously-known players by
consulting a world-persisted `knownPlayersStore` registry — specifically to unblock the
already-designed Hybrid-delivery-mode scenario of assigning to a currently-offline target. The
picker was updated to match that design; the server-side send validation was not.

Second, unrelated bug: once a sealed Task Notice item actually enters the recipient's inventory,
every nearby Inbox-capable block (Desk, Lectern, Scriptorium, Chalkboard, standalone Inbox) starts
emitting the ambient "unseen assignment" particle effect (`inbox-tab`'s "Inbox-capable blocks show
an ambient particle when the player has an unseen assignment" requirement,
`src/Mod/BlockEntityScribeWritingStation.cs:284-305`'s `OnAssignmentParticleTick`, gated by
`ScribeModSystem.HasUnseenAssignment` at `ScribeModSystem.cs:176`). That gate is
`myReceivedAssignments.Any(b => b.Assignment is { Seen: false })` — it doesn't distinguish a
notice-delivered assignment (the player is already holding the physical item that *is* the
assignment) from a Local-Inboxes assignment (nothing physical exists yet; the player genuinely
needs to walk to a block to see it). The result: receiving a Task Notice into inventory pulls the
player's attention toward nearby blocks' particles — a second, redundant "you have something new"
signal fired *after* the first (better) one already succeeded, sending the player toward a "shiny
object" instead of the notice they're already holding. We should trust the player to notice the
item they just received and stop firing the block particle for notice-delivered assignments.

## What Changes

- Add a membership-check method to `ScribeKnownPlayersStore` (`src/Core/ScribeKnownPlayersStore.cs`)
  so callers can ask "is this UID a known player" without needing the full snapshot.
- In `SendAssignmentBatch`, accept a target UID if it resolves via EITHER `sapi.World.PlayerByUid`
  (online or connected earlier this session) OR the server's persisted `knownPlayersStore` (known
  from a prior session) — the known-players registry becomes the authoritative "is this a
  legitimate target" check, replacing the session-only one for players who aren't currently
  connected.
- No change to the invalid/garbage-UID rejection behavior: a UID that is neither online-this-
  session nor in the known-players registry is still rejected exactly as before.
- Stop the ambient "unseen assignment" block particle from firing for an assignment that was
  delivered as a Task Notice item once that item has actually reached the recipient's inventory.
  The particle continues to fire for Local-Inboxes-delivered assignments exactly as before — that
  case has no physical item, so the ambient nudge to visit a block is still the only signal the
  player gets. The Inbox nav-button shimmer (`inbox-tab`'s other unseen-assignment indicator,
  `ScribeDialogBase.ShowInboxShimmer`) is unaffected by this change — it's a passive indicator
  inside a dialog the player already opened, not a "come over here" world-space signal, and isn't
  the "wasted time" problem being fixed.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `assignment-multi-item-creation`: the "Sending a batch creates one independent assignment per
  selected row" requirement is clarified/extended so the recipient may be any player the target
  picker can validly present — including a currently-offline, previously-known player — and the
  server SHALL create the assignment(s) rather than silently dropping the batch in that case.
- `inbox-tab`: the "Inbox-capable blocks show an ambient particle when the player has an unseen
  assignment" requirement is narrowed so a notice-delivered assignment no longer counts once the
  notice has been received into the recipient's inventory — it does not trigger the block particle
  for that recipient, even though it's still otherwise "unseen" (Seen: false) until they open an
  Inbox.

## Impact

- `src/Core/ScribeKnownPlayersStore.cs`: add a `Contains(string uid)` (or equivalent) lookup.
- `src/Mod/ScribeModSystem.Assignment.cs`: `SendAssignmentBatch`'s target-validation guard
  (line 51) now also consults `knownPlayersStore`.
- No wire/message format changes, no save-file compatibility impact (reads the same existing
  `knownPlayersStore`, adds no new persisted state).
- `src/Core/ScribeAssignment.cs`: add a small predicate ("unseen AND not yet delivered as a held
  notice item", i.e. `Seen == false && ReceivedDate is null`) as a Core member, unit-testable
  without a game install.
- `src/Mod/ScribeModSystem.cs`: add a new gate (e.g. `HasUnseenUndeliveredAssignment`) delegating
  to that Core predicate over `myReceivedAssignments`, alongside the existing `HasUnseenAssignment`
  (which stays as-is for the nav-button shimmer).
- `src/Mod/BlockEntityScribeWritingStation.cs`: `OnAssignmentParticleTick` (lines 284-305) checks
  the new gate instead of `HasUnseenAssignment`. One change point covers all five Inbox-capable
  blocks, since they all share this base class's particle-tick registration.
- Out of scope for this change (noted, not addressed here): the send-button stamp animation firing
  optimistically before server confirmation is a separate, pre-existing UX gap that also affects
  other failure modes (e.g. a malformed batch); and the `assignment-delivery-mode` /
  `assignment-target-roster` capability specs are archived/pending but never synced into
  `openspec/specs/` — an existing gap in this repo's spec bookkeeping, unrelated to this bug. Also
  out of scope: `BlockEntityInbox`'s separate notice-in-Inbox particle signal
  (`HoldsUndiscoveredNoticeFor`, `BlockEntityInbox.cs:164-168`) and the world-space proximity ping
  toward a dropped, not-yet-picked-up notice (`OnTaskNoticeProximityTick`,
  `ScribeModSystem.Delivery.cs:330-361`) — both already stop once the notice is no longer sitting
  unclaimed, and neither is the "particles after the item is already in inventory" bug reported.
