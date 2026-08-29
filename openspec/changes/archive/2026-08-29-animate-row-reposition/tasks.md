## 1. Spike: measuring a row's real post-layout displacement (design.md D2)

- [x] 1.1 Prototyped attaching a per-id measurement wrapper. Landed on a different (simpler) primitive
      than the originally-planned `GlobalKey` + live `RenderObject.LocalToGlobal` read: a transparent
      `ScribeSizeReportWidget`/`ScribeSizeReportRender` (a `RenderProxyBox` subclass that calls
      `base.PerformLayout()` — already a full pass-through — then reports the resulting `Size`), wrapped
      around every row the container renders.
- [x] 1.2 Confirmed (by decompiling `Gui.dll`'s `GuiBase.ForceRebuild()`, and reading the mod's actual
      call sites) that a survivor's new Y is never needed as a live same-frame read at all: both the old
      and new cumulative Y are computable ANALYTICALLY from each row's last-known height (a value from
      *before* this frame's layout, which is exactly what's available during `Build()`). See design.md's
      D2 for the resolved mechanism and D2b for the `ForceRebuild`-vs-`RebuildBody` finding that confirms
      the height cache survives across ordinary row mutations.
- [x] 1.3 No one-frame lag needed: the mechanism seeds and self-corrects within the same build cycle
      (see design.md D2's entering-row edge case). This is a strict improvement over the originally
      considered fallback, not just a tie.

## 2. Core mechanism: reposition animation in `ScribeAnimatedList`

- [x] 2.1 `ScribeAnimatedListState.Build` now computes, alongside the existing departure/appearance
      diff, each survivor's cumulative-Y delta from `prevRenderOrder` vs. `diff.RenderOrder` using the
      `knownHeight` cache (step "4c" in `ScribeAnimatedList.cs`).
- [x] 2.2 Added a `move:<id>:<generation>` registry key namespace (`ScribeAnimatedListState.MoveKey`),
      distinct from the collapse key and `enter:<id>`.
- [x] 2.3 Added `ScribeRowReposition`/`ScribeRowRepositionState` (`ScribeRowSizeAnimation.cs`): a
      `StatefulWidget` that obtains its `AnimationController` from the registry by the generation-keyed
      id, runs 0→1 over `ScribeRowSizeAnimation.DefaultDurationMs`/`Curves.EaseInOutCubic`, and paints a
      `Transform.Translate` from the (every-build-recomputed) target offset to zero.
- [x] 2.4 An entering row is never also wrapped for reposition — `survivorTargetOffset` only considers
      ids present in `prevLiveIds` (excludes freshly-appeared ids by construction), and the materialize
      step checks `entering.Contains(id)` before checking `repositionGeneration`.
- [x] 2.5 Reposition controllers are released on departure (`ScribeAnimatedListState`'s existing
      departure loop now also does `repositionGeneration.Remove(dep.Id, out var gen)` +
      `Registry.Release(MoveKey(dep.Id, gen))`), mirroring the entry-controller release discipline.
- [x] 2.6 Same-frame entering-and-repositioning collision is structurally impossible (2.4), so no
      separate manual repro was needed beyond confirming the code path.

## 2b. Playtest-found bug: a once-entered row could never reposition again

- [x] 2b.1 Player-reported: a brand-new task never reposition-animates on later moves, but the SAME
      task does once the dialog is closed/reopened or the tab is switched away and back. Root cause:
      `entering` never clears for a row's whole live lifetime, but step 5's original branch treated
      `entering`-membership and reposition-eligibility as mutually exclusive (`if/else if`) — so any
      row that ever entered (every brand-new task) was permanently locked out of the reposition branch
      for the rest of that mounted session. See design.md D2a.
- [x] 2b.2 Fixed: the two wrappers now stack (both applied independently when applicable) instead of
      being mutually exclusive, so a settled (or still-animating) entry wrapper no longer blocks a
      later reposition wrapper. Updated the `animated-task-list` spec delta's scenario wording to match
      (exclusion is per-build, not per-row-forever).
- [x] 2b.3 `dotnet build src/Mod/Mod.csproj -c Debug`: 0 warnings/errors after the fix.

## 2c. Playtest-found bug: siblings jump specifically when a NEW task causes the shift

- [x] 2c.1 Player-reported: newly created tasks still jump; other tasks now jump specifically WHEN a
      new task is created (a regression of the exact behavior this change targets), while non-insertion
      repositions worked. Root cause #1: `PrefixY` defaulted a missing height to `0f`; for a single
      fresh insertion, the entering row's own (not-yet-measured) height is the ONLY thing separating a
      survivor's old/new position, so the computed delta was exactly zero and got epsilon-filtered —
      never even started. Root cause #2 (a related fragility): the original "recompute every build"
      design let ANY unrelated container rebuild mid-animation reset the target to ~0 and snap the row
      to rest early. See design.md D2c.
- [x] 2c.2 Fixed #1: `PrefixY` now falls back to the smallest currently-known row height instead of
      zero for a not-yet-measured id.
- [x] 2c.3 Fixed #2: `ScribeRowReposition` now seeds `TargetOffsetY` once (on `InitState`/generation
      change) into a private field, instead of re-reading the container's per-build recomputation —
      mirroring `ScribeSlideIn`'s fixed `SlideDistance`. Immune to how often the container rebuilds
      while the motion eases.
- [x] 2c.4 `dotnet build` (Core + Mod) and `dotnet test tests/Core.Tests`: 0 warnings/errors, 536/536.

## 3. Surface verification

- [x] 3.1 Manually verified in the Lectern editor: with New Task Insert set to Top, existing rows
      displace smoothly instead of jumping.
- [x] 3.1b Re-verified the exact reported repro (new task creation without a dialog reopen/tab switch
      in between): confirmed fixed by the player.
- [x] 3.2 Manually verified on the Pin Tab and HUD: with Pin Insert set to Top, existing pinned rows
      displace smoothly on both surfaces.
- [x] 3.3 Manually verified a removal-caused shift (delete/unpin with others below it): reads
      correctly, collapse + reposition interplay is correct.
- [x] 3.4 **Bug found in playtest, fix applied 2026-08-29 — pending in-game re-verification (3.4.4).**
      A Sink/UnpinSink completion: the OTHER displaced rows animated correctly, but the SINKING row
      itself instant-jumped to the bottom instead of animating there.
      - [x] 3.4.1 Investigated by decompiling `Gui.dll`'s `MultiChildElement`/`Element`/`Widget.CanUpdate`
            (later found already written up in `VSAPI-NOTES.md` § LibGUI — check there first next time)
            and hand-tracing a concrete Sink event index-by-index through `MultiChildElement.Update`.
            Ruled out: the sunk row's content/key does NOT change at the exact moment its position
            changes (confirmed by tracing the HUD's actual timeline — `SunkVisual` flips at tick-expiry,
            before the server-confirmed reorder); its rebuild path is genuinely `RebuildHudBody`'s
            reconciling `SetState`, never `ForceRebuild`.
      - [x] 3.4.2 Confirmed mechanism (real, but could not prove it uniquely explains the sinking row vs.
            its merely-displaced neighbors — see design.md D2d): LibGUI's reconciler matches Column slots
            by INDEX, not id. Any row whose slot shifts gets its Element unmounted and a fresh one
            mounted (`InitState` reruns). `ScribeRowReposition.seededOffset` was a `State`-local field set
            only in `InitState`/on generation change — correct on the triggering build (which computes a
            real delta), but a LATER remount of the same still-animating row (forced by the positional
            reconciler for any unrelated reason) would reseed from that later build's `TargetOffsetY`,
            which defaults to zero per the materialize-step fallback — snapping the motion to rest. This
            is the same class of bug 2c.3 already fixed for the SetState-reuse case; it did not cover the
            remount case.
      - [x] 3.4.3 Fixed: `seededOffset` now lives in `ScribeAnimationRegistry` (`Seed(id, value)`), keyed
            like the `AnimationController` already is, so it survives a remount exactly the same way the
            controller's elapsed progress does — a re-attach to an already-seeded id gets the ORIGINAL
            value back regardless of how many times or why the Element remounts in between. Released
            alongside the controller in `Registry.Release`. `dotnet build` (Core + Mod): 0 warnings/errors.
            `dotnet test tests/Core.Tests`: 536/536.
      - [x] 3.4.4 Restaged and re-verified the original Sink-completion repro in-game (2026-08-29):
            confirmed fixed by the player.
- [x] 3.5 No surface (`HudScribePins.cs`, `ScribeDialogBase.*`, Pin Tab, Read view) needed any code
      change — confirmed by grep: the only edits were `ScribeAnimatedList.cs` (the container) and
      `ScribeRowSizeAnimation.cs` (the shared animation primitives file). The container-level design
      fully abstracted the motion as intended.

## 4. Wrap-up

- [x] 4.1 `dotnet build` (Core + Mod): 0 warnings/errors. `dotnet test tests/Core.Tests`: 536/536
      (Core untouched by this change, as expected — confirmed no regression).
- [x] 4.2 Updated `CHANGELOG.md`'s `## [1.3.3]` `### Added` section with the new reposition-animation
      behavior.
- [x] 4.3 design.md's D2/D2b updated in place to describe the actual shipped mechanism (the analytical
      height-cache approach), superseding the speculative live-geometry-read plan.
