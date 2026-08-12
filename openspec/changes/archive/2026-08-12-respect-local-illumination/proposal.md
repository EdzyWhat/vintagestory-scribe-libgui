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
- Drive brightness through a hand-drawn **non-linear response curve** (`ScribeBrightnessCurve`,
  a pure unit-tested Core file), NOT a linear trace of local light — so darkness is punishing
  but a little light already reads comfortably, and the GUI reaches full brightness slightly
  before full ambient light. Only hue tracks the light directly.
- Combine the three light sources the engine itself uses so the GUI matches how the world
  looks: `IBlockAccessor.GetLightRGBs(playerPos)` (block-light RGB with torch warmth baked in,
  plus a sun-brightness scalar), `IAmbientManager.BlendedAmbientColor` / `BlendedSceneBrightness`
  (sky hue and weather/rain darkening, which are *not* in the block grid), and the player's own
  **held light** (`RightHandItemSlot`/`LeftHandItemSlot` → `Collectible.GetLightHsv`, merged as
  `EntityPlayer.LightHsv` does) — which the block grid at the player's own block does *not*
  contain, so an offhand torch/lantern would otherwise leave the GUI dark. Held light is folded
  in by MAX (VS light is max-based, not additive); its level maps through the live
  `BlockLightLevels` table so a held lantern lands on the same curve point as a placed one, and
  its hue comes from the item's own `lightHsv` so a held torch, lantern, and oil lamp each read
  their own color temperature.
- **Read the engine's live light, never bake.** Every input is sampled fresh from an engine
  source each frame, so any mod that changes the light where the player stands (WarmerLighting,
  ImmersiveLight, a future one) is honored automatically — the GUI shades off the engine's
  computed light, not a snapshot of what vanilla emits.
- Soften the color/temperature effect to two-thirds strength (`TintStrength`), and **smooth**
  the transition (exponential ease, τ=0.2s → ~400ms glide) so brightness glides as the player
  moves through changing light instead of snapping between quantization buckets.
- Apply the shade as a single color transform over the whole composed dialog using a mod-side
  `SaveLayer` + `SKColorFilter.CreateColorMatrix` render wrapper — the same mechanism the
  shipped `ScribeGearEffect` already uses — so brightness scaling and color temperature are
  one filter, applied once, and can brighten as well as darken. **No fork of the `gui`
  dependency.**
- Quantize the sampled light value (coarse brightness steps + hue buckets) before it drives
  the filter, so LibGUI's cached paint picture only re-records when the player's light
  *meaningfully* changes rather than every frame.
- Add a **client-config minimum-brightness floor** (`ScribePlayerSettings.IlluminationFloor`,
  default `0.03`) which is the response curve's leftmost (x=0) anchor, so total darkness is
  dim-but-faintly-legible by default; lowering it approaches unreadable and raising it to `1.0`
  opts out of the effect entirely. Client-side, per-machine, no sync.
- Applies uniformly across all Scribe surfaces that inherit `ScribeDialogBase` (lectern,
  notebook, tablet, and future documents) by wrapping the shared body once.

Non-goals (explicitly out of scope; noted for a possible follow-up):

- **Held-light flicker / Immersive Lanterns unification** is a *soft, optional* follow-up, not
  part of this change. The steady level+hue of a held torch/lantern/lamp IS now supported (via
  the held-light input above); its per-frame *flicker* is not yet synthesized. If a player has
  Immersive Lanterns installed, matching its modifiable flicker on the held light (so the GUI's
  flicker unifies with the player's chosen setting) is a deferred, dependency-optional
  enhancement — never a hard dependency. When a held IL item drives the light, IL's flicker
  *supersedes* this change's smoothing **on that held contribution only** (the 400ms ease is a
  low-pass filter that would otherwise flatten the flicker); environmental transitions stay
  smoothed. Placed-object flicker is out of scope by choice.
- **Placed dynamic point-lights' flicker** (the shader-only `IPointLight` system) is not
  supported: it never writes into the block-light grid `GetLightRGBs` samples, and there is no
  public API to read active point lights (the list is `internal` on `ClientMain`). A *steady,
  placed* torch/lantern is picked up via the baked grid; only its per-frame flicker is missed.
- No change to persistence, sync, or the document model. The only `src/Core/` additions are
  the pure brightness response curve (`ScribeBrightnessCurve`) and the one config floor field
  on `ScribePlayerSettings` — both unit-tested, neither references the VS API. Everything that
  touches live light stays in `src/Mod/`. This is otherwise purely a client-side render effect.

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

- **New code (`src/Mod/`):** a light-sampling helper (`ScribeAmbientLightSampler` — reads
  `GetLightRGBs` + `AmbientManager` + held light, folds them into a smoothed, quantized
  brightness+RGB value on the render thread), a `ScribeGlobalTint` render wrapper (`SaveLayer` +
  `CreateColorMatrix`, filter cache modeled on `ScribeGearEffect`, never disposed per-frame), its
  insertion at the shared body-wrap point in `ScribeDialogBase`, and a `.scribelight` dev
  diagnostic command (one-shot readout of the sampled light, for calibrating the curve anchors
  against real in-game values).
- **New code (`src/Core/`):** `ScribeBrightnessCurve` (pure piecewise-linear response curve) and
  the `IlluminationFloor` field on `ScribePlayerSettings` — both unit-tested, no VS API.
- **Config:** one new `ScribePlayerSettings` field (`IlluminationFloor`, minimum-brightness
  floor / curve anchor); on-disk JSON at `ModConfig/scribe-client-config.json` gains a key.
  Absent key falls back to the code default.
- **Dependencies:** none added. Uses vanilla `VintagestoryAPI` (`IBlockAccessor`,
  `IAmbientManager`, `EntityPlayer.RightHand/LeftHandItemSlot`, `Collectible.GetLightHsv`,
  `IWorldAccessor.BlockLightLevels`, `ColorUtil`) and the already-referenced SkiaSharp. No `gui`
  (LibGUI) fork. Because every light input is read live, lighting mods (WarmerLighting,
  ImmersiveLight) are supported without a dependency on them.
- **Performance:** per-frame light read is cheap (one chunk lookup + HSV→RGB). The real cost
  is LibGUI re-recording its paint picture when the tint value changes; quantization bounds
  this to meaningful light changes. Cost on the pixel-art parchment backdrops must be measured
  in-game.
- **Risk / prior art:** reuses the proven `ScribeGearEffect` color-filter pattern and must
  follow the documented `SharedPaint` save/restore discipline (`ScribeBackdropPaintReset`) to
  avoid paint-state leaks. Unrelated to the known white-flash (a terrain-pass dropout *behind*
  the GUI).
