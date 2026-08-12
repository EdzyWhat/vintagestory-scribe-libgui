# Tasks — migrate-hud-onto-animated-list

> View-layer only — no `src/Core/` model/persistence/sync change. Depends on
> `extract-animated-task-list` (the container + the `Delayed` stub) and is best sequenced AFTER
> `animate-row-insertion` (editor migration + the `ScribeFade` host-owned fade primitive). This is
> the final consolidation of `extract-animated-task-list` §6.2 + §6.3.

## 1. Baseline & reference

- [ ] 1.1 Re-read the HUD's hand-wired departure choreography as the reference: `HudScribePins.cs`
      `departing` / `DepartingRow` / `BeginDeparting` / `ReconcileDeparting` / `CancelDeparting` /
      `OnDepartingCollapsed`, the `awaitingRemoval` server-confirmation set, `UndoWindowMs`, and the
      `ScribeFadeText` text-fade. Record the exact undo-window duration and fade curve so the
      migration can reproduce them (D5 parity gate).
- [ ] 1.2 Confirm the container's `Immediate` path (Read/Pin/editor) and the `ScribeFade` primitive
      from `animate-row-insertion` are in place and green before starting.

## 2. Wire the `Delayed` removal policy in the container

- [ ] 2.1 Replace the `Delayed` guard (`throw NotSupportedException`) in `ScribeAnimatedList` with a
      real hold-then-collapse: on the build a row departs under `Delayed`, record a hold deadline and
      render its ghost at FULL height for the undo window, then transition into the existing
      `ScribeRowSizeDirection.Collapse` (D1). Drive the hold off the same host-owned
      `ScribeAnimationRegistry` / ticker the collapse uses — no second timing system.
- [ ] 2.2 Optional hold-phase fade: during the hold, fade the ghost content via the `ScribeFade`
      host-owned-controller primitive (D2), NOT `ScribeFadeText`. Full height throughout the hold —
      the fade is content-only, the misclick-rescue height stays.
- [ ] 2.3 Reappear-during-hold: confirm the container's existing reappear-cancels-departure path
      covers a `Delayed` row revived during its hold window (subsumes `CancelDeparting`); add a case
      if the hold phase needs explicit cancel handling.
- [ ] 2.4 Expose the undo-window duration as a per-adoption parameter (the HUD passes its
      `UndoWindowMs`); default/other surfaces stay `Immediate`. Keep `Immediate` behavior byte-for-byte
      unchanged.

## 3. Migrate the HUD onto the container

- [ ] 3.1 Route `HudScribePins`'s pin rows through `ScribeAnimatedList(Delayed)`, keyed by the
      existing `(docId, taskId)` identity, feeding the existing `HudPinRow` builder and the HUD's
      layout as the `layoutBuilder`. Keep the "+N more" affordance and sink overlays as HUD chrome
      outside the animated row set (D-open-question — confirm they compose).
- [ ] 3.2 Keep `awaitingRemoval` in the HUD and keep suppressing in-flight-destructive pins from the
      item set handed to the container (D3) — the pin leaving the set is what triggers the `Delayed`
      departure. Delete `departing` / `DepartingRow` / `BeginDeparting` / `ReconcileDeparting` /
      `CancelDeparting` / `OnDepartingCollapsed`.
- [ ] 3.3 Sink / UnpinSink stay an in-set reorder via `sunkOrder`, NOT a departure (D4). Confirm the
      sink countdown/overlay still renders alongside a concurrent `Delayed` departure of another row.
- [ ] 3.4 If departure-fading no longer uses `ScribeFadeText` anywhere, remove it; otherwise document
      what still uses it.

## 4. Verify (parity gate — do not skip)

- [ ] 4.1 Build clean (0 errors, no new warnings); Core suite green; restage Debug.
- [ ] 4.2 Core.Tests: if the hold-then-collapse timing gets any pure logic (hold-elapsed →
      begin-collapse predicate), cover it game-API-free; otherwise assert the container's existing
      diff behavior is unchanged for `Immediate`.
- [ ] 4.3 In-game HUD parity (A/B against pre-migration behavior): complete a pin under Unpin /
      Delete → row holds at full height for the SAME undo window, text fades as before, THEN
      collapses and neighbors slide up. Undo within the window (re-pin / re-add) → departure cancels
      cleanly, no ghost, text restored.
- [ ] 4.4 In-game HUD edge cases: Sink / UnpinSink still moves the row to the bottom (no departure);
      rapid multi-completion (each holds+collapses in its own slot, order preserved); a row sliding
      under a stationary cursor keeps its controls; re-pin during the undo window revives; the
      `ScribeFadeText` undo-fade regression from `extract-animated-task-list` §4.5 stays fixed.
- [ ] 4.5 Regression-check the three already-migrated surfaces (editor / Read / Pin Tab) are
      untouched by the `Delayed` wiring — their `Immediate` removals behave exactly as before.

## 5. Consolidation & docs (`extract-animated-task-list` §6.3)

- [ ] 5.1 Confirm all four interactive surfaces (editor / Read / Pin Tab / HUD) route through
      `ScribeAnimatedList`; retire any now-dead duplicated primitives and verify ONE choreography path
      remains.
- [ ] 5.2 Update `docs/animation-lessons-learned.md` + `VSAPI-NOTES.md` §LibGUI: the `Delayed` policy
      is now WIRED, the HUD is migrated, one animation path across all surfaces. Update
      [[hud-undo-window-is-policy-hiding]] memory to note the mechanism moved to the container policy
      (the decision itself is unchanged).
- [ ] 5.3 `openspec validate migrate-hud-onto-animated-list --strict` passes.
- [ ] 5.4 Run `build/verify.sh` (Core + Atlas) green and restage.
- [ ] 5.5 Record playtest verdicts in `TESTING.md` (regenerate via the what-to-test skill). Flag the
      mechanism change for the ModDB changelog if user-visible timing shifts at all (it should not).
