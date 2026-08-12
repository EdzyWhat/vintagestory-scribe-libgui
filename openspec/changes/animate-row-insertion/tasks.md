# Tasks — animate-row-insertion

> View-layer only — no `src/Core/` model/persistence/sync change. Depends on
> `reconcile-animating-surfaces` (`ScribeAnimatedList`, `ScribeListDiff.Appeared`,
> `ScribeRowSizeDirection.Reveal`); sequence after it or share its branch.

## 1. Wire the appeared seam (container)

- [ ] 1.1 In `ScribeAnimatedList.Build`, consume `lastAppeared` (currently assigned but unread): for
      each appeared id, wrap the live row in an entry animation instead of rendering it bare. Keep
      departures/revivals working exactly as today (this only touches the live-row materialization
      branch).
- [ ] 1.2 Add the container input that identifies the auto-focused newly-created row (a
      `focusedAppearedId` / per-item focus flag on `ScribeAnimatedListItem`), so the container can
      select entry mode per D1. Default = none (all appearances grow).
- [ ] 1.3 Select entry mode inside the container: focused appeared id → opacity fade at full height;
      every other appeared id → `ScribeRowSizeDirection.Reveal` height-grow. The focus-safety rule
      lives ONLY here (D1).

## 2. Entry animations (widgets)

- [ ] 2.1 Height-grow path: reuse `ScribeRowSizeAnimation` with `Reveal` (factor 0→1) driven by a
      host-owned, id-keyed controller from `ScribeAnimationRegistry` (same pattern as Collapse), so
      it resumes across `ForceRebuild`/reconcile and does not snap on remount.
- [ ] 2.2 Fade path: wrap the full-height focused row in a reconcile-stable opacity animation 0→1
      (reuse the existing `AnimatedOpacity` / `ScribeFadeText`-style mechanism — resolve which is
      reconcile-stable by reading the source first, per design Open Questions). Full height for the
      entire animation — never a height change.
- [ ] 2.3 Apply the D3 first-frame opacity floor on the fade (start at a small non-zero α) so the
      focused row is never invisible-but-focused for a frame.
- [ ] 2.4 Release the entry controller for an id when its entry animation completes (mirror
      `OnGhostCollapsed`'s registry release), so a later removal or re-insertion of the same id
      starts clean. Verify no double-play if entry then immediate removal.

## 3. Adopt across surfaces

- [ ] 3.1 Editor: on add (`OnClickAddTask` / `EditorInsertTaskBelow` / `QuickAddTopTask`), pass the
      newly-created auto-focused row id to the container so it fades in; confirm a NON-focused editor
      appearance (if any) grows. This is the focus-safety-critical surface — verify first.
- [ ] 3.2 Pin Tab: rows that appear (a task newly pinned) grow in via the container; no focused-entry
      case here (pin adds are not auto-focused), so all appearances use the grow path.
- [ ] 3.3 Read view: rows that appear grow in via the container; confirm entry motion does not fight
      the read-view scroll-pin / external-resync machinery from reconcile §5.

## 4. Optional symmetry polish (D4 — gated on cheapness)

- [ ] 4.1 If cheap and low-risk, layer a matching opacity fade onto BOTH slide paths (enter grow +
      exit collapse) for visual consistency. If it complicates or risks the collapse timing, SKIP and
      file a follow-up note — the spec does not require it.

## 5. Docs & verification

- [ ] 5.1 Update `docs/animation-lessons-learned.md`: the ScribeRevealable/enter sketch is now
      REALIZED — record the fade-vs-grow focus split and the D3 opacity floor as shipped facts, not a
      proposal. Add a `VSAPI-NOTES.md` §LibGUI note only if a non-obvious reconcile-stability gotcha
      surfaces during 2.x.
- [ ] 5.2 Core.Tests: if any pure selection logic is added (appeared-set → entry-mode predicate),
      cover it; otherwise note the behavior is view-only and assert the existing `ScribeListDiff`
      `Appeared` computation stays correct with an added row.
- [ ] 5.3 `openspec validate animate-row-insertion --strict` passes.
- [ ] 5.4 Run `build/verify.sh` (Core + Atlas) green and restage.
- [ ] 5.5 In-game playtest, record verdicts in `TESTING.md`:
      (a) add a task → the focused row FADES in at full height, caret visible and usable from frame 1,
          a click within it lands correctly throughout;
      (b) quick-add / a peer appearance → the row GROWS in and rows below settle, no pop;
      (c) trigger a `ForceRebuild`/reconcile mid-entry (e.g. pin toggle) → the entry continues, does
          not restart or snap;
      (d) add at the bottom and add past a full page → no scroll jump;
      (e) add then immediately delete the same row → clean transition, no residual/double animation;
      (f) Pin Tab and Read view appearances animate in without disturbing existing rows' focus/scroll/hover.
