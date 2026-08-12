## Why

Scribe's GUIs render at full, uniform brightness regardless of the player's surroundings —
blindingly bright when the player is standing in total darkness. This breaks immersion and
undercuts the value of carrying a light source: reading your notebook in a pitch-black cave
should require a torch, and the page should take on the warm cast of that torch, the neutral
cast of noon daylight, or the dimness of a rainy afternoon. The engine already knows the
light reaching the player at every moment; the GUI simply ignores it.

## What Changes

- Sample the light reaching the player's position each frame — both **brightness** and
  **color/temperature** — and shade the composed Scribe GUI to match it. In bright, neutral
  daylight the GUI looks as it does today; under a torch it warms and dims; in darkness it
  drops toward unreadable.
- Combine the two light sources the engine itself uses so the GUI matches how the world
  looks: `IBlockAccessor.GetLightRGBs(playerPos)` (block-light RGB with torch warmth baked in,
  plus a sun-brightness scalar) and `IAmbientManager.BlendedAmbientColor` /
  `BlendedSceneBrightness` (sky hue and weather/rain darkening, which are *not* in the block
  grid).
- Apply the shade as a single color transform over the whole composed dialog using a mod-side
  `SaveLayer` + `SKColorFilter.CreateColorMatrix` render wrapper — the same mechanism the
  shipped `ScribeGearEffect` already uses — so brightness scaling and color temperature are
  one filter, applied once, and can brighten as well as darken. **No fork of the `gui`
  dependency.**
- Quantize the sampled light value (coarse brightness steps + hue buckets) before it drives
  the filter, so LibGUI's cached paint picture only re-records when the player's light
  *meaningfully* changes rather than every frame.
- Add a **client-config minimum-brightness floor** (`ScribeClientConfig`) so total darkness is
  dim-but-faintly-legible by default, with the punishing "effectively unreadable" end
  available to players who want it. Client-side, per-machine, no sync.
- Applies uniformly across all Scribe surfaces that inherit `ScribeDialogBase` (lectern,
  notebook, tablet, and future documents) by wrapping the shared body once.

Non-goals (explicitly out of scope; noted for a possible follow-up):

- **Flickering / dynamic point-light mods** (e.g. Saltywater's Immersive Lanterns) are *not*
  supported. VS dynamic lights are a shader-only `IPointLight` system that never writes into
  the block-light grid `GetLightRGBs` samples, and there is no public API to read active point
  lights (the list is `internal` on `ClientMain`). A *steady, placed* torch/lantern is picked
  up via the baked grid; only its per-frame flicker is missed. Reading point lights would
  require reflecting into a private engine field — deferred as a separate future change.
- No change to `src/Core/`, persistence, sync, or the document model. This is purely a
  client-side render effect.

## Capabilities

### New Capabilities
- `gui-ambient-illumination`: the Scribe GUI's rendered brightness and color temperature track
  the real light reaching the player (block/torch light, sun brightness, sky color, weather
  darkening), with a configurable minimum-brightness floor for total darkness.

### Modified Capabilities
<!-- None. The tint is a new render effect layered over the existing dialog chrome; it changes
     no existing spec's requirements (scribe-dialog-base, gui-backdrop, client-theme-preference
     all keep their current contracts). -->

## Impact

- **New code (`src/Mod/`):** a light-sampling helper (reads `GetLightRGBs` +
  `AmbientManager`, combines them into a quantized brightness+RGB value on the render thread),
  a `ScribeGlobalTint` render wrapper (`SaveLayer` + `CreateColorMatrix`, filter cache modeled
  on `ScribeGearEffect`, never disposed per-frame), and its insertion at the shared body-wrap
  point in `ScribeDialogBase`.
- **Config:** one new `ScribeClientConfig` field (minimum-brightness floor); on-disk JSON at
  `ModConfig/scribe-client-config.json` gains a key. Absent key falls back to the code default.
- **Dependencies:** none added. Uses vanilla `VintagestoryAPI` (`IBlockAccessor`,
  `IAmbientManager`, `IGameCalendar`) and the already-referenced SkiaSharp. No `gui`
  (LibGUI) fork.
- **Performance:** per-frame light read is cheap (one chunk lookup + HSV→RGB). The real cost
  is LibGUI re-recording its paint picture when the tint value changes; quantization bounds
  this to meaningful light changes. Cost on the pixel-art parchment backdrops must be measured
  in-game.
- **Risk / prior art:** reuses the proven `ScribeGearEffect` color-filter pattern and must
  follow the documented `SharedPaint` save/restore discipline (`ScribeBackdropPaintReset`) to
  avoid paint-state leaks. Unrelated to the known white-flash (a terrain-pass dropout *behind*
  the GUI).
