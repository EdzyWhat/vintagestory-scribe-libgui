## Context

`BlockEntityScribeLectern` guards its editor with a single-editor lock so two players can't
type into the same document at once (a crash-prevention guard). The lock is modelled as two
fields:

- `lockHolderUid` — server-authoritative; the UID of the player currently in the editor, or null.
  Written only by grant (`RequestAccess`) and release (`ReleaseLock`).
- `syncedLockHolderUid` — a client-side mirror, used by `IsLockedByOther(viewerUid)` to render
  the "switch to editor" affordance as unavailable when a *different* player holds the lock.

The lock holder is written into the block-entity tree in `ToTreeAttributes` and read back in
`FromTreeAttributes`. Verified against `VintagestoryLib.dll`: `FromTreeAttributes` assigns the
incoming value **only to `syncedLockHolderUid` (the client mirror)** — it never sets the server's
`lockHolderUid`. The engine calls `Initialize` on every disk-loaded block entity at chunk load
(where `lockHolderUid` is null), and the server rebuilds each client packet **live** from
`ToTreeAttributes` (`lockHolderUid ?? ""`). So the tree value only ever reaches the *client mirror*;
the server's authoritative holder is null after any fresh load. Consequently a full **server
restart already self-heals** the lock.

The bug: the server's in-memory `lockHolderUid` is not reliably cleared when the holder leaves the
editor. Release is driven only from specific client paths — the editor-mode close/switch-to-read
(`SendReleaseLockPacket` inside the `isEditorMode` branches) and `OnPlayerDisconnect`. If none of
those fire (or the release packet's DocId doesn't resolve to the block via `TryResolveHost`), the
holder stays set for the whole lifetime of the loaded block. A relog re-syncs the still-set holder
back into every client's mirror, so player 2 stays locked out until the block is unloaded/reloaded
or the server restarts. This is precisely the "lock leak on disconnect/crash → stays locked forever"
risk that the original lock change (archived `2026-07-28-fix-multiplayer-editor-lock`) listed as an
unconfirmed risk and open question and never verified.

Separately, the author wants to *keep* this sticky "one owner" behavior — but as an explicit,
opt-in **private / read-only** permission on a lectern, not as the accidental default and not
conflated with the crash-prevention lock.

## Goals / Non-Goals

**Goals:**
- The single-editor lock can never outlive the holder's editing session: it is released when the
  holder leaves the editor by ANY path (dialog close, switch-to-read/other tab, disconnect) and is
  cleared on block load. A second player is blocked only while the first is *actively* editing.
- The contended-editor affordance (`IsLockedByOther`) keeps working over live sync — a second
  player still sees "switch to editor" as unavailable *while* the first is actively editing.
- Preserve the sticky-ownership behavior as a dormant, persisted per-lectern `AccessMode`
  (`Public` default, `Private` reserved), plumbed and synced but with no player-facing control yet.

**Non-Goals:**
- No player-facing UI, command, or block interaction to set `Private` in this release.
- No lock heartbeat / keep-alive timer. Release is event-driven (load, disconnect, close, switch).
- No changes to the document, guestbook, or history persistence.
- No new network packet types for the transient-lock fix.

## Decisions

### Decision: Clear the lock on load (defence-in-depth against a stale holder)
On block-entity `Initialize` (server side), reset `lockHolderUid = null`. Independent of how the
holder leaked, a freshly-loaded block must start editable. This alone doesn't fix a leak within a
single load session (the block stays loaded across a relog while its chunk is active), so it is
paired with the release-on-every-close fix below rather than relied on by itself.

*Alternative considered — rely on the existing restart self-heal.* Rejected: the user's symptom is
a relog, not a restart, and the block stays loaded across a relog, so restart self-heal doesn't help.

### Decision: Release the lock on every dialog close, not only in editor mode
Today `OnGuiClosed` only calls `SendReleaseLockPacket()` inside `if (isEditorMode)`. If the client
left editor mode (e.g. switched to read/pins/history) but the server still records this player as the
holder — or any close path where `isEditorMode` is false at close time — the release is skipped and
the server holder leaks. Move the release send so it fires on every close for this dialog's lectern.
`ReleaseLock` is already idempotent and UID-guarded (`if (lockHolderUid == playerUid)`), so an
unnecessary release is a harmless no-op; a missing one is the bug.

*Alternative considered — audit and fix only the one path that leaks.* Rejected: three candidate
leak points (close-not-in-editor, DocId-resolution no-op, disconnect timing) are hard to disambiguate
without a live repro, and an always-release-on-close is correct for all of them at negligible cost.

### Decision: Keep the tree sync of the lock, treat it as transient
`ToTreeAttributes`/`FromTreeAttributes` continue to carry `lockHolder` — it is the only channel that
drives the contended-editor affordance on other clients, so it must stay. What changes is intent and
documentation: it is transient session state synced for the affordance, never authoritative across a
load (the clear-on-load enforces that). Removing it would regress the "affordance reflects the held
lock" requirement.

### Decision: AccessMode is a separate persisted field, dormant this release
Introduce a `LecternAccessMode` enum (`Public` default, `Private` reserved) persisted in the tree
under its own key and mirrored on the client — genuinely durable state, unlike the lock. It is
defined, serialized, and synced, but nothing in this release sets it away from `Public`, mirroring
the existing "reserved, not wired" precedent (`HistoryEventKind.LoreDiscovery`). The editor-entry
gate is NOT changed to read it, so a `Public`-only world behaves identically to today.

### Decision: Temporary server-side diagnostics for the repro
Add a temporary, low-noise server-side log line on grant / release / release-resolution-miss so the
two-client repro can confirm which trigger actually leaked (and prove the fix closes it). Removed
before the change is archived; called out as a task so it isn't left in.

## Risks / Trade-offs

- **The lock value is still physically written to disk (just ignored on load).** → Acceptable: it is
  harmless because load always clears it, and this preserves the single sync channel the affordance
  needs.
- **An unnecessary release-on-close could free a lock another view still "wants."** → No: a dialog
  close ends this player's session entirely; there is no in-dialog state that needs to retain the
  lock past close. `ReleaseLock` is UID-guarded so it only frees this player's own hold.
- **Reserved `Private` mode accidentally gating editing.** → Mitigation: wired for persistence/sync
  only; the editor-entry gate reads the lock mirror alone, so `Public`-only worlds are unchanged.
- **Existing affected saves / live worlds.** → Self-healing: on next block load the holder is cleared
  and the lectern is editable again; no migration step or data rewrite.

## Migration Plan

No explicit migration. On the first load after this change each lectern's holder starts null and the
new `accessMode` key defaults to `Public` when absent, so pre-existing lecterns behave exactly as
before. Rollback is reverting the code — the extra `accessMode` tree key is ignored by the prior
version.

## Open Questions

- Which trigger actually leaked in the field (close-not-in-editor vs. DocId-resolution miss vs.
  disconnect timing)? Resolved by the temporary diagnostics + two-client repro; the always-release
  fix closes all three regardless.
- The eventual player-facing surface for `Private` mode (block-interaction toggle vs. dialog control
  vs. command, owner-only vs. group-based) is deferred to the future change that exposes it.
