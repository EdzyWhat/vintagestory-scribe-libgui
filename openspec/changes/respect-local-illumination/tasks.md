# Tasks — respect-local-illumination

> Client-side render effect. The render/sampling code lives in `src/Mod/`; the brightness RESPONSE
> CURVE + the config floor are pure math/state and live in `src/Core/` (unit-tested, no VS API) —
> a deliberate scope change from the original "no Core change" plan once the user supplied a
> hand-drawn non-linear curve (see design D1/D5). NO persistence/sync/document-model change beyond
> the one config field, NO `gui` (LibGUI) fork, NO new dependency. See `design.md` for D1–D6.

## 0. Brightness response curve (D1 — Core, non-linear)

- [x] 0.1 Add `src/Core/ScribeBrightnessCurve` — a pure, unit-testable piecewise-linear evaluator
      encoding the author's hand-drawn curve (control points `0.45→0.50`, `0.814→0.85`,
      `1.00→1.00`; the `x=0` anchor is the config floor). The two high anchors are pinned to VS's
      `blockLightLevels` table: `0.814 = blockLightLevels[20]` (large lantern, V=20) → 85%, and
      `1.0` (table saturates at V≥26) → full. `Evaluate(localBrightness, floor)` interpolates
      linearly between points, lifts each point to `max(y, floor)` so a raised floor stays
      monotonic, and clamps inputs to `0..1`. Non-linear by construction — NOT a linear trace.
- [x] 0.2 Unit tests (`ScribeBrightnessCurveTests`): the plotted points map exactly, the lantern
      anchor maps to 85%, the high shoulder interpolates between 85% and full, sub-first-point
      interpolates from the floor, mid-range output exceeds a straight linear mapping, full only at
      the top (`x=1.0`), monotonic at the default and a raised floor, floor=1.0 is always-full,
      inputs clamp. All green.

## 1. Light sampler (D1)

- [x] 1.1 `src/Mod/ScribeAmbientLightSampler` reads `IBlockAccessor.GetLightRGBs(playerPos)` (XYZ =
      block-light RGB, W = sun brightness) and `IAmbientManager.BlendedAmbientColor` /
      `BlendedSceneBrightness`, and folds them into one `(brightness, RGB tint)` value: brightness =
      `max(blockLuma, sunW * sceneBrightness)` run through `ScribeBrightnessCurve`; tint = block RGB
      blended toward sky color, weighted by which is lighting the player, normalized so it skews hue
      only (level is carried by brightness). Read on the render/main thread only.
- [x] 1.2 Player position source = the client entity's `Pos.AsBlockPos` (dimension-aware, grid-aligned
      → the stable cache key); read in `OnRenderGUI` (render thread). Documented inline.
- [x] 1.3 Quantize to ~32 brightness buckets + 16 hue buckets/channel; `Sample(dt)` returns a `Shade`
      whose `Changed` flag is set iff the bucket differs from last frame, so downstream reconciles
      (and re-records the paint cache) only on a meaningful change.

## 1b. Tuning (author requests, 2026-08-12)

- [x] 1b.1 HELD light input (D1, third input): read both hand slots exactly as `EntityPlayer.LightHsv`
      does (`RightHandItemSlot`/`LeftHandItemSlot` → `Collectible.GetLightHsv`, `ColorUtil
      .MergeLightHSV`), fold in by MAX. Level maps through the live `IWorldAccessor.BlockLightLevels`
      table (so a held lantern lands on the same curve point as a placed one); hue from the item's own
      `lightHsv` (torch/lantern/oil-lamp differ for free from game data). `TryHeldLight` returns false
      when no held item emits light.
- [x] 1b.2 Reduce color/temperature effect by 1/3: `TintStrength = 2/3` pulls each hue channel partway
      back toward neutral. Level unaffected.
- [x] 1b.3 SMOOTH the transition (author: glide the output V, stretched to ~400ms). Exponential ease
      `alpha = 1 − e^(−dt/τ)`, `τ = 0.2s`, frame-rate independent, run BEFORE quantization; first
      frame adopts the target directly (no fade-up on open). First-order ease toward the CURRENT
      target (not a fixed-duration tween), so it stays continuous while the target keeps moving.
      Deliberate, bounded relaxation of the §1.3 "only on change" guard (design D7).
- [x] 1b.4 `.scribelight` dev command (`ScribeAmbientLightSampler.Describe` + registration in
      `ScribeModSystem.ClientPrefs`): one-shot unsmoothed readout of grid RGB/luma, sun/scene, held
      light, raw curve input, and curve output % — to calibrate the curve anchors against real
      in-game values (measure-don't-theorize). Client command, dot-prefix.
- [x] 1b.5 Live-read philosophy (no-bake): every light input read fresh from an engine source each
      frame so lighting mods (WarmerLighting, ImmersiveLight) are honored automatically; the only
      baked value is the author's curve shape. Documented in the sampler class doc + design/spec.

## 2. Tint widget (D2, D6)

- [x] 2.1 `src/Mod/ScribeGlobalTint` (`SingleChildWidget` + `RenderProxyBox`, modeled on
      `ScribeGearEffect`): `Canvas.SaveLayer(paint)` around `base.Paint(context)` with
      `paint.ColorFilter = SKColorFilter.CreateColorMatrix(m)`, the 4×5 matrix scaling each RGB
      channel by `brightness × tintChannel` and passing alpha through unchanged.
- [x] 2.2 Color filters cached by the packed quantized `(brightness, r, g, b)` key, NEVER disposed
      (D6). The `SaveLayer` uses its own static `LayerPaint`, never `SharedPaint`, so it can't leak
      paint state into sibling draws. An identity shade (full-bright, neutral) skips the layer
      entirely → the fully-lit dialog is pixel-for-pixel the pre-illumination look, zero cost.
- [x] 2.3 The parent reconfigures the widget (→ `UpdateRenderObject` → `MarkNeedsPaint`) only when the
      quantized shade changed (the §1.3 signal, checked in `OnRenderGUI`), so the `SKPicture` cache
      re-records only on meaningful light changes.

## 3. Insertion + surfaces (D4)

- [x] 3.1 Wrap the composed body once in `ScribeDialogBase.BuildBodyTree` — `ScribeGlobalTint` OUTSIDE
      the `Theme`, so backdrop + chrome + text flatten into one layer and the matrix shades them
      together. Driven each frame from the sampler via the dialog's `currentShade`.
- [ ] 3.2 Confirm in-game that all `ScribeDialogBase` surfaces (lectern, notebook, tablet) receive the
      shade uniformly with no per-dialog wiring. (Structurally guaranteed — single shared wrap point —
      but verify visually in the playtest, §5.4f.)

## 4. Config floor (D5)

- [x] 4.1 Add the floor field to `ScribePlayerSettings` (Core) — NOT the retired `ScribeClientConfig`:
      `IlluminationFloor` (default `0.03` = the drawn curve's `x=0` anchor; range
      `MinIlluminationFloor 0.02` .. `MaxIlluminationFloor 1.0`), with `ClampIlluminationFloor` and a
      line in `Normalized()`. Absent key → the code default (existing JSON semantics). Unit-tested.
- [x] 4.2 The floor is the curve's leftmost anchor (design D5, revised from "clamp"): the sampler passes
      `settings.IlluminationFloor` into `ScribeBrightnessCurve.Evaluate` as the `x=0` y-value, so total
      darkness renders at the floor and raising it lifts the dark end without distorting the shape.
      ConfigLib surface exposure deferred (JSON is the source of truth; no hard dep).

## 5. Docs & verification

- [x] 5.1 `VSAPI-NOTES.md` entry added — the two-input light recipe (`GetLightRGBs` W = sun brightness
      only; sky color + weather in `AmbientManager.BlendedAmbientColor`/`BlendedSceneBrightness`; point
      lights shader-only/unreadable) + the render-thread + paint-cache-quantization gotchas.
- [x] 5.2 `openspec validate respect-local-illumination --strict` passes (re-run after the design/spec
      edits — valid).
- [x] 5.3 `build/verify.sh Debug` green — build 0 warnings/0 errors, 339 Core tests, 25 Atlas
      integration tests, mod restaged into the Mods folder. Relaunch the client to load the build.
- [ ] 5.4 In-game playtest, record verdicts in `TESTING.md`:
      (a) noon outdoors → GUI at full brightness, visually unchanged from today;
      (b) rain outdoors → GUI dimmer than clear noon;
      (c) dark cave + placed torch → GUI warm/orange and dimmed to the torch level;
      (d) total darkness, default config (floor 0.03) → GUI near-black, faintly legible;
      (e) total darkness, floor lowered to minimum → GUI near-black / unreadable;
          and floor raised to 1.0 → GUI stays full-bright regardless (opt-out works);
      (f) all three surfaces (lectern/notebook/tablet) shade identically under the same light;
      (g) typing/clicking/scrolling/focus unaffected by the shade; no tint bleed into other
          GUIs in the same frame;
      (h) MEASURE the paint-cache re-record cost on the parchment backdrops while light changes
          (day/night transition, walking past torches) — confirm no unacceptable hitching; if it
          hitches, coarsen quantization / throttle cadence (NOT a LibGUI fork);
      (i) CONFIRM THE CURVE FEELS RIGHT — the point of the hand-drawn shape: darkness is punishing
          but a torch is "enough," and the mid-range isn't muddy. Tune the control points in
          `ScribeBrightnessCurve` if the in-game feel disagrees with the paper sketch.
- [ ] 5.5 CALIBRATE the curve anchors against real values: stand next to a placed lantern, run
      `.scribelight`, read the RAW curve input and confirm the output lands ~85% (V=20 territory).
      Adjust the `ScribeBrightnessCurve` points if the measured value disagrees.
- [ ] 5.6 Playtest the tuning (§1b), record verdicts in `TESTING.md`:
      (a) HELD light in a dark cave — a held offhand lantern lifts the GUI to ~lantern level, a held
          torch proportionally lower, and nothing held stays at the darkness floor;
      (b) per-item COLOR TEMPERATURE — held torch vs. lantern vs. oil lamp read visibly different hues;
      (c) TINT −1/3 — the warm/cool cast reads present but gentler than before, not an aggressive wash;
      (d) SMOOTHING — walking between light and shadow glides the brightness over ~400ms rather than
          snapping; the glide stays smooth when you keep moving into greater/lesser light (no kink or
          restart); a newly opened dialog shows the current light immediately (no fade-up).
