## 1. Config knobs (ScribeClientConfig.cs)

- [x] 1.1 Add affordance-styling knobs, all with sensible defaults (flat, hand-editable JSON):
      `AffordanceBgR/G/B/A` (opaque parchment-tone fill ~ dialog bg, A ≈ 1.0), `AffordanceOutlineR/G/B/A`
      (ink-tone, low-alpha), `AffordanceOutlineThickness` (~1.5), `AffordanceIconColorR/G/B/A` (ink-tone),
      `AffordanceIconFill` (fraction of the button the icon spans, ~0.8), and optional
      `AffordanceCornerRadius`. Add `DragColumnAlwaysReserved` (bool, default true).

## 2. Custom minimal button — Group A1 (ScribeBlockRowCell.cs + ScribeRowElement.cs)

- [x] 2.1 In `ScribeRowElement.cs`, confirm/expose the `RoundedRect` helper and the `DrawIcon`-into-
      surface idiom as reusable (they may need to be `internal static` or extracted) so the button can
      call them without duplicating the geometry.
- [x] 2.2 In `ScribeBlockRowCell.cs`, evolve `ScribeHoverIconButton` into a self-drawing button:
      override `ComposeElements` WITHOUT calling base; add an own `LoadedTexture` field + ctor init +
      `Dispose()` override (mirror `ScribeRowElement`'s texture lifecycle). Bake onto an own
      `ImageSurface`/`Context`: opaque rounded-rect fill (`AffordanceBg*`), then thin outline stroke
      (`AffordanceOutline*`, `LineWidth = max(1, scaled(AffordanceOutlineThickness))`), then
      `api.Gui.Icons.DrawIcon(ctx, iconCode, inset, inset, size - 2*inset, size - 2*inset, iconColor)`
      with `inset = InnerWidth * (1 - AffordanceIconFill) / 2` and `iconColor` from `AffordanceIconColor*`.
      Derive ALL draw math from `InnerWidth/InnerHeight` so the pill matches the base-hit-tested `Bounds`.
- [x] 2.3 In `RenderInteractiveElements`, keep the existing `HoverRegion` early-out, then blit the own
      texture with `Render2DTexturePremultipliedAlpha` (mirror `ScribeRowElement`), inside the dialog clip.

## 3. Input margin symmetry — Group A6

- [x] 3.1 In `ScribeRowElement.cs`, center single-line content in the floored row height: compute one
      `contentTop` local from `max(minHeight, contentHeight)` and use it for both the text draw and the
      checkbox-glyph draw so they agree (no bottom-heavy slack).
- [x] 3.2 In `GuiDialogScribeLectern.cs`, set the floating input's bounds height to
      `rowHeights[i] - BottomOverheadFixed(config)` so the focus highlight stops above the ruling with a
      margin matching the top (safe: input is `Autoheight=false`).

## 4. Layout rewrite — Group B (RowTextLayout.cs)

- [x] 4.1 In `RowTextLayout.For`, set `TextWidth = rowWidth - textX` (stop subtracting the pin/delete
      gutters) while keeping `PinX`/`DeleteX` right-anchored as overlay anchors.
- [x] 4.2 Relocate the drag column to the far left: `dragColWidth = DragHandleWidth * TextSizeScale`
      reserved when `DragColumnAlwaysReserved` (both views); `DragHandleX = 0`; `CheckboxX = dragColWidth`;
      `TextX = dragColWidth + checkboxSize + checkboxTextGap`. Update the class doc-comment to describe
      the new `[grip][checkbox][gap][text …pin/delete overlay]` layout.

## 5. Single-line button height + overlay wiring — Group B2/B3/B5

- [x] 5.1 Add `ScribeRowElement.SingleLineRowHeightFixed(capi, font, config)` built on the existing
      `MeasureWrappedTextHeightFixed` primitive (floored at `MinRowHeight`), giving a one-line row
      height. (Block-independent — a single line is the same height for a task or a note — so it takes
      no `block`/`reserveAffordances`; all three buttons share the one value and line up.)
- [x] 5.2 In `GuiDialogScribeLectern.cs`, set the pin/delete/grip button bounds height to the single-line
      height, top-aligned at `rowYs[i]`; set the grip's `x` to `layout.DragHandleX` (=0). Text and input
      widths follow the now-full `layout.TextWidth`.
- [x] 5.3 In `ScribeRowElement.cs`, fix `IsInIconGutter` to yield only when `args.X >= Bounds.absX +
      scaled(overlayStartX)` where `overlayStartX = rowWidth - (pinWidth + deleteWidth)` (narrow right-
      edge cluster), so text left of the cluster still focuses/edits.

## 6. Build, test, playtest

- [x] 6.1 `dotnet build src/Mod/Mod.csproj -c Release` — clean. `dotnet test tests/Core.Tests` — green
      (no Core change expected).
- [ ] 6.2 Restage Debug (`bash build/restage.sh Debug`) and manually verify in-game: (1) thin-outline
      buttons, no brown pill, icon fills most of the button; (2) on a tall wrapped note, pin/delete/grip
      sit on the TOP line, not spanning the row; (3) long text uses full row width and hovering overlays
      pin/delete with their opaque background hiding the text beneath; (4) click overlaid pin/delete →
      stub log fires, click text LEFT of the cluster → focuses/edits; (5) focused input highlight has
      equal top/bottom margin, not touching the ruling, and a short min-height task centers; (6) grip is
      far-left of the checkbox and checkbox/text shift right in the editor; (7) Read↔Edit toggle → the
      checkbox does NOT jump horizontally; (8) text-size min↔max scales buttons/outline/icon/drag
      column/margins with no min-size crash; (9) pin `On` persists across a mouse-up elsewhere; (10)
      scroll a row past the clip edge → overlay icons + input clip natively (no bleed).

## 7. Docs

- [x] 7.1 If any non-obvious `GuiElementToggleButton`/`DrawIcon` behavior was relied on (e.g. the base's
      `scaled(4)` icon inset, or `Toggleable` reset-on-mouseup), record it in `VSAPI-NOTES.md` so it's not
      re-derived.
