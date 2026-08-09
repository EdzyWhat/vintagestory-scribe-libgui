## 1. Registry predicate

- [x] 1.1 Add `bool AnyAnimating` to `ScribeCollapseRegistry` (`src/Mod/ScribeCollapsible.cs`),
      returning true iff any owned `AnimationController.Status != AnimationStatus.Completed` — sibling
      to the existing `IsComplete(id)`.

## 2. Re-hover helper

- [x] 2.1 Add a private helper on the host (shared shape between `ScribeDialogBase` and
      `HudScribePins`) that reconstructs the current window-local cursor position as
      `new Vector2(capi.Input.MouseX, capi.Input.MouseY) / GetUiScale() - WindowPos` and calls
      `EventDispatcher.DispatchPointerMove(RootElement, new PointerEvent(local.X, local.Y))`, guarded on
      a non-null `RootElement`/`RenderObject` (mirror `GuiBase.OnMouseMove`'s guards). Done: pure
      conversion factored into `ScribeHoverRefresh.ToWindowLocal`; each host has its own
      `RefreshHoverAtCursor` calling it.
- [x] 2.2 Decide whether to skip the dispatch when the local point is outside window bounds (Open
      Question in design.md); implement the chosen behavior. Decided: do NOT special-case
      out-of-window — an out-of-bounds dispatch simply hit-tests nothing (or fires a leave), exactly as
      real motion does; the guard is only the null `RootElement`/`RenderObject` check. Revisit only if
      4.6 observes flicker.

## 3. Wire the per-frame trigger

- [x] 3.1 In `ScribeDialogBase.Lifecycle.cs` `OnRenderGUI`, after the existing collapse-cleanup block,
      call the re-hover helper when `editorCollapseRegistry.AnyAnimating` is true.
- [x] 3.2 In `HudScribePins.cs`, wire the same per-frame re-hover against the HUD's own collapse
      registry so HUD unpin is covered.
- [x] 3.3 Confirm empty-row cleanup (which routes through `DeleteEditorBlock`, hence the editor
      registry) is covered by 3.1 with no extra wiring. Confirmed: empty-row removal calls
      `DeleteEditorBlock`, which arms the same `editorCollapseRegistry`, so 3.1 covers it.

## 3b. Generalize to any rebuild (added after first playtest)

- [x] 3b.1 Add `ArmIfRebuilt(object? currentRoot)` to `ScribeHoverRefreshLatch`: arm the linger whenever
      `RootElement` identity changes (the only post-mount change is `GuiBase.ForceRebuild` assigning a
      fresh instance). Catches unpin, new-row, title-edit — every `ForceRebuild` path — with no
      per-call-site code (design Decision 4c).
- [x] 3b.2 Call `hoverRefreshLatch.ArmIfRebuilt(RootElement)` once per frame in both hosts' `OnRenderGUI`;
      drop the now-redundant explicit collapse-cleanup `Arm()` (subsumed by the rebuild detector).

## 4. Verify (manual, in-game)

- [x] 4.1 Restage (`bash build/restage.sh Debug`) so the game loads current DLLs before testing.
- [x] 4.2 Manually test: open the lectern editor with several rows, hover a row's delete button, click
      delete WITHOUT moving the mouse — confirm the delete/pin controls of the row that slides under
      the cursor appear immediately (no wiggle). **Confirmed 2026-08-08.**
- [ ] 4.3 Manually test fluid mass-delete: repeatedly click delete on the row under the stationary
      cursor, faster than the ~200ms collapse — confirm each delete control is available mid-collapse
      and rows delete without any mouse movement. **Backlogged 2026-08-08:** hover works mid-collapse,
      but the CLICK misses until the collapse completes (the departing ghost-snapshot row intercepts the
      hit-test). Click-target problem, not hover; low-value ("90% there"). Backlogged to
      `docs/vnext-ideas.md`; out of scope for this change (see design Non-Goals).
- [x] 4.4 Manually test the HUD unpin path: unpin a pinned task while hovering, without moving the
      mouse — confirm the next row's hover controls refresh. **Fixed via 3b (general rebuild detector);
      awaiting re-test** — unpin isn't collapse-animated, so it needed `ArmIfRebuilt`, not `AnyAnimating`.
- [x] 4.5 Manually test empty-row cleanup: create an empty task row, leave it (blur) so it self-removes
      while the cursor is stationary over the list — confirm no stale-hover artifact. **Confirmed 2026-08-08.**
- [x] 4.6 Regression check: with nothing collapsing, confirm normal hover on real mouse motion is
      unchanged, and that tooltips/press states don't flicker during a collapse. **Confirmed 2026-08-08.**
- [x] 4.7 Add/refresh the corresponding `TESTING.md` items for 4.2–4.6 (via the `what-to-test` skill).

## 4b. Re-test after generalization (manual, in-game)

- [ ] 4b.1 Re-test HUD unpin (4.4): unpin a pinned task while hovering, no mouse movement — confirm the
      row that slides under the cursor now shows its pin/delete controls.
- [ ] 4b.2 Test new-row creation hover: hover row A, press Enter to create row B, keep the cursor still —
      confirm row A (or whichever row is now under the cursor) keeps its hover controls (the general note
      from the first playtest).

## 5. Fallback (only if 4.x can't be one-shot)

- [ ] 5.1 If the fix proves finicky, extract a pure helper (cursor xy + window pos + scale → local
      point; controller states → should-rehover bool) into an API-free unit-testable unit and add
      `Core.Tests` coverage for the pure decision, leaving only the dispatch call untested.
