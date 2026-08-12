# Tasks — unify-held-light-flicker

> Soft-optional integration with Immersive Lanterns (IL). Builds on `respect-local-illumination`'s
> `ScribeAmbientLightSampler`. NO hard dependency, NO new package, NO IL reference at compile time,
> NO LibGUI fork. All edits in `src/Mod/` (the sampler); no `src/Core/` change. See `design.md`
> D1–D3 and the decompiled-IL findings in the Context section.

## 1. IL detection (D2)

- [x] 1.1 In `ScribeAmbientLightSampler`, add a cached `bool` for
      `capi.ModLoader.IsModEnabled("immersivelanterns")` (the modid from IL's `modinfo.json`),
      evaluated once (ctor or first `Sample`) — mod enablement can't change mid-session. When
      false, the sampler must take exactly today's path (held term smoothed with the environment).
      Done: `FlickerModId` const + lazily-cached `flickerModActive` field via `FlickerModActive` property.

## 2. Split held brightness out of the smoothed shade (D1)

- [x] 2.1 Separate the brightness fold in `Sample(dt)` into two components before `Smooth(...)`:
      an ENVIRONMENT brightness (grid light + sun/sky/weather, curve-mapped, as today) and a HELD
      brightness (the curve-mapped contribution from `TryHeldLight` when it dominates). Keep the
      existing MAX combination semantics (VS light is max-based). Done: `envBrightness` +
      `heldBrightness` (0 when no held light so it can't floor-lift the MAX); combined by MAX in `Smooth`.
- [x] 2.2 When IL is active (§1.1) AND a held light is present/dominant, apply NO smoothing (adopt
      target directly) — or a negligible τ per the §4.1 decision — to the held-brightness term,
      while the environment term keeps the existing `τ = 0.2s` ease. Report
      `brightness = max(smoothedEnvironment, heldFlickerBrightness)`. Done: `passHeldThrough` branch
      in `Smooth` eases only `envBrightness`, then reports `max(smoothedBrightness, heldBrightness)`.
      Shipped with TRUE-ZERO held smoothing (adopt per frame); §4.1 may swap in a small τ from feel.
- [x] 2.3 Confirm the color-temperature TINT path is untouched (IL flickers V only): only the held
      BRIGHTNESS term bypasses smoothing; hue keeps its existing smoothed treatment. Done: `r,g,b`
      still eased in `Smooth` on every path; the split touches brightness only.
- [x] 2.4 When IL is NOT active, the split collapses to today's behavior (held folded into the one
      smoothed value) — verify no code path change for the vanilla case (structural, not just a
      runtime branch that happens to match). Done: with `passHeldThrough` false, `smoothTarget =
      max(env, held)` is smoothed as one value and reported directly — and because the curve is
      monotonic (`max(curve a, curve b) == curve(max a,b)`), this equals the pre-split single-fold value.

## 3. Docs & verification

- [x] 3.1 Update the `ScribeAmbientLightSampler` class doc + `Smooth`/`Sample` comments to describe
      the held-flicker pass-through, the IL gate, and that the env term stays smoothed. Note the
      decompiled-IL facts (held-only `pos==null` Postfix on `GetLightHsv`, V-only flicker) so they
      aren't re-derived. Consider a short `VSAPI-NOTES.md` entry under `## LibGUI` / a new IL note.
      Done: `FlickerModId`/`FlickerModActive`/`Smooth` doc-comments + inline notes; VSAPI-NOTES entry added.
- [x] 3.2 `build/verify.sh Debug` green — 0 warnings/0 errors, Core + Atlas suites pass, mod
      restaged. (Export `VINTAGE_STORY="/Applications/Vintage Story.app"` inline so the Atlas suite
      resolves the API DLL.) Done: Core 339 + Atlas 25 pass, 0/0, restaged (101 files).
- [x] 3.3 `openspec validate unify-held-light-flicker --strict` passes.

## 4. In-game playtest (record verdicts in TESTING.md)

- [x] 4.1 With IL installed: hold a torch in a dark spot with a Scribe GUI open → the page
      brightness flickers in step with the torch (fast, ~100–300ms cadence). Hold a lantern → a
      slower (~500–1000ms) flicker. Confirm the flicker amplitude/cadence tracks IL's own config
      (change an IL setting, see the page follow). DECIDE here between true-zero vs. small-τ
      held smoothing (§2.2) based on feel — jittery/aliased → add a small τ; laggy → keep zero.
      PASS 2026-08-12: it flickers; TRUE-ZERO held smoothing kept (no small τ needed).
- [x] 4.2 With IL installed: while the held flicker is passing through, ALSO walk between lit and
      shadowed areas → the environmental brightness change still eases smoothly (~400ms), i.e. the
      flicker rides on top of a smoothed environment, no global stepping returns.
      PASS 2026-08-12: environment glides as expected under the flicker.
- [x] 4.3 WITHOUT IL: hold a steady torch/lantern with a Scribe GUI open → no flicker, brightness
      smoothed exactly as `respect-local-illumination` behaves today; a static scene settles.
      PASS 2026-08-12: no IL → no flicker.
- [x] 4.4 MEASURE the paint-cache re-record cost (D3) with IL on + a flickering held light driving
      an open Scribe GUI on the parchment backdrops → confirm no unacceptable hitching. If it
      hitches, coarsen the held-brightness quantization or cap the flicker update cadence (still
      visibly flickering) — NOT a LibGUI fork.
      PASS 2026-08-12: no stutter with a flickering held light driving an open parchment GUI; the
      D3 quantization holds the re-record cost negligible. No coarsening/cadence-cap fallback needed.
