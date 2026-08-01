## 1. Make lock release robust and self-healing

- [x] 1.1 In `BlockEntityScribeLectern.Initialize` (server side), reset `lockHolderUid = null`
      on load so a freshly-loaded block never starts with a held lock (defence-in-depth). Comment
      it as the clear-on-load leg of the transient-lock guarantee.
- [x] 1.2 In `ScribeDialogBase.OnGuiClosed`, send the release on EVERY close for this dialog's
      lectern, not only inside the `if (isEditorMode)` teardown branch. Keep the editor-only teardown
      (flush, autosave stop, focus-node dispose) where it is; move/duplicate only the
      `SendReleaseLockPacket()` so a close that isn't in editor mode still frees a server-held lock.
      Note that `ReleaseLock` is idempotent + UID-guarded, so an extra release is a harmless no-op.
- [x] 1.3 Confirm the remaining release paths stay intact and event-driven: `OnPlayerDisconnect` →
      `ReleaseLock`, switch-to-read / tab-switch → `SendReleaseLockPacket`, all landing at
      `OnServerReceivedReleaseLock` → `TryResolveHost` → `ReleaseLock`. No heartbeat/timer added.
      Update the `ReleaseLock` / tree-sync comments to state the lock is transient session state.

## 2. Temporary diagnostics for the two-client repro

- [x] 2.1 Add a low-noise, temporary server-side log line (Notification/Debug) on: lock GRANT
      (in `RequestAccess`), lock RELEASE (in `ReleaseLock`, both the cleared and the UID-mismatch
      no-op branches), and a release packet whose DocId did NOT resolve to a lectern
      (`OnServerReceivedReleaseLock` when `TryResolveHost` returns non-lectern/null). Prefix each
      with a grep-able tag (e.g. `[scribe-lock]`). This confirms which trigger leaked in the field.
- [x] 2.2 Track a follow-up to REMOVE the diagnostics before archiving the change (leave a
      `// TODO(fix-transient-lectern-editor-lock): remove diagnostics` marker on each log line).

## 3. Add the dormant per-lectern access mode

- [x] 3.1 Add a `LecternAccessMode` enum (`Public`, `Private`) in `src/Mod/` (no Core seam).
      XML-doc `Private` as reserved / not player-settable this release (mirror the
      `HistoryEventKind.LoreDiscovery` reserved-kind precedent).
- [x] 3.2 Add a server-authoritative `AccessMode` field to `BlockEntityScribeLectern` defaulting to
      `Public`, plus a client mirror (matching the lock's server/synced split). Persist + sync it in
      `ToTreeAttributes` / `FromTreeAttributes` under its own key (e.g. `accessMode`), defaulting to
      `Public` when the key is absent (pre-existing saves).
- [x] 3.3 Do NOT add any player-facing control, command, or block interaction that sets
      `AccessMode` to `Private` in this release. Do NOT read `AccessMode` in the editor-entry gate —
      the gate continues to consult the lock mirror only, so a `Public`-only world behaves identically
      to today.

## 4. Build + Core suite

- [x] 4.1 `dotnet build src/Mod/Mod.csproj -c Release` compiles clean (no new warnings).
- [x] 4.2 `dotnet test tests/Core.Tests` still passes (Core is untouched — confirms no accidental
      Core coupling from the new enum/field).

## 5. In-game verification (multiplayer)

- [x] 5.1 Player 1 opens the editor; while player 1 is still editing, player 2's "switch to editor"
      affordance reads as unavailable and activating it keeps player 2 in the read view with the
      native "another player is editing" notice.
- [x] 5.2 Player 1 switches to read view / closes the dialog; player 2 can now open the editor and
      make + persist edits (lock released on close/switch — including the close-not-in-editor path).
- [x] 5.3 Player 1 holds the editor lock, then disconnects; player 2 can open the editor (lock
      released on disconnect).
- [x] 5.4 Reproduce the original bug shape (P1 opens editor → leaves → P2 relogs) and confirm P2 can
      now edit. Capture the `[scribe-lock]` server log across the sequence to confirm which release
      fired (verifies the leak is closed, not just masked).
- [x] 5.5 Confirm normal single-player editing is unaffected (a sole editor is never spuriously
      refused; edits persist across save/reload).
