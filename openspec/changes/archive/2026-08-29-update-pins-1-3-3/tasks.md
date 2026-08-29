## 1. Pin Insert setting (Core)

- [x] 1.1 Add `ScribePinInsert : byte { Top, Bottom }` in `src/Core/`, mirroring
      `src/Core/ScribeNewTaskInsert.cs`'s shape.
- [x] 1.2 Add `ScribePlayerSettings.PinInsert` (default `Bottom`) and a
      `NormalizePinInsert(ScribePinInsert)` fallback (defaulting to `Bottom` for an unknown value),
      mirroring `NewTaskInsert`/`NormalizeNewTaskInsert` in `src/Core/ScribePlayerSettings.cs`; wire it
      into `Normalized()`.
- [x] 1.3 Add `ScribePlayerSettingsTests` cases mirroring the existing `NewTaskInsert` default/normalize
      tests, but asserting the `Bottom` default (not `Top`).

## 2. Pin Insert placement (Core)

- [x] 2.1 Change `ScribePinOrdering.PlaceNewPin`'s signature to take a `ScribePinInsert insertEdge`
      parameter.
- [x] 2.2 Update the no-relation append branches (null source; unresolvable task index; depth-0 pin;
      depth-1 pin whose parent is not pinned) to insert at index 0 when `insertEdge == Top`, append when
      `Bottom` — per design.md's Open Question, apply this consistently to ALL no-relation branches,
      including the null-source/unresolvable-index edge cases.
- [x] 2.3 Leave the depth-1-with-pinned-parent branch (insert after the parent's owned-run cluster) and
      `GatherOwnedRunChildren` untouched — confirm via the existing
      `PlaceNewPin_PinningParent_GathersChildrenPreservingRelativeOrder` and
      `PlaceNewPin_ChildUnderPinnedParent_InsertsAfterCluster` tests still pass unmodified.
- [x] 2.4 Add new `ScribePinOrderingTests` cases: Top vs. Bottom for an unrelated depth-0 pin, Top vs.
      Bottom for a depth-1 pin whose parent is not pinned, and a null-source case for both edges
      (replacing/extending `PlaceNewPin_NullSource_Appends`).

## 3. Pin Insert setting (Mod)

- [x] 3.1 Update `PlaceNewPin` call site(s) in `src/Mod/ScribeDialogBase.cs` (near the existing
      `SendSetPin` pin-add path) to pass `modSystem.MySettings.PinInsert`.
- [x] 3.2 Add a **Pin Insert** dropdown to `src/Mod/ScribeSettingsContent.cs`, next to the existing New
      Task Insert dropdown (~line 145), with its own lang keys (mirror
      `scribe:scribe-newtaskinsert-top`/`-bottom`), writing through `onMutate(s => s.PinInsert = v)`.
- [x] 3.3 Add the new lang keys to `assets/scribe/lang/en.json` (or wherever the New Task Insert keys
      live).
- [x] 3.4 `dotnet build src/Mod/Mod.csproj -c Debug` to confirm the settings form compiles.

## 4. Pause-visibility investigation (trace-first, per design.md D3)

- [x] 4.1 Add temporary trace logging at: `ScribeModSystem` pin-push receipt, `MyPinsChanged` invoke,
      `HudScribePins.OnMyPinsChanged`, and `RebuildHudBody()` — mirroring the project's `TraceHudRows`
      precedent from the earlier HUD stale-controller bug.
- [x] 4.2 Restage (`build/restage.sh Debug`, client fully quit first) and reproduce in-game: pause via
      singleplayer Handbook auto-pause, pin a task from an already-open Notebook/Lectern, observe
      whether/when the HUD updates. (Player did this directly: "pinned/unpinned a bunch while paused.")
- [x] 4.3 Read the trace output (`build/scribe-log.sh` or equivalent) to find the actual stall point, if
      any — do not assume the `TrackerCount`-style `IsGamePaused` fix applies without trace evidence.
      Read `client-main.log`/`server-main.log` directly: every `[scribe-pin-push]`/`[scribe-hud-rebuild]`
      line during the session logged `paused=False`, and cross-referencing against `Client pause state is
      now on/off` + the server's `[scribe] set-pin received` lines showed why — nothing arrived server-side
      at all during a pause window; every queued `set-pin` landed in a burst the instant the client
      unpaused.
- [x] 4.4 Report the trace finding before writing any fix. Finding: singleplayer's embedded server halts
      its own tick/dispatch while the client is paused, so `ScribePinStore.SetPin` doesn't run until
      unpause regardless of when the packet was sent — not a HUD-rebuild-path problem at all. This is the
      exact reason `ScribeDialogBase.optimisticPin` already existed (add-tracker-link-tasks 7.11b): that
      overlay covers the editor's own rows but not the HUD or Pin Tab, which is exactly the gap the player
      hit. See design.md D3's resolution.

## 5. Pause-visibility fix

- [x] 5.1 Generalize the existing `ScribeDialogBase.optimisticPin` pattern (D3) instead of an
      `IsGamePaused` special-case: moved the optimistic pin/unpin overlay up to `ScribeModSystem`, applied
      inside `MyPins` (the one list the HUD, Pin Tab, and every dialog's row tint all read), keyed by
      (DocId, TaskId). `SendSetPin` (`ScribeDialogBase.Editor.cs`) and the Pin Tab's standalone unpin
      (`OnPinUnpinTask`) record the optimistic entry before their packet goes out; an optimistic ADD is
      placed via `ScribePinOrdering.PlaceNewPin` (the same function the server calls) so pinned-parent
      clustering is correct even before either pin is confirmed. Reconciled against the authoritative push
      in `OnClientReceivedPinnedSet`. Deleted the now-redundant dialog-local `optimisticPin` dict and
      `RepaintPinsOptimistically`; `ScribeDialogBase.IsPinnedForMe` delegates straight to the overlay-aware
      `ScribeModSystem.IsPinnedForMe`. `dotnet build` clean, `dotnet test tests/Core.Tests` 536/536.
- [x] 5.2 Re-run the Group 4.2 repro to confirm the HUD now updates visibly while paused. **Pending the
      player's next in-game session** (this fix landed after their first repro pass) — no further static
      verification is meaningful here; needs a live re-test.
- [x] 5.3 Add/update the `pinned-task-hud` spec's "A pin added while paused appears immediately" scenario
      coverage. The scenario already drafted in `specs/pinned-task-hud/spec.md` describes exactly the
      shipped behavior as written — no wording change needed. No automated HUD test harness exists for
      this surface, so verification is the manual repro in 5.2/6.3.

## 6. Wrap-up

- [x] 6.1 Full build (`dotnet build` for both Core and Mod) and `dotnet test` for `tests/Core.Tests`.
- [x] 6.2 Update `CHANGELOG.md` per this project's v1.3.3 release convention.
- [x] 6.3 Playtested both halves together: pin ordering with Top and with Bottom (confirmed via
      `animate-row-reposition`'s HUD/Pin Tab reposition testing), the pinned-parent-later-pinned
      re-parenting sanity check (confirmed), and the pause scenario via the optimistic-overlay fix
      (confirmed — HUD/Pin Tab show a pin/unpin instantly while paused).
