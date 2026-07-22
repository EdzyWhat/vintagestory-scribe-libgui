## Context

The row-list rework moved the lectern's rows onto a single custom `ScribeRowElement` per row that
bakes checkbox + text + ruling into a private texture and blits it in the interactive pass (so the
dialog's `BeginClip` scissor clips it natively — solving the old scroll-pop/clip-bleed). The old
per-row delete/pin icons (`ScribeHoverIconButton`) and drag handle (`ScribeDragHandleElement`) were
part of the now-dead `ScribeBlockRowCell.Compose` path and were dropped. Both classes still exist in
`src/Mod/ScribeBlockRowCell.cs`, intact but unreferenced by the live dialog.

`RowTextLayout` is the single source of horizontal truth (read by `ScribeRowElement.ComposeElements`,
`RowHeightFixed`, `IsCheckboxHit`, and the floating input's placement). It currently reserves only
`[checkbox][gap][text]`.

## Goals / Non-Goals

**Goals:**
- Restore delete + pin (hover-conditional) and a drag-handle grip column to the editor view, on the
  new `ScribeRowElement` architecture, with correct hit-testing, hover, scroll, and clip behavior.
- Columns scale with `TextSizeScale`.
- Icons use the registered custom SVGs (`scribepin`/`scribeclose`/`scribegrip`).
- Keep the label↔floating-input handoff jump-free after the text column narrows.

**Non-Goals:**
- Real delete/pin behavior — clicks are stubbed (logging no-op). Server-authoritative messaging is a
  later change.
- Drag-reorder *feedback* (lift-ghost/insertion-indicator/drop-settle) — owned by the parked
  `lectern-drag-reorder-feedback`; this only restores the grip column it will ride on.
- Read-view affordances — read view stays checkbox-only.

## Decisions

**1. Separate composer elements, not baked-into-texture.** Add the icons as their own
`ScribeHoverIconButton` elements per row, under the same `contentBounds` parent as the row and the
floating input. They inherit the scroll `fixedY` shift and native `BeginClip` clipping for free.
Baking into the row texture would force a re-bake on every hover enter/leave and hand-rolled per-icon
hit-testing — reintroducing exactly the state churn the texture design avoids.

**2. Reserve gutters from the right edge in `RowTextLayout`.** Order after text:
`[text][pin][delete][grip]`, all right-anchored. Widths scale by `TextSizeScale`; pin is task-only
(width 0 on notes), so a note's delete/grip land at the same X as a task's — columns line up down the
list. `textWidth = pinX - textX` shrinks accordingly. A `reserveAffordances` bool gates all of this
(true = editor view) so the read view keeps its full-width text and unchanged height.

**3. `RowHeightFixed` must know `reserveAffordances`.** Editor rows have a narrower text column, so
they measure taller. Thread the flag through `RowHeightFixed` → `RowTextLayout.For`, and update the
three call sites (read = false, editor = true, live re-measure = true) plus the input-placement
`For` call (true) so the floating input aligns to the narrowed label.

**4. Reuse `ScribeHoverIconButton` with the custom SVG codes.** Pin: `"scribepin"`,
`toggleable: true` (mandatory — the base `GuiElementToggleButton` resets `On=false` on any dialog
mouse-up when not toggleable, wiping seeded pinned state), `HoverRegion = rowBounds`, seed
`On = block.Pinned` after `.Compose()`. Delete: `"scribeclose"`, momentary, `HoverRegion = rowBounds`.
Grip: a `ScribeHoverIconButton("scribegrip", _ => {}, ...)` no-op — it gets the SVG + hover-hide for
free, and leaves `ScribeDragHandleElement`'s drag callbacks untouched for the reorder change to adopt
later. `HoverRegion` is the same `rowBounds` instance the row element uses, so `CalcWorldBounds`/scroll
update it automatically (no `CurParentBounds.WithChild` needed — that was only for the old math-only
bounds).

**5. Row yields gutter clicks.** The full-width row element is added before the icons, so on
mouse-down `GuiComposer` reaches it first and its `base.OnMouseDownOnElement` would consume the click.
Extend the existing focused-text-column yield: add `IsInIconGutter(args)` (mode==Edit and X right of
the text column) and `return` without setting `Handled` in `OnMouseDownOnElement`, and guard the
`onRequestEdit` fire in `OnMouseUpOnElement` so a gutter click never floats the input onto the row.

**6. Stubs slot in cleanly.** `OnEditViewDeleteRow`/`OnEditViewTogglePin` log via
`capi.Logger.VerboseDebug` with a `TODO(follow-up)`. `ScribeDocument.DeleteBlock`/`TogglePinned`
already exist, and the deferred-recompose machinery (`pendingRecomposeAction`) already handles the
mid-dispatch reentrancy the real handlers will need, so the later change is a small body swap.

## Risks / Trade-offs

- **Blur-on-icon-click**: an icon mouse-down may blur the focused input (commits via `OnEditBlur`).
  Harmless for stubs; the follow-up may want the pin button to defer its action to mouse-up to
  preserve the caret. Flagged for playtest.
- **Scissor clobber**: icons render after the input; if `GuiElementToggleButton` disables the scissor
  it could bleed the next row below the list. Mitigation is ready — `ScribeHoverIconButton` already
  overrides `RenderInteractiveElements`, so a `PushScissor(InsideClipBounds)/PopScissor()` re-assert
  can be added there if observed.
- **No GUI test coverage**: Core.Tests can't catch layout regressions (`RowTextLayout`/`ScribeRowElement`
  are untested). Mitigate with an optional pure-`RowTextLayout.For` unit test (no engine deps) and rely
  on the manual playtest for the rest.
- **Min text size**: `MinRowHeight` (=20) already floors the row height that prevents the
  negative-icon-size SVG rasterize crash; the scaled gutter widths shrink but the floor holds.
