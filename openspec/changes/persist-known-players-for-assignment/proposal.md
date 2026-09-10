## Why

The Assignment Desk's target-player picker (`ComputeAssignmentTargetPlayers()` in
`src/Mod/ScribeDialogBase.ViewSwitching.cs:632-640`) is built live from `capi.World.AllOnlinePlayers`
on every render, with no persisted backing store. A player who has ever joined a world — including
one who joined only during a temporary singleplayer "Open to LAN" session — becomes permanently
unassignable the moment they disconnect, even though they are a real, known participant in that
world. Confirmed in the "Scribe-Test" world (hosted by Junkmuffin): RaptorKhan joined via LAN,
played, then disconnected; on the next session, RaptorKhan no longer appears in the picker at all.
This isn't LAN-specific — it reproduces for ANY offline player, singleplayer-LAN or dedicated
server — but LAN is the case that surfaced it, since a LAN guest is the player most likely to be
offline when the world is later reopened solo.

While addressing this, two related picker usability gaps are worth fixing in the same pass: the
picker list isn't kept in a stable, predictable order, and it always defaults to whichever entry
happens to be first rather than the player the Assigner most likely wants to target again.

## What Changes

- Add a server-side, world-persisted known-players registry (uid + last-seen display name) that
  the Assignment Desk's target-player picker consults instead of (well, unioned with)
  `AllOnlinePlayers` alone — so anyone who has ever connected to the world remains assignable
  across sessions, regardless of how they connected (direct join, LAN, dedicated server).
- Populate the registry on player join (and refresh the stored display name on each join, in case
  it changed), so it captures everyone who has ever been online — not just those who disconnected
  cleanly.
- Sync the registry to clients so the picker can render offline entries by name.
- Order the picker list alphabetically by player name whenever an entry is added, so the list has
  a stable, predictable order rather than reflecting connection/insertion order.
- Change the picker's default selection from "first item in the list" to "the last player this
  Assigner successfully sent an assignment to" (a per-player, client-local preference), falling
  back to the first alphabetical entry if there is no such history or that player is no longer in
  the list.
- Unblocks the existing `assignment-delivery-mode` spec's "Assigner selects a currently offline
  target" scenario for Hybrid delivery mode, which today is unreachable because offline players
  never appear in the picker.

## Capabilities

### New Capabilities
- `assignment-target-roster`: persists a world-scoped known-players registry, unions it with live
  online players to source the Assignment Desk's target-player picker, keeps that list
  alphabetically ordered, and defaults the picker's selection to the Assigner's last-assigned
  target instead of the first list entry.

### Modified Capabilities
(none — `assignment-desk-block`'s existing layout requirements are unaffected; this change only
alters what feeds the picker's option list and its default selection, which is new behavior
covered by `assignment-target-roster`.)

## Impact

- `src/Core/`: new `ScribeKnownPlayersStore` (uid → last-seen display name, game-agnostic,
  serialized like `ScribePlayerLocationStore`); `ScribePlayerSettings` gains a client-local
  "last assignment target uid" preference.
- `src/Mod/`: `ScribeModSystem.cs`/`ScribeModSystem.Delivery.cs` wire join handling to the new
  store and its savegame key; a network message syncs the registry to clients; sending an
  assignment records the target uid into the sender's `ScribePlayerSettings`.
- `src/Mod/ScribeDialogBase.ViewSwitching.cs`: `ComputeAssignmentTargetPlayers()` unions the synced
  known-players registry with `capi.World.AllOnlinePlayers`, sorts alphabetically, and resolves the
  default selection from the last-assigned-target preference.
- No changes to `assignment-desk-block`'s layout requirements or to save-file compatibility beyond
  adding one new savegame key (absent key = empty registry, same graceful-empty pattern as
  `ScribePlayerLocationStore`).
