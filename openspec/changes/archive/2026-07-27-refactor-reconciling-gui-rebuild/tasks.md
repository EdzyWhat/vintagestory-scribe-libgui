## 1. Branch & baseline

- [ ] 1.1 Create a dedicated branch (e.g. `refactor-reconciling-gui-rebuild`) off the current GUI
  baseline. This is a ground-up rebuild of load-bearing content trees — do NOT layer it onto an
  in-flight feature branch. Record the branch name in the change.
- [ ] 1.2 Capture a pre-refactor baseline: build clean, Core tests green, and note the current
  `TESTING.md` focus/scroll/animation items that must still pass after each surface conversion
  (caret survival, scroll-offset preservation, external resync, animation).

## 2. Scribe-owned list container (`src/Mod/ScribeListView.cs`, new file)

- [ ] 2.1 Decide Tier 1 vs Tier 2 as the first concrete step (design D2). Default: Tier 2
  (non-virtualized `SingleChildScrollView` + `Column` of identity-keyed self-stateful rows). If
  spiking Tier 1 (thread `DataIdentity` into stock `ListView`), timebox it and fall back to Tier 2
  if the non-public cache path can't be reached without forking `gui`.
- [ ] 2.2 Build `ScribeListView`: rows keyed by `ValueKey<Guid>(TaskId)`, rebuilt from current data
  on a parent reconcile (no index child-cache to invalidate). Expose the row-builder + item list +
  shared `ScrollController` seam the read/editor views need.
- [ ] 2.3 Verify external resync WITHOUT a full-tree rebuild: a same-count data change (one row's
  text/done flips) reflects on a parent `SetState` (spec `gui-list-container`).
- [ ] 2.4 Document Tier 3 (custom virtualized render container) as the escalation path if Tier 2's
  "mount every row" is ever too heavy — do NOT build it now unless profiling demands it.

## 3. Convert the HUD (lowest risk, highest animation payoff)

- [ ] 3.1 Give `HudScribePins.Build()` a persistent `HudPinsContentState` that owns the ordered/capped
  row list; route the pin-push (`OnMyPinsChanged` in-place branch), tick-expiry (`OnTick`), and toggle
  (`OnToggleRow`) paths through a `SetState` setter instead of `ForceRebuild`.
- [ ] 3.2 Keep the 0⇄1 self-open/close as a host concern (`TryOpen`/`TryClose`), distinct from the
  in-place reconcile (spec `player-pins`).
- [ ] 3.3 Confirm the collapse/fade still animate under the new path; begin reverting
  `ScribeCollapsible`/`ScribeFadeText` toward stock `AnimatedSize`/`AnimatedOpacity` now that
  reconciliation holds (defer full simplification to §6 if risky).
- [ ] 3.4 Playtest the HUD items before moving on: pin add/remove/complete, undo window, sink, fade,
  collapse, rapid removals, re-pin — all still correct.

## 4. Convert the settings-form write-through

- [ ] 4.1 Make `ScribeSettingsContent` (or a wrapper) hold persistent state so a clamped write-through
  updates via `SetState`/notification instead of the host `ForceRebuild` on every `UpdateMySettings`.
- [ ] 4.2 Confirm the numeric-field focus survives without leaning on the current ForceRebuild-driven
  re-request (the `ScribeNumericFocusRegistry` may shrink in purpose); live-preview clamping still
  shows the clamped value.
- [ ] 4.3 Playtest: edit each field via type / +- / arrows; live preview + focus hold.

## 5. Convert the lectern editor structural mutations + read-view resync

- [ ] 5.1 Give the editor a persistent content state; route add/delete/reorder through a `SetState`
  rebuild of the child list (the drag-reorder path already proves this), moving the `editorFocusNodes`
  cross-row focus coordination into (or callable from) that state.
- [ ] 5.2 Move `RefreshReadView` onto `ScribeListView` so an external resync reconciles instead of
  `ForceRebuild`. Move the per-player pin-tint repaint (`OnMyPinsChanged`) onto the reconciling path.
- [ ] 5.3 KEEP `ForceRebuild` for the genuinely-new-tree cases: read⇄editor⇄settings view switches,
  fresh editor seed, lost-lock recovery. Verify these still work.
- [ ] 5.4 Confirm the caret-reset-on-rebuild trade-off is resolved (reconcile should PRESERVE caret,
  removing the end-of-text re-seed); scroll offset is preserved without the `pendingRestoreScrollOffset`
  gymnastics where reconcile now holds it.
- [ ] 5.5 Playtest the editor items: add/delete/reorder keep focus + caret + unsaved text; delete at
  scroll bottom; a second client toggling a task repaints the read view (external resync).

## 6. Simplify animation code & docs

- [ ] 6.1 Now that the HUD/editor reconcile, simplify `ScribeCollapsible` toward stock `AnimatedSize`
  (drop the host-owned resume-across-remount registry and deferred ticker-pump cleanup if no longer
  needed) and `ScribeFadeText` toward stock `AnimatedOpacity` — only where behavior is preserved.
- [ ] 6.2 Update `VSAPI-NOTES.md` (the `ListView` child-cache note ~line 989) to point at
  `ScribeListView` as the resolution, and record the reconciling-rebuild discipline (persistent
  content + `SetState`; `ForceRebuild` reserved for new trees / hot-reload).
- [ ] 6.3 Update `docs/libgui-reference.md` if it states the ForceRebuild pattern as the norm.

## 7. Build, validate & merge gate

- [ ] 7.1 `dotnet build src/Mod/Mod.csproj` clean (0 warnings); `dotnet test
  tests/Core.Tests/Core.Tests.csproj` green.
- [ ] 7.2 `openspec validate refactor-reconciling-gui-rebuild` passes.
- [ ] 7.3 `bash build/restage.sh Debug`, relaunch, and run the FULL manual `TESTING.md` checklist —
  the branch merges only when the whole suite (focus/caret, scroll, external resync, animation,
  multiplayer) passes green.
