## 1. Branch & baseline

- [x] 1.1 Confirm work is on the dedicated `reconcile-animating-surfaces` branch (already created off
  main). This is droppable — do NOT layer it onto a feature branch.
- [x] 1.2 Capture a pre-conversion baseline: `dotnet build src/Mod/Mod.csproj` clean, `dotnet test
  tests/Core.Tests` green. Note the current `TESTING.md` items that must still pass after the editor
  conversion (caret survival, cross-row focus, scroll-offset preservation, external resync, collapse
  animation, mass-delete). [Baseline 2026-08-09: build 0 errors / 4 pre-existing warnings; 286 Core
  tests pass.]
- [ ] 1.3 Read the abandoned branch's `src/Mod/ScribeListView.cs` (`git show
  refactor-reconciling-gui-rebuild:src/Mod/ScribeListView.cs`) as reference for the D4 container — do
  NOT merge/rebase that branch (259 commits behind, rewrote a since-split file).

## 2. Generalize the animation harness (spec: gui-row-animation-harness)

- [x] 2.1 Decide the harness shape (design Open Question): one widget with a direction/mode, or a small
  family sharing the registry. Record the decision in the change. [RESOLVED: one widget
  (`ScribeRowSizeAnimation` + `ScribeRowSizeDirection`) over one shared `ScribeAnimationRegistry` —
  rationale in design.md Open Questions.]
- [x] 2.2 Generalize `ScribeCollapsible` + `ScribeCollapseRegistry` into the reusable primitive:
  self-ticking controller, host-owned + TaskId-keyed, deferred cleanup out of the ticker callback,
  supporting exit (collapse 1→0 then remove) and enter (grow 0→1, no onEnd needed). Keep the existing
  collapse behavior intact for current callers. [File → `ScribeRowSizeAnimation.cs`; both callers
  (editor, HUD) pass `direction: Collapse` + `onEnd`; behavior byte-equivalent to the old collapse.]
- [x] 2.3 Verify the harness resumes an in-flight animation across BOTH a reconcile and a
  `ForceRebuild` (identity lookup by TaskId), and releases the identity on completion. [Structural:
  reconcile reuses the element (State + controller untouched); a remount — ForceRebuild OR a positional
  reconcile shift — detaches handlers in Dispose but the registry keeps the controller, and the next
  `InitState → registry.Controller(id)` resumes from elapsed progress. `Release(id)` frees it on
  completion. Both paths share one lookup, so resume-across-reconcile ≡ the shipped
  resume-across-ForceRebuild.]
- [x] 2.4 `dotnet build` clean; existing collapse behavior unchanged (Core tests green — note the
  harness itself is not Core-unit-testable, so this is a build + no-regression check). [Build 0 errors /
  4 pre-existing warnings; 286 Core tests pass.]

## 3. Convert the editor to reconcile — THE PROOF-OF-CONCEPT (specs: scribe-dialog-base, gui-foundation-policy, gui-list-collapse)

- [x] 3.1 Give the editor a persistent content `StatefulWidget` that owns the row list; route
  add/delete/reorder through a `SetState` rebuild of the child list instead of `ForceRebuild()`. Move
  the cross-row focus coordination into (or callable from) that persistent state. [Done via a single
  persistent-ROOT body (`ScribeDialogBody` + `bodyKey`/`RebuildBody()` — see PICKUP "Architecture
  decision"), not a per-widget key: chrome (nav/title) lives outside the editor content, so only a
  root reconcile repaints it without unmounting the editor. `Build()` → `new ScribeDialogBody(bodyKey,
  BuildBodyTree)`. Rerouted Category A (Editor.cs: insert/quick-add/delete/reorder/add + Lifecycle.cs
  collapse-cleanup) and Category B (OnMyPinsChanged/OnSettingsVisibilityChanged editor branch +
  OnTitleFieldKeyDown + _pendingTitleEditRebuild) from `ForceRebuild()`→`RebuildBody()`. Focus
  coordination: NEW `pendingFocusRow` (deferred `RequestFocus` on the persistent node in OnRenderGUI)
  re-homes REUSED rows whose field skips its mount-only `autoFocus`; `autoFocusRowOnRebuild` kept only
  for genuinely-new (mounting) rows — insert/quick-add/add. Build 0 err / 4 pre-existing warns; 286
  Core tests pass. NOT yet in-game-verified (§3.7 gate).]
- [x] 3.2 Re-key editor rows from `ValueKey<int>(index)` to stable `ValueKey<Guid>(TaskId)` (design
  D3); make the departing/collapsing state an internal state of the one stable row widget so no slot
  swaps widget type across the live→departing transition. [Rows now `ValueKey<Guid>(b.TaskId)`
  (ScribeEditorContent.cs). Departing ghosts were ALREADY TaskId-keyed (`ValueKey<Guid>(taskId)`,
  wrapped in `ScribeRowSizeAnimation`) and spliced at the held display index, so the deleted slot is
  held by the ghost while it collapses — live rows below keep their slots + caret through the collapse,
  remounting only at ghost-retire (the accepted positional caveat). NOTE: live-row and ghost are still
  DIFFERENT widget types at that slot across the live→departing transition (ScribeEditRow →
  ScribeRowSizeAnimation), but at distinct keys, so no type-swap-at-a-key occurs; the task's
  "internal state of one stable row widget" phrasing is satisfied in effect (stable identity, no
  mis-update) without literally merging the two widgets. Revisit only if the gate shows a seam.]
- [x] 3.3 Keep `ForceRebuild()` for the genuinely-new-tree cases: read⇄editor⇄settings view switches,
  fresh editor seed, lost-lock recovery. Verify these still work. [Kept on ForceRebuild
  (ViewSwitching.cs EnterEditorMode/EnterReadMode/OnClickSwitchTo* + lost-lock branch); in-game gate
  item `331c44ad` confirmed 2026-08-09 they rebuild cleanly.]
- [ ] 3.4 Carry the async-resync guard onto the reconciling path: an external server resync landing
  mid-edit must not prune a legitimately-local in-flight row (never drop the focused row; never drop an
  empty task). [RefreshReadView editor branch already routes through DeleteEditorBlock→RebuildBody and
  keeps its never-drop-focused / never-drop-empty guards; NOT yet verified in-game — gate item
  `1f95e1ec` is BACKLOGGED pending a multiplayer session. Verify the guards hold under reconcile there.]
- [x] 3.5 Measure how much of the `pendingEnsureVisible` / `pendingRestoreScrollOffset` /
  `pendingClampToExtent` settling apparatus can be removed now that reconcile preserves the scroll
  controller's offset (design Open Question); remove what's no longer needed, keep what view-switches
  still require. [MEASURED + trimmed 2026-08-10. Classification of all sites:
  • `pendingRestoreScrollOffset` / `CaptureScrollForRestore` — REMOVED from the editor collapse path
    (`OnEditorRowCollapsed`, Editor.cs). It was there only to survive the old `ForceRebuild` cleanup that
    remounted the `SingleChildScrollView` and reset the offset to 0; §3.1 rerouted collapse-cleanup to
    `RebuildBody()` (Lifecycle.cs:64), an in-place reconcile that REUSES the scroll view and preserves the
    offset inherently, and §3.10's collapse-pin already glides the viewport to the shrinking bottom during
    the animation — so the captured offset equalled the current offset and the restore loop was a no-op.
    KEPT on the 4 non-editor sites (base.cs:526/547 OnMyPinsChanged+OnSettingsVisibilityChanged read/
    non-pinned branches; ViewSwitching.cs:248/358 switch-to-read) — those still `ForceRebuild` and remount
    the virtualized read `ListView`, so they genuinely need capture-restore; retiring them is §5's job.
  • `pendingClampToExtent` — KEPT as the final safety-net settle (§3.10 designed it as the net for the rare
    shrink not covered by a live collapse, e.g. LibGUI's >50px wheel-slop clamp mid-collapse); with the
    collapse-pin active it is normally a no-op (Offset ≤ max). Fully retiring it wants a DEBUG frame-trace
    confirming it never fires; left in place, deferred to §6.3 (hover-latch / capture-restore simplification).
  • `pendingEnsureVisible` — KEPT: it scrolls a specific target row into view (append-below-fold, reorder-
    chase), orthogonal to offset preservation; reconcile doesn't make it dead.
  Build 0 err / 4 pre-existing warns; 286 Core tests pass; restaged. In-game scroll behavior already
  CONFIRMED good by gate item `29b05ca5` (task 3.10 "It's so goooooood.") — this trims dead code beneath
  that confirmed behavior, so no new in-game gate; a delete-at-bottom re-verify folds into the §7 checklist.]
- [x] 3.6 `dotnet build src/Mod/Mod.csproj` clean (0 new warnings); `dotnet test tests/Core.Tests` green.
  [Build 0 err / 4 pre-existing warns; 286 Core tests pass — committed `e950334`.]
- [x] 3.7 `bash build/restage.sh Debug`, relaunch, and RUN THE EDITOR PROOF GATE in-game (design D2 —
  all must hold): (a) delete/insert/reorder preserves an actively-edited row's caret + unsaved text;
  (b) cross-row focus is preserved (no leak/loss); (c) scroll offset preserved without capture-restore;
  (d) mass-delete first click lands mid-collapse; (e) async external resync mid-edit doesn't drop a
  local in-flight row. [Playtest submission 2026-08-09T11-13-15: (a)/(b)/(c mid-list)/(d) PASS;
  the once-feared caret-position caveat came back BETTER than predicted (caret holds on delete-above AND
  reorder); (e) BACKLOGGED pending multiplayer (item `1f95e1ec`). Two regressions surfaced → tasks 3.9/3.10.]
- [x] 3.8 GO/NO-GO decision. If the gate passes → proceed to §4. If it can't be met without forking
  `gui` or a restructuring larger than the standalone fallback would cost → BAIL: abandon this branch,
  ship `fix-mass-delete-click-target` as the narrow fallback, archive this change `--skip-specs` with
  the reason recorded in `docs/animation-lessons-learned.md`. [**GO** (2026-08-09): every single-player
  D2 criterion passed and the original mass-delete-first-click bug is fixed. The reconcile conversion
  holds — NOT bailing to fix-mass-delete-click-target. Two follow-up regressions (3.9/3.10) are within
  the GO path, not bail-out triggers. Remaining §3 work: 3.4 (multiplayer verify), 3.5, 3.9, 3.10.]
- [x] 3.9 Fix the empty-task true-up regression (gate general-notes; TESTING.md `7ab1e7dc`): rapid
  "Add task" then Editor→Read→Editor leaves empty rows present in the editor (invisible in the interim
  Read step but they REAPPEAR on return). [ROOT CAUSE (read, not theorized): NOT a lost blur — the blur
  self-destruct fires on a focus TRANSITION, not on unmount (ScribeMultilineField.cs:617), so reconcile
  reusing the field doesn't stop it; and Read merely MASKS empty tasks (Layout.cs:551 filter), so the
  empties genuinely PERSIST in the seed. The leave-time PurgeEmptyTasksFromScratch()+flush is intact and
  reconcile-independent, BUT the invariant "empty tasks are never persisted" was only enforced at the
  LEAVE boundary — never at the LOAD boundary. Re-entering the editor re-seeds `scratch` from bytes
  (EnterEditorMode); on the lectern that round-trips to the SERVER, so a purge-flush and the re-access
  request can cross on the wire and the grant carries the PRE-purge doc. FIX: call
  PurgeEmptyTasksFromScratch() on the freshly-seeded scratch in EnterEditorMode (before
  SyncFocusNodesToScratch, after isDirty=false so the purge's dirty flag re-flushes a stale seed clean) —
  host/path-independent, no ForceRebuild. Build 0 err / 4 pre-existing warns; 286 Core tests pass;
  restaged. AWAITING in-game re-verification (this bug class has a misdiagnosis history — confirm, don't
  assume). CONFIRMED 2026-08-10 (playtest 2026-08-10T09-02-17): 20 empty tasks all culled on
  Edit→Read→Edit. Fix commit `7d9489e`.]
- [x] 3.10 Animate the scroll on list-shrink-at-bottom (gate general-notes; TESTING.md `29b05ca5`):
  deleting the last row while scrolled to the bottom collapses the row out, then SNAPS the scroll offset
  upward instantly — jarring. [FIX LANDED, awaiting in-game retest. Approach chosen after reading
  ScrollController (AnimateTo exists) + SingleChildScrollView: rather than animate the offset AFTER the
  collapse (a second animation racing LibGUI's ClampOffset), PIN the viewport to the bottom DURING the
  collapse. Each frame a row is collapsing, clamp Offset down to the (shrinking) MaxScrollExtent — the
  collapse's own EaseInOutCubic drives the content height down, so the offset glides in lockstep and the
  bottom edge tracks smoothly; no dead space ever opens, so there's nothing left to snap. Guarded to
  Offset > max, so a delete that leaves the viewport in-bounds is a no-op. In OnRenderGUI after
  base.OnRenderGUI (so MaxScrollExtent is this frame's collapsed height). The post-collapse
  CaptureScrollForRestore + pendingClampToExtent settle stays as the final no-op safety net (its removal
  is §3.5, not this fix). Build 0 err / 4 pre-existing warns; 286 Core tests pass; restaged. RETEST: fill
  past one scroll page, scroll to bottom, delete the last row — the close-up should ease, not snap.
  CONFIRMED 2026-08-10 (playtest 2026-08-10T09-02-17): "It's so goooooood." Fix commit `fcf1a5d`.]

- [ ] 3.11 Eliminate the WHITE FLASH on Scribe dialog open (playtest 2026-08-10; author demands a fix,
  not just a diagnosis). CHARACTERIZED but NOT yet fixed (2026-08-10). It is a one-frame **opaque
  chunk-terrain pass dropout** (OpenCV frame extract: dialog pixel-identical, sky shows through where
  near geometry should be; entities + OIT-transparent glass panes + selection wireframe all survive).
  Two initial guesses were DISPROVEN by measurement — (1) NOT first-open-only: it flashes on EVERY open
  of every Scribe item/block, same magnitude (kills the once-per-session cold-cost/pre-warm theory);
  (2) NOT a regression of this change: BISECT built pre-reconcile `5f6022a` (ScribeDialogBody absent),
  tester confirmed the flash STILL happens → pre-existing vanilla artifact (branch diff also touches
  zero render/GL/stage code). LOCALIZED by an in-game discriminator: Lectern + Notebook + Tablet flash;
  `.ui showcase` LibGUI windows do NOT; clicking inside an open Scribe window does NOT; the HUD-gear
  Scribe Settings window does NOT. Settings is `ScribeSettingsDialog : GuiBase`, deliberately NOT wrapped
  in the pixel-art parchment backdrop; the three that flash all go through ScribeDialogBase/
  GuiDialogBlockEntityBase AND paint the 1024×1160 parchment backdrop (WrapBackdrop, Layout.cs:88 —
  pixel-art ON = BoxStyle{Texture=bmp}, OFF = plain SizedBox no texture). So the isolated variable is
  **painting the backdrop bitmap on open** — not generic LibGUI, not the Skia flush (shared with the
  clean showcase path), not block interaction (Notebook/Tablet are held items, TryOpen() only, no
  MarkDirty/chunk touch). Source-cleared as NOT the mechanism: SystemRenderTerrain.OnRenderOpaque has no
  dialog gate (blank = chunk pools momentarily empty = a re-tesselation, not the engine hiding terrain);
  ClientMain.RedrawAllBlocks (requeue all chunks) fires only from `.redrawall` + smoothShadows/
  instancedGrass watchers, none on open; GuiManager.OnGuiOpened only reorders the GUI list.
  NEXT (decisive): open a flashing surface with Pixel Art Display OFF (backdrop → plain SizedBox). Gone
  → backdrop paint confirmed; fix = pre-decode/upload the backdrop as a persistent GPU texture at load so
  no cold per-open upload lands on a live frame (+ find why Skia's texture looks evicted between closes).
  Survives → not the backdrop; trace what else ScribeDialogBase/GuiDialogBlockEntityBase do on open that
  GuiBase skips. Full write-up: VSAPI-NOTES.md `## "White flash" behind a Scribe dialog…`. Do NOT add
  render/GL code to Scribe blindly; verify any fix with the DEBUG frame-trace method.]

## 4. Convert the pinned surfaces (spec: player-pins) — only after §3 passes

- [x] 4.1 Give the HUD (`HudScribePins`) a persistent content state that owns the ordered/capped row
  list; route pin-push (`OnMyPinsChanged` in-place branch), tick-expiry, and toggle through `SetState`
  instead of `ForceRebuild()`. Key HUD rows by stable TaskId; no type-swap at a slot for departing rows.
  [Done (commit `ec4864a`). `Build()` now returns `new ScribeDialogBody(bodyKey, BuildHudTree)` (the
  §3.1 persistent-root pattern reused — `ScribeDialogBody` is in the same `Scribe` namespace); the 7
  in-place sites (`OnMyPinsChanged` else-branch, `OnMyTimerChanged` status-transition,
  `OnTimerDisplayTick`, `OnTick` expiry, `OnToggleRow`, both `TickCorruption` paths, deferred
  collapse-cleanup) route through a new `RebuildHudBody()`. Rows already keyed `ValueKey<Guid>(TaskId)`;
  departing ghosts already `ScribeRowSizeAnimation` at the same key (the gate-passed §3.2 shape — stable
  identity, no type-swap-at-a-key). Reconcile-safety: `ScribeFadeText` now (re)starts its fade from a
  shared `EnsureFading()` called from BOTH `InitState` AND a new `UpdateWidget` override (a reused row's
  `Fading` false→true flip no longer silently drops the fade, since `InitState` doesn't re-run under
  reconcile); `RebuildHudBody` arms `hoverRefreshLatch` (a reconcile `SetState` doesn't swap RootElement,
  so `ArmIfRebuilt` can't catch a row reorder under a stationary cursor). Build 0 err / 4 pre-existing
  warns; 286 Core tests pass.]
- [x] 4.2 Keep the 0⇄1 self-open/close as a host concern (`TryOpen`/`TryClose`), distinct from the
  in-place row-list reconcile. [Done (commit `ec4864a`). `OnMyPinsChanged`/`OnMyTimerChanged` still call
  `TryOpen`/`TryClose` for the 0⇄1 transitions; only the "already-open, in-place repaint" branches were
  rerouted to `RebuildHudBody`. `RebuildHudBody` no-ops when `!IsOpened()`.]
- [x] 4.3 Convert the Pinned tab (`ScribePinnedContent`) structural mutations to reconcile the same way.
  [Done (commit `6eb59a7`). The in-place pin resync (`OnMyPinsChanged`/`OnSettingsVisibilityChanged`
  WHILE already in the Pinned view) routes through `RebuildBody()`; the view-SWITCH into the tab
  (`OnClickSwitchToPinned`) keeps `ForceRebuild()` (§3.3 genuinely-new tree). `ScribePinnedContent`
  already keys rows `ValueKey<Guid>(TaskId)`, owns drag state internally, and re-seeds from
  `pinEditBuffer`, so reconcile reuses every field. New `pendingFocusPinTaskId` (deferred `RequestFocus`
  on the TaskId-keyed dialog-owned node) re-homes the focused caret — the reused field skips its
  mount-only `autoFocus`, so the old `autoFocusPinTaskId` (kept for the fresh-mount view-switch) wouldn't
  re-fire. Armed `hoverRefreshLatch` (sink-policy completion reorders rows under a stationary cursor);
  dropped `CaptureScrollForRestore` for Pinned (reconcile keeps the offset). Build 0 err / 4 pre-existing
  warns; 286 Core tests pass; restaged Debug (93 files).]
- [ ] 4.4 `dotnet build` clean; playtest the pinned surfaces: pin add/remove/complete, undo window,
  sink, fade, collapse, rapid removals, re-pin, and hover-under-still-cursor — all correct, no flicker,
  no lost hover, deletes land first-click. [Build clean + restaged (Debug, 93 files) 2026-08-10 —
  AWAITING in-game playtest. Covers BOTH the HUD (§4.1/4.2) and the Pin Tab (§4.3).]

## 5. Read-view external resync (design D4) — after §4

- [ ] 5.1 Choose the tier: spike Tier 1 (`DataIdentity` token into stock `ListView`) timeboxed; fall
  back to Tier 2 (Scribe-owned `ScribeListView`, mined from the reference impl) if the non-public cache
  path isn't cleanly reachable without forking `gui`.
- [ ] 5.2 Move `RefreshReadView` (and the per-player pin-tint repaint) onto the chosen container so a
  same-count external change reconciles instead of `ForceRebuild()`.
- [ ] 5.3 Playtest: a second client toggling a task repaints the read view (external resync) without a
  full-tree rebuild; scroll offset holds.

## 6. Later surfaces & wrap-up

- [ ] 6.1 Evaluate the tablet and any other animating surfaces for the same conversion; convert or
  explicitly descope (record which, and why, in the change).
- [ ] 6.2 Update `VSAPI-NOTES.md` `## LibGUI` with the reconciling-rebuild discipline (persistent
  content + `SetState`; `ForceRebuild` reserved for new trees / hot-reload) and point the `ListView`
  child-cache note at the chosen D4 resolution.
- [ ] 6.3 Simplify the hover-refresh latch / scroll capture-restore where reconcile now makes them
  dead code on the converted surfaces (keep them where `ForceRebuild` surfaces still need them).
- [ ] 6.4 If the whole strategy succeeds, retire `fix-mass-delete-click-target` (its bug is resolved
  here). If any surface was descoped and still `ForceRebuild`s, note the residual identity workarounds
  it keeps. [The mass-delete click-target bug (TESTING.md `94c447c8`, the exact bug that change was the
  parked fallback for) is CONFIRMED FIXED in-game 2026-08-10 (playtest 2026-08-10T09-02-17: "Works.") as
  a side-effect of the editor reconcile conversion. `fix-mass-delete-click-target/` (untracked) can be
  removed once the branch merges. Do this at wrap-up, not now.]

## 7. Merge gate

- [ ] 7.1 `dotnet build src/Mod/Mod.csproj` clean (0 warnings); `dotnet test tests/Core.Tests` green.
- [ ] 7.2 `openspec validate reconcile-animating-surfaces` passes.
- [ ] 7.3 Full manual `TESTING.md` checklist green on every converted surface (focus/caret, scroll,
  external resync, animation, mass-delete, multiplayer). The branch merges ONLY when the converted
  surfaces pass — the per-surface playtest gate the prior attempt skipped.
