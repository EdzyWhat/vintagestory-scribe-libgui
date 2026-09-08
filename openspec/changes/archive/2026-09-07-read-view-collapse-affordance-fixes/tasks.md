## 1. Fix pill-reset-on-completion bug

- [x] 1.1 Add a `pendingReadViewFilterCategory` (nullable) field to `ScribeDialogBase`, set it in
      `OnReadViewFilterCategoryChanged` (`ViewSwitching.cs:845-851`) alongside the existing local
      `readViewFilterCategory` assignment, and clear it once the host mirror's value matches it.
      Verify with a Mod-layer/Integration test that selecting a pill sets the pending sentinel.
      (The decision logic itself — `ShouldAcceptHostFilterCategory` — was extracted to
      `ScribeReadViewFilter` as a pure static helper and unit-tested there; `ScribeDialogBase` is a
      client-only `GuiDialog` with no Atlas test harness able to instantiate/open it, so the field
      wiring around the helper is covered by task 1.3's manual verification instead.)
- [x] 1.2 Update `RefreshReadView()` (`ViewSwitching.cs:876-884`) to only overwrite
      `readViewFilterCategory` from `host.ReadViewFilterCategory` when no pending sentinel is set,
      or when the host mirror's value equals the pending sentinel. Apply the identical guard to
      `collapsedReadViewGroupIds` against `host.CollapsedGroupIds`. Verify with a test that calls
      `RefreshReadView` with a stale host mirror while a pending sentinel is set, and confirms the
      local field is left unchanged. (Same test-surface note as 1.1 — the guard's pure decision
      logic, `ShouldAcceptHostFilterCategory`/`ShouldAcceptHostCollapsedGroupIds`, is unit-tested;
      `RefreshReadView` itself calls straight through to them.)
- [x] 1.3 Verify in-game: select the Completed pill, then un-check a `Task` row rendering under
  - Confirmed 2026-09-06: TESTING.md `000000a7` "(no note)" (submission 2026-09-06T18-08-19)
      it — confirm the Completed pill stays active and the row hides/shadow-renders per
      `read-view-subtask-collapse`'s existing rules, instead of the pill resetting to All.

## 2. Caret-only collapse toggle in the left column

- [x] 2.1 In `ScribeReadRowState.Build` (`ScribeReadContent.cs:595-621`), replace the two adjacent
      slots (invisible grip spacer, then `ScribeRowButton` toggle) with one slot: render the
      existing invisible `scribegrip` glyph when the row has no owned run, or a `GestureDetector`
      wrapping a bare `ScribeVsIconGlyph("scribetriangleright"/"scribetriangledown", ...)` (no
      `ScribeRowButton`/`BoxStyle` chrome) when it does. Verify with a widget-tree test/manual
      inspection that a group-parent row renders no separate button `Container` around the caret.
      (No widget-tree test harness exists in this repo for LibGUI trees — manual inspection of the
      merged single-slot `Padding(GripInsets, child: GestureDetector(...) | Opacity(...))` build
      confirms no `Container`/`BoxStyle` wraps the caret; task 2.2 covers the in-game confirmation.)
- [x] 2.2 Verify in-game: the collapse toggle appears only on Quest Link/Craft parent rows, in the
  - Confirmed 2026-09-06: TESTING.md `000000a8` "(no note)" (submission 2026-09-06T18-08-19)
      left column (where the grip would be), renders as a bare caret with no border/background,
      and remains clickable to collapse/expand; rows without an owned run show nothing there.

## 3. Automated coverage

- [x] 3.1 Run the full `Core.Tests` and `Integration.Tests` suites and confirm all pass.
      (Core.Tests: 753/753 passed. Integration.Tests: 52/52 passed, VINTAGE_STORY pointed at the
      installed game.)
