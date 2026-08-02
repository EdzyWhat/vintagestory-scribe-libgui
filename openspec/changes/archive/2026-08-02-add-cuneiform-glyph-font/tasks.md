## 1. Bundle the glyph asset

- [x] 1.1 Run `python3 tools/build_glyphs_bundle.py` in `~/claude/glyph-forge` to produce the combined `glyphs-1.json`
- [x] 1.2 Commit it to the mod as `src/Mod/assets/scribe/textures/fonts/cuneiform-glyphs-1.json`
- [x] 1.3 Document the regen command (source repo + command) in this change's notes / a comment near the loader, and confirm the bundle contains all 47 authored characters (A–Z, 0–9, 11 punctuation; no lowercase, no space)

## 2. Core glyph model + geometry (`src/Core/Cuneiform/`)

- [x] 2.1 Add a small Core `Vec2` (or reuse an existing Core vector) — no `System.Numerics`/VS types in this layer
- [x] 2.2 Add `GlyphStroke` with `start`, `end`, `weight` and a `Corners()` method implementing the export-format perpendicular-vector corner math (`start ± p`, `end ± p`)
- [x] 2.3 Add `Glyph` (character, gridSize, left/rightWidth, left/rightPadding, kerning, ordered strokes) including the format-version migration ladder (width/advanceWidth → even split; strokes-only → bbox; empty → default)
- [x] 2.4 Add `GlyphBundle` that parses the combined JSON string via `System.Text.Json` into a character→`Glyph` map (parse from string so it stays Core-testable)

## 3. Core line-layout engine

- [x] 3.1 Add `CuneiformLineLayout` producing a construction-ordered list of positioned strokes + total advance width in grid units
- [x] 3.2 Implement per-glyph placement (`renderedX = stroke.x - (gridSize/2 - leftWidth) + penX`) and footprint advance (`leftWidth + rightWidth`)
- [x] 3.3 Implement the inter-glyph padding floor (`rightPadding(prev) + leftPadding(next)`) with kerning that only widens, never narrows past the floor
- [x] 3.4 Implement uppercase folding, a `WordGapUnits` space advance (no strokes), and safe missing-glyph handling (small gap, no throw)

## 4. Core tests (`tests/Core.Tests`)

- [x] 4.1 Corner math for a known diagonal stroke (rotated rectangle, not an AABB)
- [x] 4.2 Advance/kerning: `"AA"` vs a kerned pair; kerning clamps at the padding floor
- [x] 4.3 Uppercase folding, space word-gap, and unauthored-character no-throw
- [x] 4.4 Migration ladder branches (width, advanceWidth, strokes-only, empty) and bundle parse (47 characters, a known glyph's stroke count)

## 5. Mod render widget (`src/Mod/CuneiformText.cs`)

- [x] 5.1 `CuneiformTextRender : RenderBox` — `PerformLayout` runs the Core layout, scales grid→px by em size, sets `Size`; guard a null canvas in `PaintInternal`
- [x] 5.2 `PaintInternal` fills each stroke quad with `context.Canvas` + `SKPath` + `SharedPaint` in the theme ink color (NOT `DrawBox`); honor an optional `revealStrokeCount`
- [x] 5.3 `CuneiformTextRenderWidget : RenderObjectWidget` bridge (create/update forwards text, em size, color, reveal count, bundle)
- [x] 5.4 `CuneiformText : StatefulWidget` owning an optional `AnimationController` (create in `InitState`, dispose in `Dispose`) + `AnimatedBuilder`; idle at full reveal by default

## 6. Asset load path

- [x] 6.1 Load `cuneiform-glyphs-1.json` client-side via `api.Assets.TryGet(loc, loadAsset: true)` with the self-healing re-fetch guard (do NOT use `FontRegistry`)
- [x] 6.2 Parse once into a cached `GlyphBundle` the widget reads; re-fetch bytes if the asset was unloaded

## 7. Disable-cuneiform fallback setting

- [x] 7.1 Add `bool DisableCuneiformFont` (default false) to `src/Core/ScribePlayerSettings.cs`, carried through `Normalized()`
- [x] 7.2 Add a checkbox in `src/Mod/ScribeSettingsContent.cs` (`BuildModBehaviorSection`) + `scribe:settings-*` lang keys
- [x] 7.3 Add the single `UseCuneiform` branch point adjacent to `ScribeTaskFont.Resolve` (`src/Mod/ScribeRowConstants.cs`): when disabled, render text in the resolved task font instead of the cuneiform widget

## 8. Dev harness + prototype verification

- [x] 8.1 Add a dev-only entry point (temporary hotkey / chat command / throwaway block) that renders a demo string through `CuneiformText`, behind no player-facing feature — `/cuneiform [text]` client command opens `GuiDialogCuneiformHarness`
- [x] 8.2 Prototype step 1: render 2–3 hardcoded glyphs (no asset, no animation) — confirm crisp filled strokes, theme ink color, correct box sizing — VERIFIED in-game 2026-08-02 (`screenshots/progress/2026-08-02_07-49-09_cuneiform-harness-first-in-game-render.png`): strokes crisp/filled/AA-clean, correct ink, boxes scale per em
- [x] 8.3 Prototype step 2: render a full demo sentence from the bundle — judge legibility/spacing; tune `WordGapUnits` and any letter-spacing — VERIFIED: highly legible even at small em; spacing good, `WordGapUnits=45` kept (no tune). NB the authored set has no `&` — the demo string was corrected to authored punctuation (`CLAY, WAX!`); an unauthored char correctly renders as a missing-glyph gap
- [x] 8.4 Toggle `DisableCuneiformFont` and confirm fallback to the selected task font — VERIFIED: after toggling on and re-running `.cuneiform`, lines render in the chosen task font. NB the *dev harness* does not live-swap while open (it captures state at construction and has no settings-change rebuild hook); the real tablet dialog (Proposal C) subclasses `ScribeDialogBase`, which already rebuilds on the settings-change event, so in-place replacement is a C concern, not an A gap
- [x] 8.5 `dotnet test` green (new Core tests + existing suite) — 218/218 pass
