## 1. Reference & baseline

- [x] 1.1 Re-read the editor's collapse choreography as the reference implementation:
      `ScribeDialogBase.Editor.cs` (`DeleteEditorBlock`, `OnEditorRowCollapsed`),
      `ScribeEditorContent.cs:292-348` (ghost splice + `ScribeFrozenEditorRow`),
      `ScribeDialogBase.Lifecycle.cs:61-101` (cleanup + scroll-pin + hover-latch), and the HUD's
      parallel copy in `HudScribePins.cs` (`departing`/`BeginDeparting`/`ReconcileDeparting`/
      `OnDepartingCollapsed`). Confirm the reusable primitives (`ScribeRowSizeAnimation`,
      `ScribeAnimationRegistry`, `ScribeHoverRefreshLatch`) are unchanged and view-agnostic.
- [x] 1.2 Mine `git show refactor-reconciling-gui-rebuild:src/Mod/ScribeListView.cs` (abandoned
      branch, ~107 lines) as reference only — extract nothing verbatim; note what its container
      shape got right/wrong for the diffing design.
- [x] 1.3 Confirm build + tests green on the current branch before starting (0 errors; Core suite
      passes) so any later failure is attributable to this change.

## 2. Build the ScribeAnimatedList container

- [x] 2.1 Create `src/Mod/ScribeAnimatedList.cs` — a `StatefulWidget` taking an ordered
      identity-keyed item set (`ValueKey<Guid>`), a row-builder, a `RemovalPolicy`
      (Immediate default | Delayed), and the enclosing scroll controller. State caches the rows
      built last frame per identity.
- [x] 2.2 Implement the departure diff: on rebuild, ids present-last-frame-absent-now become
      departing; render each from its cached last-built widget (D2), spliced at its former slot
      wrapped in `ScribeRowSizeAnimation(Collapse)` from a container-owned `ScribeAnimationRegistry`.
- [x] 2.3 Implement collapse-end cleanup internally (drop the ghost from the cache on `onEnd`, no
      host-visible cleanup flag).
- [x] 2.4 Implement the reappear-mid-collapse revival (D-risk / spec): an id that returns before its
      collapse ends cancels the departure and renders one live row — mirror the HUD's
      `ReconcileDeparting`.
- [x] 2.5 Preserve slot order for simultaneous departures (reproduce the editor's display-index math
      when splicing multiple ghosts).
- [x] 2.6 Implement the `Immediate` removal policy fully; stub/guard the `Delayed` policy path so the
      API exists but the HUD-style fade/undo-window is not wired in this change (HUD migration is a
      follow-up). Expose the diff's "appeared ids" as a seam for future insert/reorder, unused now.
- [x] 2.7 Move scroll-pin-during-collapse and the hover-refresh latch inside the container, driven by
      its own animating state. Resolve the scroll-pin autonomy open question: either hook a
      post-layout point internally, or accept the scroll controller + expose a per-frame tick the
      surface calls (still packaged). Document which was chosen and why.

## 3. Adopt in the Pinned tab (Immediate policy)

- [x] 3.1 Route `ScribePinnedContent`'s row `Column` through `ScribeAnimatedList` (Immediate policy),
      keyed by `ValueKey<Guid>(TaskId)`, feeding it the existing `ScribePinRow` builder.
- [x] 3.2 Verify the pin remove handlers (`OnPinDeleteTask`/`OnPinUnpinTask`/`OnPinCompleteTask` in
      `ScribeDialogBase.PinTab.cs`) need no new departing bookkeeping — they keep firing their packet
      and the container animates the now-absent row on the `OnMyPinsChanged` rebuild. Adjust only if
      the server re-push timing races the diff (document any change).
- [x] 3.3 Confirm the Pinned tab still reconciles correctly through `RebuildBody()` (the container sits
      inside the persistent `ScribeDialogBody` root) and that caret/scroll/hover behavior from the
      §4.3 reconcile work is preserved.
- [x] 3.4 Confirm Pinned completion semantics are unchanged (still immediate, no undo delay) — only the
      visual removal is now animated.

## 4. Verify (do not skip — this is the proof gate)

- [x] 4.1 Build clean (0 errors, no new warnings); Core suite green (`dotnet test`); restage Debug.
- [x] 4.2 Add/extend Core.Tests where the diff logic is testable game-API-free (identity diff →
      departing/appeared/surviving sets; slot-order preservation; reappear-cancels-departure). Keep
      the widget/render layer out of Core.
- [x] 4.3 Manually test in-game (Notebook Pin Tab): complete / unpin / delete a pin — confirm the row
      collapses and neighbors slide up (no snap), immediately, with no undo window. Watch for flicker,
      lost hover, deletes not landing first-click, caret loss while editing another row.
      CONFIRMED 2026-08-10.
- [x] 4.4 Manually test in-game the edge cases the design flags: remove the bottom row while scrolled
      to the bottom (viewport eases up, no snap); rapid multi-row removal (each collapses in its own
      slot, order preserved); re-pin a task mid-collapse (departure cancels, one live row, no ghost);
      complete a row so another slides under a still cursor (its controls appear). CONFIRMED 2026-08-10.
- [x] 4.5 Regression-check the surfaces this change did NOT touch: editor delete, Tablet delete, and
      the HUD (fade + undo window + sink) all still behave exactly as before. Editor + Tablet unaffected;
      the HUD regression-check SURFACED a genuine long-standing bug (ScribeFadeText never cleared its
      fade controller on a late undo → bare checkbox, no text), reliably reproducible only after the
      reconcile HUD conversion made the row element reused instead of remounted. FIXED
      (`SyncFadeController` disposes on `!Fading`) + retested CONFIRMED 2026-08-10. Flag for ModDB
      changelog.

## 5. Wrap-up

- [x] 5.1 `openspec validate extract-animated-task-list --strict` passes.
- [x] 5.2 Record playtest verdicts (regenerate TESTING.md via the what-to-test skill).
      DONE 2026-08-12 — §4.3/4.4/4.5 all CONFIRMED 2026-08-10; TESTING.md regenerated.
- [x] 5.3 Update `VSAPI-NOTES.md`/`docs/animation-lessons-learned.md` with the diffing-container
      pattern and the scroll-pin-autonomy resolution, so the follow-up (editor/HUD migration) has the
      seam documented.
- [x] 5.4 Note the follow-up change scope (below): migrate editor + HUD onto `ScribeAnimatedList`
      (collapsing the duplicated choreography; wire the Delayed policy for the HUD) — a separate
      proposal, gated on this one passing its playtest.

## 6. Follow-up scope (RE-HOMED — tracked in their own changes now, NOT this one)

Gated on §4.3-4.5 passing in-game (which they did). Both follow-ups have since been given real
homes; this section is left as a pointer so the seam history stays traceable. Do not implement
either under this change.

- [x] 6.1 **Folded into `animate-row-insertion` (§0).** Migrate the editor onto `ScribeAnimatedList`,
      deleting the hand-wired choreography in `ScribeDialogBase.Editor.cs`
      (`DeleteEditorBlock`/`OnEditorRowCollapsed`), the ghost-splice in `ScribeEditorContent.cs`, and
      the `needsEditorCollapseCleanup` deferred-rebuild flag. Done there because the editor must be a
      container consumer for the insertion-entry (fade) wiring to live in one place (D0/D1). See
      `animate-row-insertion/design.md` D0.
- [x] 6.2 **Promoted to its own change `migrate-hud-onto-animated-list`.** Migrate the HUD
      (`HudScribePins.cs`) onto `ScribeAnimatedList`, wiring the `Delayed` removal policy (currently a
      guarded stub that throws): the HUD's fade + undo-window + sink is the one surface that opts into
      delayed removal (see [[hud-undo-window-is-policy-hiding]] — the undo window exists only because
      the HUD hides the Completion Policy). Delete `departing`/`BeginDeparting`/`ReconcileDeparting`/
      `OnDepartingCollapsed`.
- [x] 6.3 Once all three interactive surfaces (editor/Pinned/HUD) route through the container, retire
      any now-dead duplicated primitives and confirm one choreography path remains. (Final
      consolidation — happens after `migrate-hud-onto-animated-list`.)
      DONE 2026-08-12 (`migrate-hud-onto-animated-list` §5.1) — all FOUR interactive surfaces
      (editor / Read / Pin Tab / HUD) now construct `new ScribeAnimatedList(...)`; the HUD's
      hand-wired `departing`/`BeginDeparting`/`ReconcileDeparting`/`OnDepartingCollapsed` +
      `ScribeRowSizeAnimation` wrap were deleted, leaving ONE choreography path (the container).
