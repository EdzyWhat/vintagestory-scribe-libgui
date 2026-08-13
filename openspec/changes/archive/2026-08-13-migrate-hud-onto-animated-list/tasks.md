# Tasks — migrate-hud-onto-animated-list

> View-layer only — no `src/Core/` model/persistence/sync change. Depends on
> `extract-animated-task-list` (the container + `Immediate` policy) and `animate-row-insertion` (the
> `ScribeSlideIn` entry primitive), both MERGED + ARCHIVED on main. This is the final consolidation of
> `extract-animated-task-list` §6.2 + §6.3.
>
> **Premise correction (2026-08-12):** the HUD does NOT need a container `Delayed` ghost-hold policy —
> its undo window is a deferred-network-send phase on a LIVE row, which stays in the HUD. The migration
> uses the container's existing `Immediate` policy for the post-send collapse, and the misconceived
> `Delayed` enum member is removed. See design.md "The premise correction."

## 1. Baseline & reference

- [x] 1.1 Re-read the HUD's hand-wired departure choreography as the reference: `HudScribePins.cs`
      `departing` / `DepartingRow` / `BeginDeparting` / `ReconcileDeparting` / `CancelDeparting` /
      `OnDepartingCollapsed`, the `awaitingRemoval` server-confirmation set, `pendingCompletions` /
      `PinHudWaitMs` (the deferred-send undo window), and the LIVE-row `ScribeFadeText` text-fade
      (`IsFadingOut` / `FadeWindowMs`). Record the parity baseline (D5): window 1500ms, `ScribeFadeText`
      linear fade over 1500ms, 200ms collapse.
- [x] 1.2 Confirm the container's `Immediate` path (Read/Pin/editor) and the `ScribeSlideIn` entry
      primitive are in place and green before starting (`dotnet build src/Mod/Mod.csproj`,
      `dotnet test tests/Core.Tests`).

## 2. Remove the misconceived `Delayed` policy from the container

- [x] 2.1 Delete the `ScribeListRemovalPolicy.Delayed` enum member and the `throw NotSupportedException`
      guard in the `ScribeAnimatedList` ctor. The enum keeps only `Immediate` (leave the enum + `policy`
      param in place so the call sites and the D6/D7 wiring read explicitly; do not collapse the param
      away). Update the enum + policy doc-comments to drop the "delayed / HUD follow-up" language.
- [x] 2.2 Confirm `Immediate` behavior is byte-for-byte unchanged (no logic touched — only the dead
      branch removed). Build clean.

## 3. Migrate the HUD onto the container (Immediate)

- [x] 3.1 In `HudScribePins.BuildHudTree`, route the ordered `HudPinRow`s through
      `ScribeAnimatedList(Immediate)`, keyed by the existing `(docId, taskId)` identity → a single `Guid`
      key for the container (the container keys by one `Guid`; derive a stable key — e.g. `TaskId`, which
      is unique per pin). Each `ScribeAnimatedListItem` supplies the live `HudPinRow` widget as `Child`
      and a frozen ghost as `Ghost` (a zero-opacity-text frozen twin of the row, matching today's
      `departing` snapshot so the collapse closes an already-faded row — design D2/Risks). Use the HUD's
      layout (`SizedBox` > `Padding` > `Column`) as the `layoutBuilder`; keep the header, "+N more",
      timer row, and sink overlays as HUD chrome OUTSIDE the animated row set (D-open-question — confirm
      they compose).
- [x] 3.2 Keep `awaitingRemoval` in the HUD and keep suppressing in-flight-destructive pins from the
      item set handed to the container (D3) — the pin leaving the set is what triggers the `Immediate`
      collapse. Keep `pendingCompletions` / `PinHudWaitMs` / `OnToggleRow` / `OnTick`'s send-on-expiry
      and the live-row `ScribeFadeText` fade — all unchanged (the undo window is HUD-owned, D2).
- [x] 3.3 Delete `departing` / `DepartingRow` / `BeginDeparting` / `ReconcileDeparting` /
      `CancelDeparting` / `OnDepartingCollapsed` / `needsCollapseCleanup`, and the `BuildRow`
      `Departing`-branch `ScribeRowSizeAnimation` hand-wiring. Reduce `ReconcileDeparting`'s surviving
      job (drop `awaitingRemoval` ids the server removed or that were re-pinned) into a small
      `awaitingRemoval`-only reconcile still called from `OnMyPinsChanged`. The HUD keeps its own
      `collapseRegistry` (now passed to the container) + `hoverRefreshLatch` + the `OnRenderGUI`
      scroll/hover loops driven off `collapseRegistry.AnyAnimating`.
- [x] 3.4 `ScribeFadeText` STAYS (it is the live-window countdown fade, not a departure fade —
      correcting the original design). Confirm it is still referenced only by the HUD's live-row path;
      leave it in `HudScribePins.cs`. `ScribeRowSizeAnimation` is now only used via the container.

- [x] 3.5 (D6) Leave the container's entry animation ENABLED for the HUD adoption (`animateEntry: true`)
      so a newly pinned row (or one crossing into the capped window because another collapsed out) SLIDES
      in like the editor/Read/Pin Tab, not a snap. Confirm the `+N more` cap boundary and sink overlay
      don't force a pop, and that first-build/ForceRebuild entry suppression still holds for the HUD.

- [x] 3.6 (D7) Align the HUD's row ordering with the Pin Tab: replace `BuildOrderedRows()`'s bespoke
      base order with `ScribePinOrdering.ForDisplay` under the sinking policies / raw pin order otherwise
      (mirror `OrderedPinsForDisplay()`), then re-apply ONLY the two surviving HUD-specific overlays on
      top: the durable session-sink bottom-hold (`sunkOrder`) and the in-undo-window in-place hold. Do
      NOT regress the `40be9d31` cross-surface Sink agreement (confirmed 2026-08-11).

## 4. Verify (parity gate — do not skip)

- [x] 4.1 Build clean (0 errors, no new warnings); Core suite green; restage Debug.
- [x] 4.2 Core.Tests: no new Core logic expected (the ordering rule `ScribePinOrdering` is already
      covered; the window/fade stay in the Mod layer). If any pure ordering-overlay logic is extracted to
      Core, cover it; otherwise note the migration is GUI-layer and relies on the in-game gate.
- [x] 4.3 In-game HUD parity (A/B against pre-migration behavior): complete a pin under Unpin / Delete →
      row holds LIVE at full height for the SAME undo window, text fades as before, checkbox stays
      clickable, THEN (on send) collapses and neighbors slide up. Undo within the window (uncheck) →
      nothing sent, text restored, no collapse, no ghost.
- [x] 4.4 In-game HUD edge cases: Sink / UnpinSink still moves the row to the bottom (no departure);
      rapid multi-completion (each collapses in its own slot after its own window, order preserved); a
      row sliding under a stationary cursor keeps its controls; re-pin while a row is collapsing revives
      it (container `diff.Revived`); the `ScribeFadeText` undo-fade regression from
      `extract-animated-task-list` §4.5 stays fixed.
- [x] 4.5 Regression-check the three already-migrated surfaces (editor / Read / Pin Tab) are untouched by
      the `Delayed`-removal deletion — their `Immediate` removals behave exactly as before.

- [x] 4.6 (D6) In-game HUD entry: newly pin a task → its HUD row SLIDES in (matching the
      editor/Read/Pin Tab), does not snap; a row scrolling into the capped window because another
      collapsed also slides; no entry animation on HUD first-open or a ForceRebuild.

- [x] 4.7 (D7) In-game HUD ordering: open the Pin Tab and the HUD side by side (or toggle) and confirm
      they render pins in the SAME order across pin/unpin/complete and under each completion policy
      (Keep / Unpin / Delete / Sink / UnpinSink); the durable-sink bottom-hold and in-window hold still
      behave as before; the `40be9d31` cross-surface Sink agreement still holds.

## 5. Consolidation & docs (`extract-animated-task-list` §6.3)

- [x] 5.1 Confirm all four interactive surfaces (editor / Read / Pin Tab / HUD) route through
      `ScribeAnimatedList`; retire any now-dead duplicated primitives and verify ONE choreography path
      remains. Close `extract-animated-task-list` §6.3.
- [x] 5.2 Update `docs/animation-lessons-learned.md` + `VSAPI-NOTES.md` §LibGUI: the HUD is migrated onto
      `ScribeAnimatedList(Immediate)`, one animation path across all surfaces, and record the `Delayed`
      misconception (undo window = deferred-send phase, not an animation hold — a ghost can't host a live
      undo checkbox) so it isn't re-proposed. Update [[hud-undo-window-is-policy-hiding]] memory to note
      the collapse moved to the container while the undo window stayed a HUD deferred-send phase.
- [x] 5.3 `openspec validate migrate-hud-onto-animated-list --strict` passes.
- [x] 5.4 Run `build/verify.sh` (Core + Atlas) green and restage.
- [x] 5.5 Record playtest verdicts in `TESTING.md` (regenerate via the what-to-test skill). The migration
      should be user-invisible (timing/feel unchanged); flag for the ModDB changelog only if anything
      shifts.
