## Why

The glyph-forge tool now exports a tuned readability preset for each of the 10 tablet views
(clay blue/red/fire × wet/hard/fired, plus wax-wet): body ink, link ink, cuneiform stroke
weight, and outer-glow (color/strength/blur/offset). These values are the product of a
deliberate contrast pass and should become the source of truth. But they don't fit the
current code cleanly: tablet ink/link are resolved **per-material only** (no drying-state
axis), the hard/fired glow is a **single shared halo** rather than per-clay, and there is
**no home at all** for a glow offset or a per-view stroke-weight scale. Adopting the presets
means refining the data model, not just pasting numbers — which is also the long-deferred
"one bundle per view" tablet-styling refactor.

## What Changes

- Introduce a single `(material, state)`-keyed **tablet readability bundle** — ink, link ink,
  stroke-weight scale, and glow — resolved **once** when the tablet dialog opens and
  decomposed by the page into the seams it already has. The 10 glyph-forge JSON exports become
  its seed table.
- **Theme gains a drying-state axis.** `ScribeTheme.ForTablet(material, pixelArt)` →
  `ForTablet(material, state, pixelArt)` so the bundle's `bodyInk` lands on the theme's
  `OnSurface` role and can differ wet/hard/fired (fired ink is darker). `Primary`, `Secondary`,
  and the rest of the `ColorScheme` are **untouched** — the presets carry no accent/panel data.
- **Glow becomes per-`(material, state)` with a directional offset.** `CuneiformGlow` gains
  `OffsetXFraction`/`OffsetYFraction` fields (threaded into the Skia paint). The lookup returns
  a distinct halo per clay per state — with the simplification that **all wet clays share one
  dark halo** and stroke-weight/offset are **purely state-driven** (0.9/1.0/1.1; 0/0.05).
- **New per-view stroke-weight scale** multiplies the cuneiform stroke weight (0.9 wet → 1.1
  fired). `linkInk` flows into the existing `ScribeRowStyle.LinkColor` seam, now per-state.
- `backgroundMask` is ignored (opacity 0 in every export; a no-op today).
- **Supersedes** the pending in-game tuning gates of `add-tablet-state-glow-modifier` (whose
  "one shared light halo per state" decision this **reverses** in favor of per-clay halos) and
  `tablet-text-visibility`. Their code stands; this change replaces their placeholder values
  and folds their remaining tuning into one authoritative pass.

## Capabilities

### New Capabilities
- `tablet-readability-style`: a single `(clay-material, drying-state)`-keyed bundle (ink, link
  ink, stroke-weight scale, glow) that is the source of truth for tablet text readability,
  resolved once per dialog open and decomposed into the theme, row style, and cuneiform render.

### Modified Capabilities
- `cuneiform-contrast-glow`: glow parameters become per-clay-material **and** per-drying-state
  and gain a directional **offset**; wet clays share one dark halo, hard/fired use per-clay
  light halos (reversing the shared-halo model), all sourced from the readability bundle.
- `tablet-dialog`: the tablet theme's ink (`OnSurface`) and per-material **link ink** SHALL be
  resolved with the drying **state** as an input (fired ink darker), sourced from the bundle,
  rather than per-material only.

## Impact

- **Code:** `src/Mod/ScribeTheme.cs` (`ForTablet`/`ForTabletLink` gain state; new bundle table),
  `src/Mod/CuneiformGlow.cs` (`CuneiformGlow` offset fields; `CuneiformGlowTable.For` sources the
  bundle), `src/Mod/GuiDialogScribeTablet.cs` (resolve bundle once; thread state + stroke scale),
  the cuneiform render objects (`ScribeCuneiformField`/`ScribeCuneiformTitleField` — offset +
  stroke-weight scale into the Skia paint), `src/Mod/ScribeRowStyle.cs` (stroke-scale carrier if
  needed). No `src/Core/` change (Core stays API-free; stroke weight is a Mod-side multiply on
  the existing `GlyphBundle` weight).
- **Data:** the 10 `~/Downloads/readability-params-*.json` exports are the seed values (baked as
  constants; not shipped/loaded at runtime).
- **Specs:** one new + two modified (above). On archive, reconcile the `cuneiform-contrast-glow`
  and `tablet-dialog` headers to this end-state (last-writer-wins), since the two superseded
  in-flight changes also touch them — see `[[openspec-archive-order-header-drift]]`.
- **No dependency or persistence changes**; visuals only, gated on Pixel-Art Display.
