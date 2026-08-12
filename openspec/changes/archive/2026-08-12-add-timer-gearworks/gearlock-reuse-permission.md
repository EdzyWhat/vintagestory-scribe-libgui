# Gearlock Firearms — gear-asset reuse permission (task 1.1)

The Timer-tab gearworks' small/flanking gear geometry and texture were **derived from the
Gearlock Firearms mod's gear assets**, then re-skinned to Scribe's palette.

- **Source mod:** Gearlock Firearms by **JeanPierre** — https://mods.vintagestory.at/show/mod/60282
- **Permission:** the mod page / author license permits reuse and modification of the gear
  assets by other mods. Scribe copies + modifies (re-skins) the gear geometry/texture; it does
  **not** reference Gearlock at runtime (no code/mod dependency — the assets are baked into
  Scribe's own `assets/scribe/textures/gui/`).
- **Attribution:** recorded in the repo-root `CREDITS` file under *Inspiration & derived assets*
  (JeanPierre is also already credited there for Wanderer's Sketchbook).

## What was actually shipped vs. reused

- **Flanking small gears:** derived from Gearlock's gear PNG, re-skinned to the small brown/steel
  cog look (`gui/gear-temporal-small.png` and the teal `gui/gear-temporal-large.png`).
- **Escape / great wheel:** NOT a Gearlock asset — generated procedurally in-repo
  (`ScribeGearTexture.GreatWheel`, pure SkiaSharp), in the small gear's blocky/steel spirit.
- **Rendering technique:** Gearlock renders its gears with raw GL (`Render2DTexture` + `GlRotate`
  in `OnRenderGUI`); Scribe re-implemented the meshing idea (single monotonic driver × per-gear
  `sign, ratio`) in the LibGUI Skia widget tree instead (see `design.md` D2/D3). The raw-GL path
  is recorded only as a documented fallback.
