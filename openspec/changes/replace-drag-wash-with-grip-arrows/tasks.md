## 1. Triangle glyph assets

- [x] 1.1 Add `triangle-left.svg` and `triangle-right.svg` under
  `src/Mod/assets/scribe/textures/icons/`, authored as bold solid triangles legible at the grip's
  smallest scaled size (match the visual weight of `grip.svg`).
- [x] 1.2 Register both in `src/Mod/ScribeModSystem.Assets.cs` via `RegisterSvgIcon` as
  `scribetriangleleft` / `scribetriangleright`, following the existing by-code `VsIcon` pattern
  (post-startup asset unload means by-code, not LibGUI's by-path `LoadSvg`).

## 2. Thread a "drag active" signal into rows

- [x] 2.1 In `src/Mod/ScribeEditorContent.cs`, add a `dragActive` bool input to `ScribeEditRow`
  (alongside `IsDragSource`/`IsDropTarget`) and set it from `dragFromIndex is not null` in the parent
  `.Select(...)` that builds the rows.
- [x] 2.2 In `src/Mod/ScribePinnedContent.cs`, mirror the same `dragActive` input on the pinned row
  widget and set it from that view's `dragFromIndex is not null`.

## 3. Grip-glyph arrow feedback + source dim (editor view)

- [x] 3.1 In `ScribeEditorContent.cs`, replace the grip's fixed `ScribeVsIconGlyph("scribegrip", …)`
  with a state-driven glyph: `IsDragSource` → `scribetriangleleft`; `IsDropTarget && !IsDragSource`
  → `scribetriangleright`; `dragActive && neither` → hidden (render nothing / an empty box of the
  same reserved size so the column width is unchanged); otherwise (idle) → `scribegrip` as today.
  Give the target ▶ a gentle accent color (default `Primary`) and the source ◀ the muted
  `OnSurfaceVariant`, per the design's open question.
- [x] 3.2 Wrap the row body in an always-present `Opacity` (paint-only) whose value is ~0.5 when
  `IsDragSource`, else 1.0, so the grabbed row reads as lifted without changing layout. Keep it
  always-present (don't conditionally insert) so the widget type never swaps mid-drag.
- [x] 3.3 Remove the `dragShift` source/drop-target wash branches from the row `Container`
  fill/border/thickness (~lines 752–782), leaving the `Container` to carry only the pinned tint. The
  `Container` stays always-present (structural-stability rule).

## 4. Grip-glyph arrow feedback + source dim (Pin Tab view)

- [x] 4.1 In `ScribePinnedContent.cs`, apply the identical grip-glyph state machine (3.1) to the
  pinned row's grip.
- [x] 4.2 Apply the identical always-present `Opacity` source dim (3.2) to the pinned row body.
- [x] 4.3 Remove the `dragShift` wash branches from the pinned row `Container` (~lines 399–411). A
  Pin Tab row has no resting wash, so the idle `Container` fill/border become transparent/zero.

## 5. Verification

- [x] 5.1 `dotnet build src/Mod/Mod.csproj -c Debug` clean; `dotnet test tests/Core.Tests` green
  (no Core change expected — reorder logic untouched).
- [ ] 5.2 Restage (`bash build/restage.sh Debug`); in the editor view, grip-drag a task and confirm:
  the grabbed row shows ◀ and is dimmed, all other grips vanish, the row under the cursor shows ▶,
  and no row-background drag wash appears. Release commits the move; releasing in place is a no-op.
- [ ] 5.3 In-game: drag a row over a PINNED row and confirm the pinned wash stays visible and is not
  confusable with the drop feedback (the collision this change fixes).
- [ ] 5.4 In-game: repeat 5.2/5.3 on the Pin Tab, where every row is pinned — confirm the ◀/▶ glyphs
  are the clear differentiator and the drag reads correctly with the uniform pinned wash behind it.
- [ ] 5.5 In-game: at the smallest text-size preference, confirm the triangles are legible and the
  grip column width does not change when a grip is hidden/swapped (no mid-drag row reflow).
