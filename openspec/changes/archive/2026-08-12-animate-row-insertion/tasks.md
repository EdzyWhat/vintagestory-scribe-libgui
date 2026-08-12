# Tasks — animate-row-insertion

> View-layer only — no `src/Core/` model/persistence/sync change. Depends on
> `reconcile-animating-surfaces` (`ScribeAnimatedList`, `ScribeListDiff.Appeared`,
> `ScribeRowSizeDirection.Reveal`); sequence after it or share its branch.

## 0. Migrate the editor onto `ScribeAnimatedList` (D0 — folds in `extract-animated-task-list` §6.1)

> Do this FIRST — it makes the editor a container consumer so the insertion wiring (§1) is written
> once, in the container, rather than replicated inline. This is a refactor of the collapse path
> that must stay behavior-identical before any insertion animation is added; build + playtest the
> collapse behavior after §0 and before §1 so a later regression is attributable.

- [x] 0.1 In `ScribeEditorContent.Build`, replace the hand-wired row list + `DepartingRows` ghost
      splice with a `ScribeAnimatedList`: build one `ScribeAnimatedListItem(TaskId, liveRow, ghost)`
      per block (live `ScribeEditRow`, `Ghost = new ScribeFrozenEditorRow(data, style)`), pass the
      editor's existing `Scrollbar > SingleChildScrollView > Column` as the `layoutBuilder`, and pass
      the dialog's editor collapse registry as the container `Registry`. Keep the empty-list hint
      branch as-is.
- [x] 0.2 Keep drag-reorder state (`dragFromIndex`/`dragOverIndex` + `OnRowDragStart/Over/End`) in
      `ScribeEditorContentState`; fold `isDropTarget`/`isDragSource`/`dragActive` into each live
      `ScribeEditRow` in the item builder so the container stays content-agnostic.
- [x] 0.3 Delete the dialog's hand-wired departing machinery now owned by the container:
      `DepartingRows` construction/threading, `OnDepartingCollapsed`, and the
      `needsEditorCollapseCleanup` deferred-rebuild flag in `ScribeDialogBase.Editor.cs` /
      `.Lifecycle.cs`. Route the delete-time focus fix-up through the container's
      `OnDepartureSettled` hook.
- [x] 0.4 Reconcile the OnRenderGUI loops: the editor's collapse now animates against the container's
      registry (already the same `editorCollapseRegistry` the `AnyRowAnimating` gate reads), so the
      scroll-pin/hover-latch gating needs no change — confirm `AnyRowAnimating` still sees editor
      collapses and that `pendingClampToExtent`/`pendingEnsureVisible` still fire on the settle.
- [x] 0.5 Build clean; **playtest the editor COLLAPSE path unchanged** before adding any entry
      animation: delete a mid-list row (neighbors slide up, no snap), delete while scrolled, rapid
      multi-delete (slot order preserved), delete a row mid-drag, caret in another row undisturbed.
      This is the behavior-parity gate for the migration itself. **Confirmed 2026-08-12** (playtest
      2026-08-12T08-31-32): "Editor collapse all work as expected."

## 1. Wire the appeared seam (container)

- [x] 1.1 In `ScribeAnimatedList.Build`, consume `lastAppeared` (currently assigned but unread): for
      each appeared id, wrap the live row in an entry animation instead of rendering it bare. Keep
      departures/revivals working exactly as today (this only touches the live-row materialization
      branch).
- [x] 1.2 **SUPERSEDED — no per-row focus input needed.** The first cut added `focusedAppearedId` to pick
      fade-vs-grow per row; the shipped design is ONE uniform slide for every appearance (see §1.3), so this
      input was removed. `ScribeAnimatedList` no longer takes `focusedAppearedId`, and `ScribeEditorContent`
      /`BuildEditorContent` no longer derive or thread it.
- [x] 1.3 **REVISED to a uniform slide (2026-08-12 playtest + user redirect).** The container wraps EVERY
      appeared id in one `ScribeSlideIn` (content translates in from above + fades, at full height in-slot).
      No fade-vs-grow split, no `ScribeEntryMode`, no focus predicate — the paint-only translate keeps the row
      at final height so even the auto-focused row's caret/clicks stay exact, which is what let the split
      collapse. Why: the full-height fade "appeared instantly" (playtest `d87250f4`) and a height-grow changes
      a variable-text row's height under the caret. Slide reads unmistakably; user chose slide-only, no grow.

## 2. Entry animation (widget)

- [x] 2.1 **Entry widget = `ScribeSlideIn`** (renamed from the interim `ScribeFade`): a self-ticking
      `StatefulWidget` driven by a host-owned, id-keyed `ScribeAnimationRegistry` controller — parallel to
      `ScribeRowSizeAnimation`, NOT `AnimatedSlide`/`AnimatedOpacity`/`ScribeFadeText` (all snap on
      `ForceRebuild`). Renders `Opacity(α, Transform.Translate(offset, child))` off ONE controller value:
      content starts offset up by `DefaultSlideDistance` (18px) and travels to 0 as it fades in. `Transform`
      is paint-only, so the row is full height in-slot from frame one (caret/hit-tests/ensure-visible exact).
- [x] 2.2 **The old `Reveal` height-grow entry path is not used.** `ScribeRowSizeDirection.Reveal` stays in
      the harness as a documented on-ramp, but no entry consumes it — the uniform slide replaced it (§1.3).
- [x] 2.3 Apply the first-frame opacity floor (`MinOpacity = 0.02f`) so the auto-focused row is never
      invisible-but-focused for a frame (`RenderOpacity` skips paint at α ≤ 0.001).
- [x] 2.4 Entry wrapper is KEPT-FOR-LIFE (not retired on completion): a settled `ScribeSlideIn` is an inert
      `Opacity(1) > Transform(identity)` pass-through, so no completed row type-swaps its slot back to a bare
      row and remounts its field. The entry controller is released when the id DEPARTS/REVIVES or is no longer
      live (steps 3/4/1b), never on completion. Verified no double-play if entry then immediate removal.

## 3. Adopt across surfaces

- [x] 3.1 Editor (a container consumer after §0): on add (`OnClickAddTask` / `EditorInsertTaskBelow` /
      `QuickAddTopTask`) the new auto-focused row slides in via the container. No per-row focus input and no
      inline entry animation in `ScribeEditorContent` — the container wraps every appeared id uniformly (§1.3).
      The focus-safety-critical surface; verify first.
- [x] 3.2 Pin Tab: rows that appear (a task newly pinned) slide in via the container, same uniform motion.
- [x] 3.3 Read view: rows that appear slide in via the container; confirm entry motion does not fight
      the read-view scroll-pin / external-resync machinery from reconcile §5.

## 4. Optional symmetry polish (D4 — gated on cheapness)

- [x] 4.1 **SKIPPED (per the escape clause).** Layering a cross-fade onto the height-slide paths is
      NOT cheap here: the grow/collapse rows render through `ScribeHeightFactorRender` (a height-clip
      box), while a fade needs a `ScribeFade`/`Opacity` wrapper — so a combined effect means wrapping
      every sliding row in a SECOND animation widget and threading a second controller per id through
      the same registry, doubling the entry/exit controller bookkeeping and the retire-on-complete
      logic for a purely cosmetic polish. Height-slide already reads as smooth motion; the focused row
      already fades (D1). Filing as a follow-up note rather than risking the collapse timing the §0.5
      parity gate protects. If revisited: add an optional `fade` flag to `ScribeRowSizeAnimation` that
      composes an inner `Opacity` off the SAME controller value (no second controller), so it stays a
      one-widget/one-controller change.

## 5. Docs & verification

- [x] 5.1 Update `docs/animation-lessons-learned.md`: the ScribeRevealable/enter sketch is now
      REALIZED — record the fade-vs-grow focus split, the D3 opacity floor, and the editor→container
      migration (3 of 4 surfaces now share one animation path) as shipped facts, not a proposal. Add
      a `VSAPI-NOTES.md` §LibGUI note only if a non-obvious reconcile-stability gotcha surfaces during
      §0/§2. Also mark `extract-animated-task-list` §6.1 as done-here and §6.2 as promoted to its own
      change. **Done:** added the "Row ENTRY animation" section (fade-vs-grow, D3 floor, `firstBuild`
      suppression, distinct `EntryKey`) + updated the Pointers; added the VSAPI-NOTES.md §LibGUI
      type-swap-remount note (the reconciler-stability gotcha that drove "fade wrapper stays for life").
      `extract-animated-task-list` §6.1/§6.2 were already marked (done-here / promoted).
- [x] 5.2 Core.Tests: if any pure selection logic is added (appeared-set → entry-mode predicate),
      cover it; otherwise note the behavior is view-only and assert the existing `ScribeListDiff`
      `Appeared` computation stays correct with an added row. **Done:** entry-mode selection is view-only
      (lives in the container against the VS-API widget tree), so no Core logic to unit-test; added
      `RowAppearsAtNonEndSlot_IsReportedAppeared_RenderKeepsOrder` + `MultipleRowsAppearSameFrame_AllReported`
      to `ScribeListDiffTests` to lock the `Appeared` computation for non-append and bulk additions.
- [x] 5.3 `openspec validate animate-row-insertion --strict` passes.
- [x] 5.4 Run `build/verify.sh` (Core + Atlas) green and restage. **Done 2026-08-12:** build 0 errors,
      Core 319/319, Atlas 25/25, restaged 101 files (Release).
- [x] 5.5 In-game playtest, record verdicts in `TESTING.md`. **Confirmed 2026-08-12** (playtest
      2026-08-12T09-29-40): all six items PASS — focused-add slide, peer slide, survives-reconcile,
      no-scroll-jump, add-then-delete clean, Pin Tab + Read slide.
      (a) add a task → the new auto-focused row SLIDES in (content translates down from above + fades),
          at full height in its slot; caret visible and usable from frame 1, a click within it lands
          correctly throughout the slide;
      (b) quick-add / a peer appearance → the row slides in the same way and rows below hold position
          (no height pop, since the translate is paint-only);
      (c) trigger a `ForceRebuild`/reconcile mid-entry (e.g. pin toggle) → the slide continues, does
          not restart or snap;
      (d) add at the bottom and add past a full page → no scroll jump;
      (e) add then immediately delete the same row → clean transition, no residual/double animation;
      (f) Pin Tab and Read view appearances slide in without disturbing existing rows' focus/scroll/hover.
