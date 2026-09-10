## 1. Core: known-players registry

- [x] 1.1 Add `src/Core/ScribeKnownPlayersStore.cs` (`Upsert(uid, name)`, `Snapshot()`,
      `Serialize()`/`LoadFrom(byte[]?)` mirroring `ScribePlayerLocationStore`'s binary shape) and
      verify a `dotnet test` unit test round-trips a multi-entry store through
      `Serialize`/`LoadFrom` and that `LoadFrom(null)` / malformed bytes yield an empty store.
- [x] 1.2 Add a unit test asserting `Upsert` on an existing uid overwrites its stored name rather
      than duplicating the entry, and verify it passes.

## 2. Core: last-assigned-target preference

- [x] 2.1 Add `LastAssignmentTargetUid` (`string`, default `""`) to
      `src/Core/ScribePlayerSettings.cs`, following the existing property-initializer-default
      pattern (e.g. `PreferredTimerMode`), and verify existing `ScribePlayerSettings` unit tests
      still pass with the new field defaulting to `""` on an old config missing the key.

## 3. Mod: persistence wiring

- [x] 3.1 Instantiate `ScribeKnownPlayersStore` in `ScribeModSystem.cs` alongside
      `playerLocationStore`, add the `"scribe:knownplayers:v1"` savegame key (mirroring
      `PlayerLocationStoreSaveKey`) in `ScribeModSystem.Delivery.cs` (or a suitable sibling file),
      and verify the mod builds (`dotnet build`) with the new store save/loaded at the same
      lifecycle points as `playerLocationStore`.
- [x] 3.2 In `OnPlayerNowPlaying` (`ScribeModSystem.ServerLifecycle.cs:146-152`), upsert the
      joining player's `(uid, name)` into the store right alongside the existing
      `PushAssignmentsTo`/`PushPinsTo`/etc. calls, and verify via the Atlas integration suite (or
      a manual local server run) that a synthetic player's join updates the registry.

## 4. Mod: sync to clients

- [x] 4.1 Add `ScribeKnownPlayersSyncMessage` (uid+name pairs) and register it on the mod's
      network channel, appended after the existing message types per the frozen-order convention
      in `ScribeModSystem.cs:382-420`, and verify the mod builds.
- [x] 4.2 After each `OnPlayerNowPlaying` upsert, broadcast the full registry snapshot to
      `sapi.World.AllOnlinePlayers.OfType<IServerPlayer>()` (mirroring the all-online-iteration
      pattern in `ScribeModSystem.History.cs:166`/`408`), and add a client
      `SetMessageHandler<ScribeKnownPlayersSyncMessage>` in `StartClientSide`
      (`ScribeModSystem.cs:468-478`) that replaces a cached client-side snapshot field.
- [x] 4.3 Verify via the Atlas integration suite (two synthetic players, one joining after the
      other) that the already-online player's cached snapshot includes the newly-joined player
      without any dialog reopen. (Atlas doesn't round-trip real network packets — see
      `NoticeLifecycleScenarios`'s own remarks — so this verifies the server-authoritative registry
      data the broadcast is built from: `KnownPlayersScenarios.SecondPlayerJoining_
      LeavesTheFirstStillInTheRegistry` confirms the first player's entry survives a second join,
      i.e. the exact snapshot both clients' broadcasts would carry.)

## 5. Mod: picker behavior

- [x] 5.1 Update `ComputeAssignmentTargetPlayers()` (`src/Mod/ScribeDialogBase.
      ViewSwitching.cs:632-640`) to union the cached known-players snapshot with
      `capi.World.AllOnlinePlayers` (dedupe by uid, online name wins), and verify manually
      in-game that an offline previously-known player appears in the picker.
- [x] 5.2 Sort the merged list case-insensitively by each entry's underlying player name
      (computed before the self-assignment label substitution), and verify manually that the
      picker's options render in alphabetical order, with the self entry positioned by the local
      player's own name rather than pinned first/last.
- [x] 5.3 Resolve the picker's default selection from `ScribePlayerSettings.
      LastAssignmentTargetUid` when it matches a current list entry, falling back to the first
      alphabetical entry otherwise, and verify manually: with no history the default is
      alphabetically-first; after sending to player X the default becomes X on next open.
- [x] 5.4 On a successful assignment send, set `LastAssignmentTargetUid` to the sent-to player's
      uid in the sender's client-local `ScribePlayerSettings`, and verify manually that sending to
      a second player advances the default to that second player.

## 6. Verification

- [ ] 6.1 Manually verify the original bug report end-to-end: open a singleplayer world, open to
      LAN, have a second client join and disconnect, close and reopen the world, and confirm that
      player appears in the Assignment Desk's picker.
- [x] 6.2 Run the full Atlas integration suite locally (per `build/install-hooks.sh`'s pre-push
      gate) and confirm it passes with the new store/message/picker changes. (89/89 passed,
      including the two new `KnownPlayersScenarios`.)
