## Why

The upcoming early-game **tablet** tier wants a distinctive, ancient-looking script that carves
onto clay/wax rather than yet another ordinary typeface. A real bundled `.ttf`
(`bundled-font-rendering`) cannot express carved wedge-strokes that animate stroke-by-stroke, and
whether such a script is even legible and pleasant in-game is the single biggest unknown in the
tablet plan. This change de-risks that unknown **on its own**, ahead of any tablet item or dialog:
it renders text as filled rectangle strokes from bundled glyph geometry (sourced from the
`glyph-forge` project), proving the mechanic before anything depends on it.

## What Changes

- **New Core glyph model + line-layout math** (pure BCL, no VS API, unit-testable): parse
  `glyph-forge` stroke JSON into a glyph model, compute each stroke's rectangle corners, and lay out
  a string into positioned strokes with proportional advance, padding floors, and kerning.
- **New bundled font asset**: a single combined glyph-geometry JSON, produced by
  `glyph-forge/tools/build_glyphs_bundle.py`, committed under the mod's scanned `textures/` asset
  tree and loaded client-side. The authored set is 47 characters (A–Z **uppercase only**, 0–9, and
  11 punctuation marks) — **no lowercase and no space glyph** — so the layout folds input to
  uppercase and advances a fixed gap for spaces.
- **New Mod-side custom render widget** (`CuneiformText`) that paints the arbitrary-angle stroke
  quads on the LibGUI Skia canvas, with an optional stroke-by-stroke reveal animation (idle at full
  reveal for this first prototype).
- **New client setting** to disable the cuneiform font and fall back to the player's selected task
  font, with a single resolution branch point.
- **A small dev harness** to view the rendered glyphs in-game, so the legibility/spacing of the
  script can be judged and tuned quickly.
- **Non-goals (explicitly deferred):** the tablet item, the tablet dialog, and all deferred tablet
  mechanics (firing→archive, water damage, carry-forward migration, wax-wipe, and the
  stylus-in-offhand edit gate). This change ships only the font capability and its prototype.

## Capabilities

### New Capabilities
- `cuneiform-glyph-font`: rendering text as filled-rectangle stroke glyphs from a bundled glyph
  geometry asset — the game-agnostic glyph model and proportional line-layout math, the bundled
  asset load path, the custom Skia-canvas render widget with an optional stroke-by-stroke reveal,
  uppercase folding and space handling for the authored character set, and a client setting that
  disables the script and falls back to the player's task font.

### Modified Capabilities
<!-- None. The fallback setting is font-specific behavior owned by the new capability, distinct from
     the existing client-theme-preference (pixel-art theme) and bundled-font-rendering (TTF) specs. -->

## Impact

- **New Core code:** `src/Core/Cuneiform/` (glyph model, stroke geometry, glyph bundle parse,
  line-layout math) plus xUnit tests in `tests/Core.Tests`. Honors the Core invariant (no VS API).
- **New Mod code:** `src/Mod/CuneiformText.cs` (a 3-class custom render widget mirroring
  `ScribeMultilineField`'s `RenderBox`/`RenderObjectWidget`/`StatefulWidget` pattern), asset loading
  in the client-init path, and a dev harness entry point.
- **New asset:** `src/Mod/assets/scribe/textures/fonts/cuneiform-glyphs-1.json` (committed bundle;
  regenerated via `glyph-forge/tools/build_glyphs_bundle.py`). Filed under `textures/` because VS
  only scans its fixed `AssetCategory` folders.
- **Modified:** `src/Core/ScribePlayerSettings.cs` (new `DisableCuneiformFont` flag) and
  `src/Mod/ScribeSettingsContent.cs` (a checkbox), with the disable branch resolving to the task
  font via the existing `ScribeTaskFont.Resolve` chokepoint in `src/Mod/ScribeRowConstants.cs`.
- **Dependencies:** none added — uses only the already-required `gui` (LibGUI) mod and its bundled
  SkiaSharp. The glyph geometry does **not** go through `FontRegistry`/`RegisterCustomFonts` (that
  path is TTF-only).
- **CI:** the Core suite gains the cuneiform-layout tests (runs on cloud runners, no game install
  needed).
