## Why

The lectern's single-editor lock is server-authoritative and already refuses a second editor
(returning `granted: false` with a `scribe-gui-locked` reason and a native `TriggerIngameError`),
yet a multiplayer playtest (submissions 2026-07-28T07-15-37 and 07-33-43) found the lock does not
behave as a player expects: player 2 can still open the edit view and appear to type, but the
editor reverts their edits within a few frames — and this happens **both** when player 1 holds the
edit view **and when no other player is editing at all**. The result is an editor that silently
eats a second player's work with no explanation, which is worse than a clean refusal. This is a v1
ship blocker (tracked as `2a105a38` in TESTING.md / v1-release-checklist §11.1).

## What Changes

- **Diagnose the two symptoms first (they may share one root cause):** (a) why player 2 can enter
  the edit view and type despite the server lock, and (b) why an editing player's edits revert even
  when the lock is free (nobody else editing). Symptom (b) suggests the client is entering the
  editor before the lock is actually granted, or the granted-lock state is not being retained, so
  the server's `ApplyEdit` lock check (`fromPlayer.PlayerUID != lockHolderUid`) rejects the autosave.
- **Gate editor entry on a held lock, client-side.** Player 2's "switch to editor" affordance SHALL
  be visibly disabled/inert while another player holds the lock, and activating it SHALL NOT open
  the edit view — the player stays in read view. Any player only enters the editor after the server
  has actually granted the lock (no optimistic pre-grant entry).
- **Surface clear feedback when edit access is refused,** via Vintage Story's native in-game error
  (the existing `TriggerIngameError` path), with player-facing copy along the lines of "Another
  player is making edits."
- Ensure the fix is verifiable: retest `2a105a38` in a clean two-client session (player 2 blocked
  while player 1 edits; player 1's own edits persist normally when nobody contends the lock).

## Capabilities

### New Capabilities
<!-- None — this fixes behavior already owned by an existing capability. -->

### Modified Capabilities
- `lectern-gui-shell`: the single-editor-lock acquire/refuse flow gains an explicit
  requirement that editor entry is gated on an actually-granted lock (no entering the edit view on
  refusal or before grant), that a contending player's edit affordance reflects the unavailable
  lock, and that refusal surfaces native in-game feedback.

## Impact

- **Code:** `src/Mod/GuiDialogScribeLecternLibGui.cs` (editor-entry flow: `RequestEditorAccess`,
  the granted-reply handler that calls `EnterEditorMode`, and the read-view "switch to editor"
  affordance state); `src/Mod/BlockEntityScribeLectern.cs` (server lock decision `RequestAccess` /
  `ApplyEdit` and the client `HandleServerReply` / `TriggerIngameError` refusal path);
  `src/Mod/ScribeEditDocumentMessage.cs` (the request/reply carrying `Granted` / `EditorMode` /
  `RefusalReason`) if the reply needs additional lock state.
- **Lang:** a refusal-copy string in `assets/scribe/lang/en.json` (reuse/adjust `scribe-gui-locked`).
- **Core (`src/Core/`):** none expected — this is adapter/network/GUI behavior, not document model.
- **Testing:** manual two-client verification (`2a105a38`); no cloud-CI coverage (no game install on
  runners). Multiplayer needs the second-machine setup already described in v1-release-checklist §2.
- **No new dependencies.**
