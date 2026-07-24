## Context

`restore-row-affordance-columns` (archived 2026-07-22) rebuilt the pin/delete/grip buttons on the
`ScribeRowElement` row architecture using `ScribeHoverIconButton : GuiElementToggleButton`. That base
bakes its brown pill chrome + a small icon together in private `ComposeReleasedButton`/
`ComposePressedButton` methods (`DrawIcon` at a fixed `scaled(4)` inset — the reason the glyph looks
small), with no color/inset seam to override. The only public virtual hook is `ComposeElements`. The
row's horizontal layout is centralized in `RowTextLayout.For(rowWidth, isTask, font, config,
reserveAffordances)`, which currently reserves right-anchored pin/delete/grip gutter columns and
subtracts them from `TextWidth`. The custom row draws its own texture (checkbox glyph + text + ruling)
via the `RoundedRect`/`DrawCheckboxGlyph` idiom and blits it in the interactive render pass under the
dialog's `BeginClip`. This change is GUI-layer only; Core is untouched.

## Goals / Non-Goals

**Goals:**
- Minimal Notion-style buttons: thin ink-tone outline, opaque parchment background that occludes
  covered text on hover, icon filling most of the button.
- Buttons sized to one text line, top-aligned; pin/delete as a hover overlay (text runs full-width);
  drag grip in a dedicated far-left column with the read view reserving matching width.
- Symmetric input top/bottom margin against the ruling; single-line content centered in floored rows.
- All new styling values as defaulted, hand-editable config knobs.

**Non-Goals:**
- Any drag-to-reorder interaction feedback (grip stays a visual no-op).
- Any Core change or persistence change.
- Wiring the delete/pin stubs to real document mutations (still a later change).

## Decisions

**Custom button via `ComposeElements` override (no base call).** `ScribeHoverIconButton` keeps the
`GuiElementToggleButton` base for its `On`/`Toggleable` hit-test plumbing (the pin needs stateful
toggle) but overrides `ComposeElements` to bake its own `LoadedTexture` instead of the brown chrome:
opaque rounded-rect fill (`AffordanceBg*`, alpha ~1.0 to occlude text) → thin outline stroke
(`AffordanceOutline*`, `LineWidth = max(1, scaled(AffordanceOutlineThickness))`) → `DrawIcon` at
`inset = InnerWidth*(1 - AffordanceIconFill)/2` so the icon fills most of the button. All draw math
derives from `InnerWidth/InnerHeight` so the visible pill matches the clickable `Bounds` the base
hit-tests. `RenderInteractiveElements` keeps the `HoverRegion` early-out, then blits the texture with
`Render2DTexturePremultipliedAlpha` inside the dialog clip. Reuses the exact texture pattern already
proven in `ScribeRowElement`.

**Icon color: ink-tone, config-driven** (`AffordanceIconColor*`), to match the parchment aesthetic —
not white.

**Overlay geometry, `RowTextLayout.For` rewritten once.** `TextWidth = rowWidth - textX` (full width);
`PinX`/`DeleteX` stay right-anchored as *overlay anchors* (no longer subtracted from text). The
yield/hit boundary `IsInIconGutter` yields to the overlay only when `args.X >= absX +
scaled(overlayStartX)`, `overlayStartX = rowWidth - (pinWidth + deleteWidth)`, keeping the reactive
strip narrow so text left of the cluster still focuses/edits. Accepted tradeoff below.

**Drag column far-left, always reserved.** `dragColWidth = DragHandleWidth * TextSizeScale` reserved in
both views when `DragColumnAlwaysReserved` (default true); `DragHandleX = 0`, `CheckboxX = dragColWidth`,
`TextX = dragColWidth + checkboxSize + checkboxTextGap`. Read view reserves the width but draws no grip
— this keeps checkbox/text at the same X across the Read↔Edit toggle (the "no shift" invariant).

**Single-line button height.** New `ScribeRowElement.SingleLineRowHeightFixed(...)` (built on the
existing `MeasureWrappedTextHeightFixed` primitive, floored at `MinRowHeight` like
`ScribeBlockRowCell.RowHeight`) gives the buttons a one-line height, top-aligned at `rowYs[i]`.

**Input margin symmetry.** Center single-line content within the floored `max(minHeight, contentHeight)`
via one shared `contentTop` used by both text and checkbox draw. Set the floating input's bounds
height to `rowHeights[i] - BottomOverheadFixed(config)` so the highlight stops above the ruling with a
top-matching margin (safe because the input is `Autoheight=false`).

## Risks / Trade-offs

- **Overlay hit-test vs. hover visibility.** The buttons exist in the layout even while hover-hidden,
  so their `Bounds` still capture clicks — the rightmost ~2 icon-widths of a row are not text-clickable.
  Accepted: it matches what the user sees when hovering, and text left of the cluster is unaffected.
- **`ComposeElements` override drift.** Not calling base means future engine changes to
  `GuiElementToggleButton` chrome won't apply — intended, since we're deliberately replacing that
  chrome. Documented in `VSAPI-NOTES.md` if a non-obvious base behavior is relied on.
- **No cheap unit test.** `RowTextLayout.For` carries a `CairoFont` (client API type) so it can't live
  in the API-free `Core.Tests`, and there is no Mod-side test project. Layout correctness rides on the
  manual playtest, same as `restore-row-affordance-columns`.
- **Min-size crash guard.** Single-line height floors at `MinRowHeight`, and icon `inset` derives from
  `InnerWidth`, so the smallest text-size setting cannot produce a negative icon size.
