# Tasks — respect-local-illumination

> Client-side render effect only. NO `src/Core/` change, NO persistence/sync/document-model
> change, NO `gui` (LibGUI) fork, NO new dependency. All code lives in `src/Mod/`. See
> `design.md` for the D1–D6 decisions this implements.

## 1. Light sampler (D1)

- [ ] 1.1 Add a `src/Mod/` helper (e.g. `ScribeAmbientLightSampler`) that, given the client
      API + player position, reads `IBlockAccessor.GetLightRGBs(playerPos)` (XYZ = block-light
      RGB, W = sun brightness) and `IAmbientManager.BlendedAmbientColor` /
      `BlendedSceneBrightness`, and folds them into one `(brightness, RGB tint)` value using
      the two-input recipe the engine's `PreparedStandardShader` uses (block light + sun
      brightness, tinted toward sky color). Read on the render/main thread only.
- [ ] 1.2 Resolve the player position source (eye vs. block position) and confirm the read is
      safe/cheap on the render thread; document the choice inline.
- [ ] 1.3 Quantize the output to coarse brightness buckets + hue buckets (D3) and expose a
      "changed since last frame?" signal so downstream only marks needs-paint on a meaningful
      change. Optionally lerp/smooth the quantized value to avoid visible stepping.

## 2. Tint widget (D2, D6)

- [ ] 2.1 Add `src/Mod/ScribeGlobalTint` as a `RenderProxyBox` subclass modeled on
      `ScribeGearEffect`: `Canvas.SaveLayer(paint)` around `base.Paint(context)` with
      `paint.ColorFilter = SKColorFilter.CreateColorMatrix(m)`, where the 4×5 matrix encodes
      brightness scale + RGB temperature from the sampled value.
- [ ] 2.2 Cache color filters (and the `SKPaint`) by quantized `(brightness, r, g, b)` key;
      NEVER dispose per-frame (D6). Use a dedicated cached paint for the `SaveLayer` (don't
      mutate `SharedPaint`); if `SharedPaint` is touched, save/restore
      `Color`/`ColorFilter`/`ImageFilter`/`BlendMode` around `base.Paint`.
- [ ] 2.3 Wire `MarkNeedsPaint()` to fire only when the quantized value changes (via the §1.3
      signal), so the LibGUI `SKPicture` cache re-records only on meaningful light changes.

## 3. Insertion + surfaces (D4)

- [ ] 3.1 Wrap the composed body once at the shared `ScribeDialogBase` body/backdrop wrap point
      (around `BuildBodyTree`/`WrapBackdrop`) with `ScribeGlobalTint`, so backdrop + chrome +
      text are all inside the tinted layer and every surface inherits it. Drive the widget from
      the §1 sampler each frame.
- [ ] 3.2 Confirm all `ScribeDialogBase` surfaces (lectern, notebook, tablet) receive the shade
      uniformly with no per-dialog wiring.

## 4. Config floor (D5)

- [ ] 4.1 Add a minimum-brightness-floor field to `ScribeClientConfig` (default =
      dim-but-legible; range down to near-black). Read it when the dialog opens; absent key →
      code default.
- [ ] 4.2 Apply the floor as the lower clamp on the sampler's brightness so total darkness
      never drops below the configured minimum. If a ConfigLib settings surface exists, expose
      the knob there too (optional, JSON stays source of truth) — no hard ConfigLib dependency.

## 5. Docs & verification

- [ ] 5.1 Add a `VSAPI-NOTES.md` entry recording the D1 light recipe (`GetLightRGBs` W = sun
      brightness only; sky color + weather live in `AmbientManager.BlendedAmbientColor` /
      `BlendedSceneBrightness`; point lights are shader-only and unreadable) and the D3
      paint-cache re-record gotcha, so neither is re-derived.
- [ ] 5.2 `openspec validate respect-local-illumination --strict` passes.
- [ ] 5.3 Run `build/verify.sh` (Core + Atlas) green and restage. (No Core logic added — the
      effect is view-only against the VS API widget tree — so no new Core.Tests; note this.)
- [ ] 5.4 In-game playtest, record verdicts in `TESTING.md`:
      (a) noon outdoors → GUI at full brightness, visually unchanged from today;
      (b) rain outdoors → GUI dimmer than clear noon;
      (c) dark cave + placed torch → GUI warm/orange and dimmed to the torch level;
      (d) total darkness, default config → GUI heavily dimmed but text faintly legible;
      (e) total darkness, floor lowered to minimum → GUI near-black / unreadable;
      (f) all three surfaces (lectern/notebook/tablet) shade identically under the same light;
      (g) typing/clicking/scrolling/focus unaffected by the shade; no tint bleed into other
          GUIs in the same frame;
      (h) MEASURE the paint-cache re-record cost on the parchment backdrops while light changes
          (e.g. day/night transition, walking past torches) — confirm no unacceptable hitching;
          if it hitches, coarsen quantization / throttle cadence (NOT a LibGUI fork).
