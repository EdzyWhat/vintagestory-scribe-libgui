## Why

The row-list rework (S1 read view + S2 edit-in-place) replaced the old composer-based rows —
`ScribeBlockRowCell.Compose`, which stacked a checkbox + text input + `ScribeHoverIconButton`
delete/pin + `ScribeDragHandleElement` — with a single custom `ScribeRowElement` per row that bakes
checkbox + text + ruling into its own texture and blits it inside the dialog's `BeginClip` scissor.
That fixed the scroll-pop and clip-bleed problems, but the shared row layout has no icon gutters, so
it **dropped the per-row delete and pin icons and the drag handle** — a known, flagged regression
from the S2 playtest. The `lectern-gui-shell` spec already *requires* these affordances
("Row icons are hover-conditional", "Task rows expose a pin-toggle affordance"), so the
implementation is currently out of compliance with its own spec.

## What Changes

- Restore the per-row **delete** and **pin** icons (hover-conditional) and a **drag-handle (grip)**
  gutter to the editor view, built on the new `ScribeRowElement` architecture as separate composer
  interactive elements (reusing the intact-but-dead `ScribeHoverIconButton`).
- `RowTextLayout` reserves right-anchored gutter columns so the text column shrinks to make room;
  column widths **scale with `TextSizeScale`** (mirroring the checkbox), so they grow/shrink with row
  text rather than staying fixed.
- Icons use the registered custom SVG codes `scribepin` / `scribeclose` / `scribegrip` (shipped
  unwired by the archived `add-custom-svg-row-icons`).
- **Functionality is stubbed**: clicking delete/pin calls a logging no-op. Real server-authoritative
  delete/pin messaging is deliberately a later change.
- Read view is unchanged (checkbox-only per spec). Editor view only.
- Absorbs `lectern-gui-quick-edit-affordances` §6 (icon-column `TextSizeScale` scaling), whose
  §6.1/6.2 tasks target the now-dead `ScribeBlockRowCell.TextWidth`; that change is rescoped as part
  of this one's closeout.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `lectern-gui-shell`: adds a requirement that editor rows reserve a **drag-handle affordance
  column** (no current requirement describes the grip — the reorder *feedback* lives in the parked
  `lectern-drag-reorder-feedback` change, but the column's existence belongs here). The existing
  hover-conditional-icons and pin-toggle requirements are unchanged in wording — this change restores
  the implementation that satisfies them.

## Impact

- `src/Mod/RowTextLayout.cs` — `For()` gains a `reserveAffordances` param and reserves scaled
  pin/delete/grip gutters; new `PinX/PinWidth/DeleteX/DeleteWidth/DragHandleX/DragHandleWidth` fields.
- `src/Mod/ScribeRowElement.cs` — `RowHeightFixed` gains `reserveAffordances` (narrower editor text
  column → correct height); new `IsInIconGutter` + mouse yield so gutter clicks reach the icons.
- `src/Mod/GuiDialogScribeLectern.cs` — add pin/delete/grip icon elements + tooltips in
  `ComposeEditorView`'s row loop; update the `RowHeightFixed`/`RowTextLayout.For` call sites; add the
  `OnEditViewDeleteRow`/`OnEditViewTogglePin` stubs.
- `src/Mod/ScribeBlockRowCell.cs` — salvage source only (`ScribeHoverIconButton`, the `PinKey`/
  `DeleteKey`/`DragHandleKey` helpers); no live behavior change.
- `src/Mod/ScribeClientConfig.cs` — read-only (existing `DeleteWidth`/`PinWidth`/`DragHandleWidth`/
  `HoverTextWidth` knobs).
- No `Core` change, no persistence/network change, no new dependency. CI (Core-only) is unaffected;
  verification is a Mac restage + manual playtest, since Core.Tests has no GUI-layout coverage.
