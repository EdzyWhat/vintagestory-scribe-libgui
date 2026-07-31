## Why

The lectern's single-editor lock is meant to be transient — it exists only to stop a second
player from entering the editor while someone else is mid-edit (a crash-prevention guard). In
practice it behaves like a permanent lockout: once player 1 opens the editor, player 2 can
never edit that lectern again, even after player 1 closes the dialog or disconnects and player
2 relogs.

The root cause is that the server's authoritative in-memory `lockHolderUid` is not reliably
cleared when the holder leaves the editor. Release is fired only from specific client paths
(the editor-mode close/switch and `OnPlayerDisconnect`); when none of those fire — or the
release packet's DocId doesn't resolve to the block on the server — the holder stays set for
the lifetime of the loaded block. (This was flagged but never verified in the original lock
change: its own design listed "lock leak on disconnect/crash → stays locked forever" as an
unconfirmed risk and open question.) The lock is also written into the block-entity tree, but
that only ever reaches the *client mirror* (`syncedLockHolderUid`) — the server rebuilds packets
from live state and starts fresh on load — so a full server restart already self-heals; a relog
does not, which matches the observed symptom.

Separately, the sticky "one owner" behavior is actually a useful primitive to keep — as an
opt-in *private / read-only* permission on a lectern — just not as the accidental default and
not conflated with the crash-prevention lock.

## What Changes

- **Make lock release robust and self-healing** so a held lock can never outlive the holder's
  editing session:
  - Clear the in-memory `lockHolderUid` when the block entity is (re)loaded, so no path can
    leave a lectern loaded with a stale holder.
  - Release the lock on **every** dialog close, not only when the client happens to still be in
    editor mode at close time.
  - Keep the existing release-on-disconnect and release-on-switch-to-read paths.
- Stop treating the lock as persistent state: it is server-session-only. The tree round-trip
  continues to carry it (it is the sync channel that drives the contended-editor affordance on
  other clients), but it is documented and treated as transient — never authoritative across a
  block load.
- Introduce a distinct, persisted **per-lectern access mode** (`Public` default; a `Private` /
  read-only mode reserved) as a *dormant* mechanism: the field is defined, persisted, and synced
  server-authoritatively, but there is **no player-facing control** to change it in this release
  (mirrors the existing "reserved, not wired" `HistoryEventKind.LoreDiscovery` precedent). This
  preserves the sticky-ownership behavior for a future change to expose, cleanly separated from
  the transient lock.

## Capabilities

### New Capabilities
- `lectern-access-mode`: a persisted, server-authoritative per-lectern access mode (Public
  default; Private/read-only reserved and not yet player-settable) that governs who may edit a
  lectern's document, distinct from the transient editor lock.

### Modified Capabilities
- `lectern-gui-shell`: the single-editor lock is clarified to be transient/server-session-only —
  reliably released when the holder leaves the editor by ANY path (close, switch-to-read,
  disconnect) and cleared on block load — so it can never become a permanent lockout that
  survives the holder leaving or a second player's relog.

## Impact

- `src/Mod/BlockEntityScribeLectern.cs` — clear `lockHolderUid` on `Initialize` (block load);
  keep `ReleaseLock` on disconnect; add the new persisted `accessMode` field (Public/Private) to
  the tree round-trip and its client mirror. Optionally add temporary server-side logging on
  grant/release/resolve to confirm the exact leaking trigger during the two-client repro.
- `src/Mod/ScribeDialogBase.cs` — release the lock on every `OnGuiClosed`, not only inside the
  `isEditorMode` teardown branch, so a close that isn't in editor mode still frees a lock the
  server granted this player.
- `src/Mod/ScribeModSystem.cs` — release/request-access handlers unchanged in contract; no new
  packet types for the transient-lock fix.
- World saves: the `lockHolder` tree key remains written for the sync channel but is ignored as
  authoritative on load (cleared). A new `accessMode` key is written (defaulting to Public, so
  existing lecterns behave identically).
