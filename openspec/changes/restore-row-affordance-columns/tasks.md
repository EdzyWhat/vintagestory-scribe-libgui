## 1. Reserve gutter columns in RowTextLayout

- [ ] 1.1 In `src/Mod/RowTextLayout.cs`, add a `bool reserveAffordances` parameter to `For()`.
      When true, reserve right-anchored gutter columns scaled by `TextSizeScale`:
      `dragHandleWidth`/`deleteWidth = config.X * TextSizeScale`, `pinWidth = isTask ? config.PinWidth
      * TextSizeScale : 0`; compute `dragHandleX = rowWidth - dragHandleWidth`, `deleteX = dragHandleX
      - deleteWidth`, `pinX = deleteX - pinWidth`; set `textWidth = pinX - textX`. When false, keep
      the current full-width behavior.
- [ ] 1.2 Add public fields `PinX`, `PinWidth`, `DeleteX`, `DeleteWidth`, `DragHandleX`,
      `DragHandleWidth` (unscaled, same coordinate space as the rest) and populate them from `For()`.
- [ ] 1.3 Update the class doc-comment (currently says the editor row has no gutters) to describe the
      `[checkbox][gap][text][pin][delete][grip]` layout and the `reserveAffordances` gate.

## 2. Thread reserveAffordances through height measurement

- [ ] 2.1 In `src/Mod/ScribeRowElement.cs`, add `bool reserveAffordances` to `RowHeightFixed` and pass
      it into `RowTextLayout.For`. In `ComposeElements`, pass `mode == ScribeRowMode.Edit` to `For`.
- [ ] 2.2 Update the `RowHeightFixed` call sites in `GuiDialogScribeLectern.cs`: `ComposeReadView`
      (false), `ComposeEditorView` (true), `OnEditInputTextChanged` live re-measure (true). Update the
      input-placement `RowTextLayout.For` call in `ComposeEditorView` to pass `reserveAffordances: true`
      so the floating input's `TextX`/`TextWidth` match the narrowed label (no focus jump).

## 3. Add per-row icon elements (editor view only)

- [ ] 3.1 In `ComposeEditorView`'s row loop, compute the row's affordance layout once
      (`RowTextLayout.For(..., reserveAffordances: true)`) and add — after the row element and the
      floating input — a pin button (task rows only, code `"scribepin"`, `toggleable: true`,
      `HoverRegion = rowBounds`), a delete button (all rows, code `"scribeclose"`, momentary,
      `HoverRegion = rowBounds`), and a grip button (all rows, code `"scribegrip"`, no-op callback,
      `HoverRegion = rowBounds`). Bounds from `layout.PinX/DeleteX/DragHandleX` × their widths at
      `rowYs[i]`/`rowHeights[i]`. Keys via `ScribeBlockRowCell.PinKey/DeleteKey/DragHandleKey(i)`.
- [ ] 3.2 Add hover tooltips for pin and delete via `SingleComposer.AddHoverText(Lang.Get(
      "scribe:scribe-gui-pin" / "scribe:scribe-gui-delete"), CairoFont.WhiteSmallText(),
      (int)clientConfig.HoverTextWidth, bounds.FlatCopy())`.
- [ ] 3.3 Seed the pin `On` state (`= block.Pinned`) in the compose tail where the floating input is
      seeded post-`.Compose()`, using `composer.GetToggleButton(PinKey(i))`.

## 4. Yield gutter clicks in ScribeRowElement

- [ ] 4.1 Add `private bool IsInIconGutter(MouseEvent args)` — true when `mode == Edit` and
      `args.X >= Bounds.absX + scaled(layout.PinX)` (layout via `RowTextLayout.For(..., reserveAffordances:
      mode==Edit)`).
- [ ] 4.2 In `OnMouseDownOnElement`, add `if (mode == ScribeRowMode.Edit && IsInIconGutter(args)) return;`
      beside the existing focused-column yield, leaving `args.Handled` false so the later-added icon wins.
- [ ] 4.3 In `OnMouseUpOnElement`, add the same guard before the `onRequestEdit` fire so a gutter click
      never floats the input onto the row.

## 5. Stub click wiring

- [ ] 5.1 Add `OnEditViewDeleteRow(int index)` and `OnEditViewTogglePin(int index)` in
      `GuiDialogScribeLectern.cs` as `capi.Logger.VerboseDebug(...)` no-ops, each with a
      `// TODO(follow-up)` comment naming the eventual `scratchDocument.DeleteBlock(index)` /
      `TogglePinned(index)` + `isDirty = true` + `RequestRecompose()` body.

## 6. Build, test, playtest

- [ ] 6.1 `dotnet build src/Mod/Mod.csproj -c Release` — clean (0/0). `dotnet test tests/Core.Tests`
      — green.
- [ ] 6.2 (Optional) Add a pure `RowTextLayout.For` unit test (task/note × with/without affordances)
      asserting `TextWidth` shrinks when affordances are reserved and that delete/grip X align across
      task and note rows. Plain struct, no engine deps.
- [ ] 6.3 Restage Debug (`bash build/restage.sh Debug`) and manually test in-game: (a) hover a task row
      → pin + delete + grip appear, move off → hide, no recompose flicker; (b) hover a note row →
      delete + grip only (no pin), at the same X as tasks; (c) read view → no icons on hover, checkbox
      still toggles; (d) click delete/pin → log fires, no crash, pin flips visually then reverts on next
      recompose; (e) focus a row and type → text column is narrower, label↔input handoff has no jump,
      hovering icons doesn't disturb the caret; (f) click a gutter icon while editing → does not float
      the input onto that row; (g) sweep the text-size slider → columns + icons scale, no min-size crash;
      (h) scroll with icons visible → icons track their row and clip at the viewport edge (watch for any
      bleed below the list = scissor-clobber flag).

## 7. Coordinate scope with parked changes

- [ ] 7.1 Rescope `lectern-gui-quick-edit-affordances` §6: mark 6.1/6.2 obsolete (they target the
      now-dead `ScribeBlockRowCell.TextWidth`; icon-column scaling is delivered here), keeping the 6.3
      manual test as a cross-reference or noting it's covered by 6.3(g) above.
