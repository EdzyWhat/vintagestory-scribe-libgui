## 1. Confirm the mechanism (decisive discriminator)

- [x] 1.1 Open a flashing surface (Lectern/Notebook/Tablet) with **Pixel Art Display OFF** in Scribe
  Settings — the backdrop becomes a plain `SizedBox` with no texture (`WrapBackdrop`,
  Layout.cs:~88). Capture with the DEBUG frame-trace / OpenCV frame-extract method. **Flash gone →**
  backdrop bitmap paint confirmed as the mechanism, go to §2. **Flash survives →** not the backdrop;
  skip to §3. *(Playtest 2026-08-11, `f79c21bf`: "The flash is gone!" with Pixel Art OFF → backdrop-bitmap paint CONFIRMED as the mechanism. Proceed to §2.)*
- [x] 1.2 Record the discriminator result in `VSAPI-NOTES.md` (`## "White flash"…`) so the next
  reader doesn't re-run it. *(2026-08-11: recorded the "DISCRIMINATOR RESOLVED" block + updated the heading — backdrop-bitmap paint is the confirmed mechanism; fix work moves to §2.)*

> **ROUTE 1 REVERTED 2026-08-13 — it was a net regression.** Decisive in-game discriminator
> (three fresh-session scenarios, see §4.1) proved the flash is bound to **dialog OPEN**, not to
> drawing the backdrop: turning Pixel Art ON *while a dialog is already open* (the first backdrop
> image draw of the session) does **not** flash, so "first backdrop draw cost" is dead. Worse, the
> Route-1 warm (`BlockTexturesLoaded += WarmBackdropTextures`, 13 resident GL textures) made the
> **Pixel-Art-OFF** path flash too — a path that §1.1 proved was flash-free — almost certainly by
> perturbing the shared GL/GrContext state so the first Scribe Skia frame after each open churns VS's
> own terrain textures. Backed out §2.3–2.6 (warm-at-load + `FromTexture`); **kept §2.2** (crisp
> nearest-sampling `ScribePixelArtBackdrop` via `FromBitmap` + `SetImmutable` + native 128×145
> re-export — harmless, not implicated). The channel-swap (blue) fix went out with the reverted warm;
> re-derive it only if a future fix re-uploads. **Next: frame-trace the OPEN transition (§4.1),
> do NOT write another speculative fix first.**

## 2. Fix (backdrop paint confirmed — Route 1: GPU-resident SKImage, see design.md) — REVERTED, see banner above

- [x] 2.1 Read the upload path in the DLLs + LibGUI source. **Done 2026-08-13** (two research
  agents): upload is synchronous/main-thread/one-GL-context (`ClientPlatformWindows`, throwing guard +
  `GL.TexImage2D`); VS warms its own atlases behind the loading screen (`TextureAtlasManager`); LibGUI
  runs its own Skia surface on that same context (`SkiaRenderer`, public `GrContext`) and uploads
  asset bitmaps **lazily on first `DrawBitmap`/`DrawImage`**; the ship-shape idiom for
  no-in-frame-upload is `SKImage.FromTexture(GrContext,…)` over a pre-uploaded GL texture
  (`VsIconTextureCache.cs:56`). Root cause = the one cold first-open upload landing on a live frame;
  it is size-independent (measured).
- [~] 2.2 **Interim partial (applied, insufficient):** `SetImmutable()` on decoded/tinted/procedural
  bitmaps + `ScribePixelArtBackdrop` (`SKImage.FromBitmap` + `DrawImage`, nearest) + re-export
  backdrops to native 128×145. Result: flash reduced from *every open* → *first open only* (repeat
  opens cached). NOT zero — `FromBitmap` is a raster image, still lazy-uploads on first draw. **Keep
  all three** (design D3) but they are not the fix on their own.
- [~] 2.3 **REVERTED 2026-08-13** (see banner) — was: Warm at load (design D1 step 1 / D2). **Done 2026-08-13.** Added `ScribeBackdrops.All` (the
  full 13-spec roster) + `ScribeModSystem.WarmBackdropTextures`, uploading each spec's (possibly tinted)
  bitmap to a `LoadedTexture` via `capi.Render.LoadTexture(new BitmapExternal(bmp), ref tex, false, 1, false)`
  and holding them resident in a `backdropTextures` dict for the mod lifetime. **Timing refinement within
  Route 1:** hooked `Event.BlockTexturesLoaded` (registered in `StartClientSide`) rather than `StartClientSide`
  itself — same "behind the loading screen, main thread" window, but with the GL context *guaranteed* current
  (VS has just uploaded its own atlases), removing the only real risk that a raw-`StartClientSide` upload could
  fail if the context weren't ready that early. Idempotent (skips already-resident + missing-asset specs).
- [~] 2.4 **REVERTED 2026-08-13** (see banner) — was: Draw GPU-resident (design D1 step 2). **Done 2026-08-13.** `ScribeModSystem.GetBackdropImage(spec)`
  wraps the resident texture once via `SKImage.FromTexture(GuiModSystem.Instance.SkiaRenderer.GrContext,
  new GRBackendTexture(w, h, false, new GRGlTextureInfo(3553, texId, 32856)), TopLeft, Rgba8888, Premul)`
  (mirrors `VsIconTextureCache`), caches in `backdropImages`. `ScribePixelArtBackdrop` now takes
  `(modSystem, spec, bitmap)` and at paint prefers `GetBackdropImage(spec)`, falling back to
  `SKImage.FromBitmap(bitmap)` when it returns null (missing asset or `GrContext` not yet ready) — never
  crashes. `WrapBackdrop` passes `modSystem, host.BackdropSpec` through.
- [~] 2.5 **REVERTED 2026-08-13** (see banner) — was: Dispose (design D4). **Done 2026-08-13.** `Dispose` frees `backdropImages` (the wrappers) FIRST,
  then `backdropTextures` (frees the GL ids via `LoadedTexture.Dispose`), then the `backdropCache` bitmaps —
  order matters so no `SKImage` dangles over a freed texture id.
- [x] 2.6 **Done 2026-08-13.** `dotnet build src/Mod/Mod.csproj` clean (4 pre-existing warnings, 0 new,
  0 errors); `dotnet test tests/Core.Tests` green (339/339); `bash build/restage.sh Debug` (103 files).

## 3. Fix (backdrop refuted) — N/A

- [x] 3.1 Not applicable: §1.1 confirmed the backdrop-bitmap paint is the mechanism, so the refuted
  branch does not apply. (Retained for record.)

## 4. Verify

- [ ] 4.1 Re-run the DEBUG frame-trace on every backdropped surface (Lectern, Notebook, Clockmaker,
  Tablet variants, wax), **including the first open of each in a fresh session** (the case that still
  flashes today): no one-frame opaque-terrain dropout on any open. Confirm the Settings window and
  `.ui` showcase are unregressed.
  - **Playtest 2026-08-13 (FAILED — root cause reframed):** with Route 1 fully implemented + textures
    confirmed GPU-resident (a channel-swap bug rendered the backdrops blue, which *proved* the
    `FromTexture` path was live, not the `FromBitmap` fallback), **the first-open flash still occurred on
    every surface.** Pre-warming the upload behind the loading screen did NOT remove it. Combined with the
    already-measured **size-independence** (72 KB flashed as hard as 4.75 MB — far too small an upload to
    stall a frame), this **falsifies the "cold texture upload" root cause.** The flash is a cost of the
    first *draw* of the backdrop in a session, not of its upload. Leading new hypothesis: first-time Skia
    GL program/shader compilation for the image-draw pipeline (size-independent, once per program config,
    separate from upload). **Next: a quick per-image-vs-per-session discriminator in-game** (open surface A
    → flash; then open a *different* backdrop B — if B also flashes on its first open it's per-image, if B
    is clean the program was shared/warmed by A) to decide the fix shape, THEN a frame-trace to confirm the
    stall is a compile. Do NOT write another fix before this measurement. The blue channel-swap fix (upload
    as BGRA via `LoadOrUpdateTextureFromBgra` over `SKColor`-normalized pixels) is kept regardless.
  - **DISCRIMINATOR RAN 2026-08-13 (Route 1 still in) — flash is bound to dialog OPEN, per visual-config,
    once per session. Route 1 reverted as a net regression.** Three fresh-session scenarios:
    - *Load 1:* open Notebook (art ON) → **flash**; close; disable art in Settings; reopen (art OFF) →
      **flash**; any further open (either state) → no flash.
    - *Load 2:* open Notebook (art ON) → **flash**; keep it open, disable art in Settings → **no flash**.
    - *Load 3:* open Notebook (art OFF) → **flash**; keep it open, enable art in Settings → **no flash**.
    Two conclusions, both decisive:
    1. **The flash is the OPEN transition, not the backdrop draw.** Load 3 does the session's *first
       backdrop image draw* in-place (art toggled ON in an already-open dialog) and does **not** flash.
       So "cost of first backdrop draw/upload" is falsified alongside "cost of first backdrop upload."
       The flash fires once per *(surface × art-state)* on the first *open* into that config; in-place
       toggling never flashes because the LibGUI surface + `GrContext` already exist.
    2. **Route 1 regressed the art-OFF path.** §1.1 proved art-OFF was flash-free; with the Route-1 warm
       resident it flashed on first open. The only art-OFF-path change was `BlockTexturesLoaded +=
       WarmBackdropTextures` (13 resident GL textures) → reverted. Expected post-revert baseline (to
       confirm on next relaunch): **art OFF = no flash; art ON = first-open-only flash** (the pre-Route-1
       state). **Next: frame-trace the art-ON OPEN transition specifically** — capture the frame the
       dialog appears and identify what work on the main render thread drops the opaque-terrain pass
       (LibGUI surface (re)alloc + `GrContext.ResetContext`, first-raster of the new SKPicture, or a
       Skia GL program compile keyed on the backdrop-image draw). Measure before touching render code.
  - **POST-REVERT RETEST 2026-08-13 — INTERMITTENT, NO RELIABLE REPRO, ROOT CAUSE STILL UNKNOWN. Change
    PARKED here for a clean resume.** Three fresh relaunches of the reverted baseline: relaunch 1 flashed
    (both art states); relaunches 2 & 3 had zero flashes (any surface, either art state). I hypothesized a
    cold-GPU-shader-cache warmup (launch 1 compiles GL programs, later launches serve from the driver's
    on-disk cache) — **but the tester rejected it**: they have repeatedly seen the flash reproduce across a
    full open→see-flash→**quit**→relaunch→see-flash-again cycle for most of this playtest, which a
    persisted driver cache would not do. So warmup is NOT the answer; the true trigger is unidentified and
    currently not reproducible on demand. What we DO still hold as measured fact: (1) it's a one-frame
    opaque-terrain-pass dropout, GUI pixel-identical (OpenCV); (2) it's bisect-confirmed pre-existing
    (`5f6022a`), orthogonal to any Scribe render code; (3) it's bound to the dialog OPEN transition, NOT to
    drawing the backdrop — toggling Pixel Art ON in an *already-open* dialog (the session's first backdrop
    draw) does not flash; (4) §1.1's "art OFF removes the flash" is now DOUBTED as ordering luck rather than
    a real causal discriminator, given (3) and the intermittency. Route 1 (resident-texture warm +
    `FromTexture` draw) was tried and made it WORSE (added art-OFF flashes + repeatable per-config flashes)
    — reverted; do NOT re-add resident-texture warming. Full resume record: memory
    `[[white-flash-is-world-render-stall]]` + VSAPI-NOTES "White flash". **RESUME PLAN when it recurs: FIRST
    pin down a reliable repro (exact sequence — cold boot? which surface first? art on/off? single vs MP?
    does quit→relaunch reflash?), THEN frame-trace the offending open frame to see what stalls the render
    thread — the measurement we have never captured. Do NOT write another fix before that trace; we have
    already falsified 3+ theories by guessing.**
- [ ] 4.2 Visually confirm each `FromTexture` backdrop renders **correct** (not flipped/upside-down,
  not channel-swapped/blue-tinted) and the nearest-neighbour crispness + baked tints are intact —
  the origin/format risk in design.md is a visual bug the frame-trace won't catch.
- [ ] 4.3 `openspec validate fix-dialog-open-white-flash` passes; record the playtest verdict in
  `TESTING.md`; update `VSAPI-NOTES.md` (supersede the old "cold upload is NOT the cause" conclusion —
  it held only for the mutable-bitmap case) and memory `[[white-flash-is-world-render-stall]]` with
  the confirmed root cause + fix.
