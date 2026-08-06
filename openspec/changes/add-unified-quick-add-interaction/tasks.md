## 1. Shared quick-add seam (editor open + top task + caret focus)

- [x] 1.1 Add a "quick-add on open" seam to the shared editor path (`ScribeDialogBase`):
      when opening into the Editor view, optionally insert a new empty task at index 0 and
      focus its text input. Reuse the existing add path (`scratch.InsertTask(0, "")` /
      `EditorInsertTaskBelow` machinery), not a new Core mutation.
      — `QuickAddTopTask()` in `ScribeDialogBase.Editor.cs`.
- [x] 1.2 Gate the seam on the task cap: if `CanAddTaskUnderPolicy()` is false, open the
      editor but do NOT insert, and surface the existing "document full" feedback
      (`NotifyTabletFull()` / the editor add-control feedback) — matching the editor's own
      add behavior.
- [x] 1.3 Ensure the caret focus lands on the newly inserted top task after the rebuild
      settles (reuse the existing focus-node resync used by insert-task-below), verified by
      the same focus machinery the editor already uses.
      — sets `autoFocusRowOnRebuild = 0` / `focusedEditIndex = 0` + `pendingEnsureVisible`.

## 2. Lectern gesture change (BREAKING)

- [x] 2.1 In `BlockScribeLectern.OnBlockInteractStart`, stop mapping `Shift` to the plain
      editor view. Plain right-click continues to open Read.
- [x] 2.2 Route `Shift`+right-click through the new quick-add seam (open editor + top task +
      caret focus) via `BlockEntityScribeLectern.OnRightClick` / `RequestAccess`, replacing
      the `wantEditor` shift branch. Confirm the plain-editor entry point remains reachable
      via the Editor nav tab after a plain right-click.
      — quick-add flag threads OnRightClick→RequestAccess→SendReply(ScribeEditDocumentMessage.QuickAdd)
      →HandleServerReply→EnterEditorMode+QuickAddTopTask; mid-session nav-switch passes false.

## 3. Held-item gesture changes (Notebook + Tablet)

- [x] 3.1 `ItemScribeNotebook.OnHeldInteractStart`: intercept `Shift`+right-click for
      quick-add (route through the shared seam); pass through to `base` (ground storage)
      ONLY when both `Controls.CtrlKey && Controls.ShiftKey`. Plain right-click still opens
      Read. — also applied to the sibling `ItemClockmakerNotebook.OnHeldInteractStart`.
- [x] 3.2 `ItemScribeTablet.OnHeldInteractStart`: on `Shift`+right-click, keep the existing
      `TryQuench(...)` attempt; if quench does NOT fire (not aimed at water), perform
      quick-add instead of falling through. Pass through to `base` (ground storage) ONLY when
      `Controls.CtrlKey && Controls.ShiftKey`. Plain right-click still opens the tablet dialog.
      — Ctrl+Shift branch checked BEFORE the Shift branch (Ctrl+Shift also sets ShiftKey).
- [x] 3.3 Verify no `Shift`-only press reaches `base.OnHeldInteractStart` for either item, so
      ground placement never double-fires alongside quick-add.
      — base is reached ONLY under `CtrlKey && ShiftKey`; every Shift-only press is intercepted.

## 4. Interaction help & localization

- [x] 4.1 Update `GetHeldInteractionHelp` for Notebook and Tablet: advertise
      `Shift`+right-click = quick-add and `Ctrl`+`Shift`+right-click = place on ground
      (spear convention). Keep the tablet's quench hint accurate.
      — Notebook + Clockmaker add quickadd(shift) + place(ctrl+shift); Tablet is now
      state-aware: wet advertises quick-add, hard/fired advertises the water-soften hint.
- [x] 4.2 Update the Lectern block interaction hints to advertise `Shift`+right-click =
      quick-add. — `blockhelp-scribelectern-edit` relabeled "Quick-add task".
- [x] 4.3 Add/update the corresponding `en.json` help strings under `scribe:`.
      — added `itemhelp-*-quickadd`, `itemhelp-scribe-item-place`, `itemhelp-scribetablet-quench`.

## 5. Docs & changelog

- [x] 5.1 Add a prominent BREAKING-gesture note to `CHANGELOG.md` for 1.0: Lectern
      `Shift`+right-click and held-item `Shift`+right-click change meaning; ground placement
      moves to `Ctrl`+`Shift`+right-click. — Added quick-add to `### Added` and a BREAKING
      bullet to `### Changed` under `[Unreleased]`.
- [x] 5.2 Update the in-game handbook / wiki interaction docs to describe the unified
      quick-add gesture and the new ground-placement combo. — getting-started, editor
      reference "Adding tasks", Tablet (clay + wax) and Notebook handbook about-texts.

## 6. Manual in-game verification

- [ ] 6.1 Manually test in-game: `Shift`+right-click a lectern opens the editor with a new
      empty task at the top and the caret focused; plain right-click still opens Read; the
      Editor nav tab still opens the editor with no new task.
- [ ] 6.2 Manually test in-game: `Shift`+right-click a held Notebook quick-adds (top task +
      caret); `Ctrl`+`Shift`+right-click places it on the ground; plain right-click opens Read.
- [ ] 6.3 Manually test in-game: `Shift`+right-click a held Tablet aimed at water quenches a
      hard tablet; `Shift`+right-click NOT aimed at water quick-adds; `Ctrl`+`Shift`+right-click
      places it on the ground.
- [ ] 6.4 Manually test in-game: quick-add on a surface at its task cap opens the editor,
      inserts no task, and shows the "document full" feedback.
- [ ] 6.5 Manually test in-game: held-interaction help for Notebook and Tablet lists both the
      quick-add and the `Ctrl`+`Shift` ground-placement gestures.
