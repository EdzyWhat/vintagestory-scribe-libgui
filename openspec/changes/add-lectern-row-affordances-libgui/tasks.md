## 1. Row-control scaffolding (shared)

- [x] 1.1 Add a hover-tracked flag to `ScribeEditRowState` (via a row-level `MouseRegion` `onEnter`/`onExit`, not `GestureDetector` — MouseRegion is the dedicated hover primitive) so per-row controls can be hidden unless the row is hovered (`lectern-gui-shell` "Row icons are hover-conditional"). Hit-testing is innermost-first and enter/exit propagate up the hierarchy, so the row region does NOT steal click-to-focus from the inner field (no caret disturbance).
- [x] 1.2 Add a reserved affordance-control area to `ScribeEditRow`'s `Row` (grip + delete for all rows; pin for task rows only) via fixed-width `ScribeRowControlSlot`s, sized from `ScribeRowStyle.ControlSize` (= `RowCheckboxSize * TextSizeScale`) so it scales with the row (`lectern-gui-shell` scaling). Fixed-width slots mean revealing a hover glyph does not reflow the text column.
- [x] 1.3 Render the control glyphs with `VsIcon("scribegrip"/"scribeclose"/"scribepin", size, color)` — reuse the already-registered `CustomIcons` codes (no new rendering code). NOTE: `IconButton` was NOT used — it accepts only an SVG-by-path `Icon` whose `LoadSvg` fails on our post-startup-unloaded assets; instead delete/pin use `ScribeHoverVsIcon` (VsIcon + GestureDetector + hover brighten) and the grip is a bare `ScribeVsIconGlyph` inside the drag `GestureDetector`.

## 2. Delete

- [x] 2.1 Add a dialog method wrapping `scratch.DeleteBlock(index)` → mark dirty → `SyncFocusNodesToScratch()` → `ForceRebuild()` (mirror `OnClickAddTask`); route through the existing lock-gated autosave (`DeleteEditorBlock`)
- [x] 2.2 Handle focus safety when deleting the focused/last row: clear or relocate `focusedEditIndex` if it pointed at or past the deleted row, so no path focuses a removed row (spec "deleting the focused row does not break focus"); empty document falls back to the existing empty-state hint
- [x] 2.3 Wire the per-row delete control (`ScribeHoverVsIcon` "scribeclose") `onTap` to 2.1

## 3. Pin

- [x] 3.1 Add a dialog method wrapping `scratch.TogglePinned(index)` → mark dirty → autosave; task rows only (pin control absent on text-section rows) (`TogglePinnedEditorTask`)
- [x] 3.2 Wire the per-row pin control (`ScribeHoverVsIcon` "scribepin") `onTap` to 3.1; reflect the pinned state in the glyph color (accent `Primary` when pinned, muted `OnSurfaceVariant` when not)
- [x] 3.3 Render the resting pin indicator (subtle row-background tint, `PinnedIndicatorMode.RowTint` intent, from `ScribeRowStyle.PinnedTint`) in BOTH `ScribeEditRow` and `ScribeReadRow`, drawn under row content via a `Container`, from the row's `Pinned` snapshot (spec "Pinned tasks show a resting indicator")

## 4. Mouse-drag reorder

- [x] 4.1 Add a `GestureDetector(onPress, onRelease)` to the grip; `onPress` records the dragged index + marks drag active in `ScribeLecternEditorContentState` (relies on the dispatcher's automatic pointer capture, as `Scrollbar`'s thumb does)
- [x] 4.2 REVISED APPROACH (simpler than the planned `GlobalToLocal`/`ComputeContentSpaceY` walk): the drop index comes from the row-level `MouseRegion.onEnter` firing `OnRowDragOver(index)`. During a grip-drag capture the dispatcher still hit-tests the pointer and fires enter/exit on the row under it (`_dragHoveredElement`), so each crossed row reports itself as the drop target with no manual geometry — and this is inherently scroll-correct (hit-test uses live element bounds). Recorded in `dragOverIndex`.
- [x] 4.3 On `onRelease` (`OnRowDragEnd` → dialog `ReorderEditorBlock`), if drop index ≠ start index, `scratch.MoveBlock(from, to)` → mark dirty → `SyncFocusNodesToScratch()` → `ForceRebuild()`; release-in-place is a no-op (spec "Dropping in place changes nothing") — guarded in both `OnRowDragEnd` and `ReorderEditorBlock`
- [x] 4.4 Show drop feedback during the drag: the current drop-target row paints a `StateSelected` highlight `Container` (spec allows "a highlighted target"), driven by `IsDropTarget` (computed from the drag's own `dragOverIndex`, NOT sibling hover), via `SetState` on the content state. (Insertion-line `Divider` was the alternative; target highlight chosen to avoid mutating the `ValueKey`-keyed child list mid-drag, which would risk the grip's capture.)
- [ ] 4.5 Verify (in-game) the reorder works with the list scrolled and across sibling rows mid-drag (capture holds) — deferred to §5 playtest; the enter-driven approach is scroll-correct by construction (uses live hit-test bounds), but confirm empirically

## 5. Verify

- [x] 5.1 `dotnet build src/Mod/Mod.csproj` clean (0/0); `dotnet test tests/Core.Tests` still green (47/47). No Core changes in the rendering layer (the `ScribeRowStyle`/dialog edits are all in `src/Mod`).
- [ ] 5.2 In-game: delete a task (incl. the focused row and the last row); confirm it's gone, order preserved, persists across reload
- [ ] 5.3 In-game: pin/unpin a task; confirm the resting indicator shows in both read and editor views and persists across reload; confirm no pin control on text-section rows
- [ ] 5.4 In-game: drag-reorder rows (including with the list scrolled); confirm drop feedback, the drop lands where released, in-place release is a no-op, and the order persists across reload
- [ ] 5.5 Regression: confirm click-to-edit, text selection, checkbox toggle, Enter/Tab keyboard model, and autosave still work with the new controls present; note whether the 8.5 caret-to-end residue is bothersome after a reorder/delete rebuild
- [ ] 5.6 Update `TESTING.md` with verdicts for 5.2–5.5 and sync/annotate `add-lectern-block` 5.2/5.4 (delete/reorder now delivered here); sync deltas to main specs and archive per the openspec flow when confirmed
