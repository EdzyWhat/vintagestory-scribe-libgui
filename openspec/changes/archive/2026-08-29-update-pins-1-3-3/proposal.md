## Why

Pinning a task always appends it to the end of the pin list, with no way for a player to match the
same Top/Bottom preference they already get for new tasks (`new-task-insert-position`). Separately,
a pin added while the game is paused (e.g. singleplayer auto-pause while the Handbook is open)
completes but gives the player no visible confirmation until they unpause — leaving them unsure
whether the pin action actually registered.

## What Changes

- Add a new **Pin Insert** player setting (Top/Bottom, defaulting to Bottom — matching today's
  always-append behavior so existing players see no change until they opt in), distinct from **New
  Task Insert**. A newly pinned task with no pinned-parent relationship inserts at the list's Top or
  Bottom per this setting.
- A pinned subtask whose parent is currently pinned continues to insert directly under that parent's
  cluster, unaffected by the Top/Bottom setting (unchanged existing behavior — a subtask never jumps
  to the Top/Bottom of the whole list on its own).
- No new logic needed for re-parenting: when a parent is pinned after one of its subtasks is already
  pinned elsewhere in the list, the subtask already moves to sit under the parent's new cluster —
  `ScribePinOrdering.PlaceNewPin`'s `GatherOwnedRunChildren` already does this today, and it's already
  covered by `PlaceNewPin_PinningParent_GathersChildrenPreservingRelativeOrder` in
  `ScribePinOrderingTests.cs`. Called out here only so the Top/Bottom work below doesn't regress it.
- The pinned-task HUD SHALL visibly reflect a pin arriving (or any other pin-set change) even while
  the game is paused, with no dependence on the player unpausing or closing whatever paused the game.
  The concrete fix is deferred to design.md pending an in-game trace (see Impact) — this proposal
  commits to the observable outcome, not a specific mechanism yet.

## Capabilities

### New Capabilities
- `pin-insert-position`: the player-facing Pin Insert (Top/Bottom) setting and how it governs where an
  unrelated (no pinned-parent) new pin lands in the pin list. Mirrors the shape of the existing
  `new-task-insert-position` capability but is a distinct setting scoped to pinning, not task creation.

### Modified Capabilities
- `player-pins`: the "Pinning a subtask inserts it under its pinned parent" and "Pinning a parent
  gathers its already-pinned children" requirements currently say an unrelated pin "SHALL be
  appended" — that language changes to "SHALL insert per the player's Pin Insert setting" for the
  no-pinned-parent case. The pinned-parent-cluster placement and the already-pinned-children
  gathering behavior are unaffected.
- `pinned-task-hud`: the "The HUD refreshes when the pin set changes" requirement gains an explicit
  guarantee that the refresh is not deferred by the game being paused.

## Impact

- `src/Core/ScribePinOrdering.cs` (`PlaceNewPin`) — every append-on-no-relation branch takes the
  Top/Bottom choice instead of unconditionally appending.
- `src/Core/ScribeNewTaskInsert.cs` / `src/Core/ScribePlayerSettings.cs` — new sibling enum
  `ScribePinInsert` (Top/Bottom) and `ScribePlayerSettings.PinInsert` property + normalize fallback,
  mirroring the existing `NewTaskInsert` pattern.
- `src/Mod/ScribeSettingsContent.cs` — new dropdown next to the existing New Task Insert control.
- `src/Mod/ScribeDialogBase.cs` (`SendSetPin`/pin-add call sites) — pass the player's `PinInsert`
  setting through to `PlaceNewPin`.
- `src/Mod/HudScribePins.cs` and/or the pin-push handling in `ScribeModSystem.ClientPrefs.cs` — the
  pause-visibility fix, exact location pending the trace step in design.md/tasks.md. Investigation
  already ruled out the obvious suspects: `RegisterGameTickListener` callbacks do stop firing while
  `IsGamePaused` (confirmed via decompiling `ClientMain.MainRenderLoop` in `VintagestoryLib.dll` —
  `TriggerGameTick` only runs `if (!IsPaused)`), but the actual pin-arrival path
  (`SendSetPin` → server → `MyPinsChanged` → `OnMyPinsChanged` → `RebuildHudBody`) is event-driven,
  not tick-driven, and render (`TriggerRenderStage`, and therefore LibGUI's own animation ticker) runs
  unconditionally regardless of pause. So the blocker, if any, is somewhere less obvious and needs an
  in-game trace before committing to a fix.
- Tests: `tests/Core.Tests/ScribePinOrderingTests.cs` already exists (covers `ForDisplay` and the
  current append/cluster behavior) — extend it with Top/Bottom placement cases; the existing
  re-parenting test should keep passing unchanged.
