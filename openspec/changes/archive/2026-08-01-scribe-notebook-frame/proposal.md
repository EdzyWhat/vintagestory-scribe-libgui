## Why

The `scribe-gui-backdrops` mechanism works, but the Lectern still shows a flat-tan placeholder stretched
to fill the 567×520 window. Real illustrated notebook art now exists (1024×1160, portrait, aspect 0.883),
and because LibGUI's `BoxStyle.Texture` is hard-coded stretch-to-fill, dropping portrait art into the
landscape window would skew it. Sizing the dialog to the art's aspect turns stretch-to-fill into a
distortion-free uniform scale — so the Lectern can read as a physical notebook whose illustrated cover
frames the functional GUI laid out inside it, using the backdrop code we already have.

## What Changes

- Swap the flat-tan placeholder for real notebook art (`scribe-notebook.png`, 1024×1160); repoint the
  Lectern's backdrop spec to it. The existing self-loaded/cached `SKBitmap` path is unchanged.
- Introduce a **proportional layout driven by a single width `W`** ("Pixel Art Size"): the whole dialog is
  an `OuterArtBox` of `W × H` (H = W·1160/1024) whose backdrop art fills it un-distorted, and every inner
  structure's size is derived from `W`. A small `LecternLayout` value-record precomputes the derived
  dimensions so the widget tree reads as ratios, not arithmetic.
- Lay the functional GUI out INSIDE the OuterArtBox as nested bands/columns so the art frames it:
  - a **TitleBar** band (`W × 0.13H`) at the top — the draggable region — containing a bottom-anchored,
    centered **TitleTextButtons** row (`0.75W × 0.065H`): title text on the left (window-text ×1.1),
    a right-aligned button group using the custom SVGs (the delete SVG reused as the close button at
    1.4× the delete size), each button carrying a tooltip;
  - a **SectionInnerBox** (`0.9W × 0.8H`, centered) below it, split into three full-height columns —
    **SectionLeftCol** (`0.0675W`), **LecternTasksBox** (`0.765W`, the existing scrolling read/editor
    content), **SectionRightCol** (`0.0675W`, a vertical stack of icon buttons: Scribe Settings, Read
    view, Edit view, Pinned tasks — icon-only with tooltips);
  - the remaining ~7% of `H` is bottom margin below the SectionInnerBox.
- **Drop LibGUI's `WindowFrame`** for this dialog: build the OuterArtBox tree directly so the art is the
  outermost box (not a strip below a stock bar). Dragging comes from `WindowConfig.DragHandleHeight` sized
  to the TitleBar band; closing is a close-SVG `IconButton`. **BREAKING** (visual/layout): the dialog is
  materially different in size/shape and no longer uses the stock frame or resize.
- Add a permanent **Pixel Art Size** (`W`) numeric preference to Scribe Settings: increments of 10,
  bounded {300, 1000}, read fresh each build so changing it re-lays-out the open Lectern live.

## Capabilities

### New Capabilities
<!-- None. This extends existing behavior; the backdrop mechanism itself already exists in scribe-gui-backdrops. -->

### Modified Capabilities
- `lectern-gui-shell`: the dialog is an art-sized `OuterArtBox` (sized to the backdrop's aspect ratio,
  non-resizable) that frames a nested proportional layout (TitleBar band + a three-column SectionInnerBox),
  replacing the stock `WindowFrame` with a directly-built tree whose drag/close are wired without it.
- `settings-tab`: adds a permanent **Pixel Art Size** preference (the layout's driving width `W`) in the
  Appearance section, applied live.

## Impact

- **Mod (`src/Mod/`)**: `GuiDialogScribeLecternLibGui` — window config (size from `W`, `Resizable=false`,
  `DragHandleHeight` = TitleBar height); `Build()` rebuilt as the OuterArtBox → Column[TitleBar, Row[3
  cols]] tree with the backdrop `Container` at the root; new `LecternLayout` record; close/nav as
  `IconButton`s with tooltips; reads `W` fresh. `ScribeBackdrop.cs` (repoint `LecternPage`).
  `ScribeSettingsContent.cs` (Pixel Art Size field). `assets/scribe/lang/en.json` (new label/help + any new
  button tooltips).
- **Core (`src/Core/`)**: `ScribePlayerSettings` gains one plain int field `PixelArtSize` (default 600) +
  `Min/Max` consts (300/1000) + `ClampPixelArtSize` + `Normalized()` wiring (no VS API — Core stays
  game-agnostic).
- **Assets**: new `assets/scribe/textures/gui/scribe-notebook.png`; the flat `lecternbackdrop.png` stays as
  the reserved placeholder.
- **LibGUI (`gui`, existing hard dep)**: no source changes; we stop using `WindowFrame` for this dialog and
  compose `Container`/`Column`/`Row`/`SizedBox`/`Align`/`IconButton` directly.
- **No new dependencies.** Verification is in-game only (Core suite can't reach `src/Mod/` GUI or the VS API).
- **Out of scope** (tracked separately, from the last playtest): the OFF-background reversal and the
  pinned-row contrast bump.
