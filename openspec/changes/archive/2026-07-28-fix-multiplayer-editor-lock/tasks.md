## 1. Diagnose the root cause (do this before changing behavior)

- [x] 1.1 Audit every path that puts the dialog into editor mode: find all callers of
      `dialog.EnterEditorMode(...)` and any other editor-mode entry, and map which are reached with a
      confirmed `Granted && EditorMode` reply vs. which enter optimistically (before/without the grant).
      Candidate paths: read-view "switch to editor", right-column nav Edit button (`RequestEditorAccess`),
      Back-from-settings re-request, and any pre-grant entry added for responsiveness.
      - **FINDING (2026-07-28): editor entry is ALREADY fully grant-gated — the design's "optimistic
        entry" hypothesis is DISPROVEN.** Full trace: `isEditorMode` is a private computed property
        (`GuiDialogScribeLecternLibGui.cs:106-110`) set true ONLY inside `EnterEditorMode` (lines 455, 467).
        `EnterEditorMode` has exactly one caller: `BlockEntityScribeLectern.HandleServerReply` (line 503),
        reached only when `message.Granted && message.EditorMode` (the refusal branch at 464 falls to
        `EnterReadMode`). Every "switch to editor" affordance (read-view control :1606, right-col nav
        button :1533) is wired to `RequestEditorAccess`, which ONLY sends the request packet (:596-605) and
        never flips the view. The initial right-click (`BlockScribeLectern.OnBlockInteractStart`) runs
        `OnRightClick`, which no-ops on the client (`Api is not ICoreServerAPI`) — even the first open is
        server-gated. Server lock lifecycle is complete: `lockHolderUid` set on grant (:260), checked in
        `ApplyEdit` (:271), released on close (`ReleaseLock` :405 via `SendReleaseLockPacket`) AND on
        disconnect (`OnPlayerDisconnect` :413). So from static reading BOTH reported symptoms should be
        impossible — which means the real cause is a RUNTIME issue only observable with two clients (e.g. a
        lock-instance mismatch across a chunk reload between request and edit, a reply arriving with the
        wrong `EditorMode`/`Granted`, or the playtest having run on a stale build). Task 3.1 ("remove
        optimistic entry") therefore has NO target. Tasks 1.2/1.3 (two-client repro + server logging) are
        the actual next step and require the second-machine setup. Pausing for a design/tasks revisit
        rather than implementing an unsupported fix.
      - **STALE-BUILD / HOST-BUILD CHECK (2026-07-28, option 3):** This Mac's build was NOT stale — at
        playtest time (07:15–07:33) HEAD was `0e7136d` (00:44 Jul 28) and the lock code last changed
        `91fb72c` (Jul 18, server lock) / `a7ad139` (Jul 23, `HandleServerReply`), both far older than the
        staged DLL, so this client had current lock code. BUT the tester noted this Mac was the JOINING
        player, not the host. The lock is server-authoritative: `RequestAccess`/`ApplyEdit`/`ReleaseLock`/
        `lockHolderUid`/`OnPlayerDisconnect` all gate on `Api is ICoreServerAPI` and run on the HOST, and
        `modinfo.json` has `requiredOnServer: true`. "Types then reverts" is exactly `ApplyEdit` returning
        false (lock check) → `SendSaveFailedAck`, a decision made ENTIRELY host-side. So the build that
        governed the observed behavior was the HOST's, not this Mac's — and if the host ran an older/
        mismatched `Scribe.dll`, its lock logic would reject this client's edits regardless of this Mac's
        (correct) code. STRONG candidate for symptom (b). Not confirmable without the host's build/commit.
        NEXT (for the real fix): confirm the host was on the same commit as the joining client before
        trusting the two-client repro; a version-skew repro is not a code bug in this tree.
- [x] 1.2 Reproduce both symptoms on the two-client setup and confirm the mechanism: (a) player 2 enters
      + types while player 1 holds the lock; (b) an editing player's edits revert when the lock is FREE
      (no contention). Correlate (b) with `ApplyEdit` returning `false` (server lock check) by logging
      `lockHolderUid` vs. the autosave sender on the server.
      - **RESOLVED 2026-07-28 (matched-build two-client session).** Neither symptom reproduced once the host
        and joining client ran the SAME build (playtest submission 2026-07-28T09-01-45). This confirms the
        1.1 diagnosis: the original "types then reverts" report (07-15-37/07-33-43) was a host-build mismatch
        (the Mac was the joining player; the host governed the server-authoritative lock), not a defect in
        this tree. No server logging needed — the defensive UX (2.x/4.x) both fixed the affordance and made
        the mismatch moot.
- [x] 1.3 Confirm whether the lock reliably releases on editor-close AND on an ungraceful disconnect
      (crash/timeout), not just a clean close — note the result (drives whether task 3.x needs a release fix).
      - **Verified by code (2026-07-28):** clean close/leave → `OnClickSwitchToRead`/`LeaveEditorMode` →
        `SendReleaseLockPacket` → server `ReleaseLock`. Disconnect → `sapi.Event.PlayerDisconnect +=
        OnPlayerDisconnect` (registered in Initialize, line ~92) → `ReleaseLock(player.PlayerUID)`. Both paths
        clear `lockHolderUid`. Release path is present and correct; no release fix needed in §3.

## 2. Server: sync lock state to clients

- [x] 2.1 Sync the editor-lock state through the block-entity tree (`ToTreeAttributes` /
      `FromTreeAttributes`), following the vanilla Sign pattern already used for the document. Prefer a
      server-derived "locked by another" signal (or the `lockHolderUid`) sufficient for the client to
      decide affordance state; `MarkDirty` on lock acquire/release so clients resync.
      - **Done (2026-07-28):** `ToTreeAttributes` writes `lockHolder` (the `lockHolderUid`, "" = free);
        `FromTreeAttributes` reads it into a client-side `syncedLockHolderUid`. New `IsLockedByOther(viewerUid)`
        helper on the block entity. `MarkDirty()` added on lock acquire (`RequestAccess`) and release
        (`ReleaseLock`) so the state rides the next block-entity packet to all clients.
- [x] 2.2 Verify the lock releases (and re-syncs) when the holder leaves the editor and on disconnect,
      so the synced state can never be permanently stuck locked. Add the release path if 1.3 found it missing.
      - **Done (2026-07-28):** release path confirmed present (1.3); added `MarkDirty()` inside `ReleaseLock`
        so BOTH the clean-close and disconnect release re-sync the freed lock to other clients.

## 3. Client: gate editor entry on a granted lock

- [x] 3.1 Remove any optimistic/pre-grant editor entry found in 1.1: the dialog enters the editor view
      ONLY on a `HandleServerReply` with `Granted && EditorMode`. "Switch to editor" (read-view control and
      nav button) requests the lock and STAYS in read view until the grant arrives, then swaps.
      - **No change needed (2026-07-28):** 1.1 proved entry is ALREADY grant-gated (only `EnterEditorMode`,
        only from `HandleServerReply` on `Granted && EditorMode`; affordances only send the request). There
        was no optimistic entry to remove. Requirement already satisfied by the existing code.
- [x] 3.2 Confirm the refusal path in `HandleServerReply` still falls back to `EnterReadMode()` and never
      leaves the player in an editor whose edits are silently discarded.
      - **Verified (2026-07-28):** `HandleServerReply` refusal branch (`!message.Granted`) fires the native
        error then `EnterReadMode()` (both the not-open and already-open sub-paths), except the save-failed
        recovery which re-requests. Confirmed correct; unchanged.

## 4. Client: contended-affordance UX + feedback

- [x] 4.1 Disable/inert the read-view "switch to editor" affordance when the synced lock state shows the
      lock is held by another player (compare against `capi.World.Player.PlayerUID`); activating it does
      NOT request or enter the editor.
      - **Done (2026-07-28):** new `TryEnterEditor()` gate — both affordances (read-view footer
        `onSwitchToEditor` + right-col Edit nav button) now route through it instead of raw
        `RequestEditorAccess`. When `IsLockedByOther`, it fires the native error and returns WITHOUT
        requesting/entering. The Edit nav glyph also dims (alpha 0.4) in that state so it reads as
        unavailable. `RequestEditorAccess` stays ungated for the lost-lock recovery re-acquire.
- [x] 4.2 Ensure a refused request (the race backstop) surfaces the native in-game error via
      `capi.TriggerIngameError`, and reword/point the refusal lang string so the copy reads "Another player
      is making edits." (adjust `scribe-gui-locked` in `assets/scribe/lang/en.json`).
      - **Done (2026-07-28):** `scribe-gui-locked` reworded from "Only one person can use the lectern at a
        time." → "Another player is making edits." Fired by both the server-refusal `HandleServerReply` path
        (existing backstop) and the new client-side `TryEnterEditor` pre-gate.

## 5. Build, stage, and verify

- [x] 5.1 Build Debug (`dotnet build src/Mod/Mod.csproj -c Debug`) with 0 warnings/errors; run
      `dotnet test` (Core suite) green.
      - **Done (2026-07-28):** build 0W/0E; Core suite 140/140 passed.
- [x] 5.2 Restage (`bash build/restage.sh Debug`) and fully relaunch BOTH clients.
      - **Done (2026-07-28).** Restaged and both clients relaunched on the matched build; the 09-01-45
        session ran with host and joining client on the same commit (the prerequisite from 1.1/1.2).
- [x] 5.3 Two-client retest of `2a105a38` (record verdict in TESTING.md):
      (a) player 1 editing → player 2's "switch to editor" is inert and does not enter the editor; player 2
      sees the "Another player is making edits." native error;
      (b) player 1 leaves/disconnects → lock releases → player 2 can now enter the editor;
      (c) SOLE editor (no contention) → edits persist with NO revert.
      - **Confirmed 2026-07-28** (playtest submission 2026-07-28T09-01-45, two-client MP session on MATCHED
        builds — this was the missing prerequisite from 1.2). Tester: "Works. Surface visual feedback, and
        player 2 can't activate the view, and it's greyed out." The defensive UX holds: P2's affordance is
        inert/greyed while P1 holds the lock, refusal surfaces feedback, edits are no longer silently scrubbed.
        The original "types then reverts" symptom did NOT recur once both machines ran the same build —
        confirming the 1.1/1.2 host-build-mismatch diagnosis for the original report.
- [x] 5.4 Update TESTING.md `2a105a38` with the verdict and check off v1-release-checklist §11.1
      (this change supersedes that inline task).
      - **Done 2026-07-28:** TESTING.md `2a105a38` marked Confirmed; v1-release-checklist §11.1 checked off.
