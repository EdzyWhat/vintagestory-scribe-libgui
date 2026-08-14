## Context

The one-frame "white flash" on opening a backdropped Scribe surface is a **synchronous GPU texture
upload landing on a live gameplay frame**, which stalls the render thread and drops the opaque
chunk-terrain pass for that frame (sky shows through). §1 confirmed the mechanism by discriminator
(Pixel Art Display OFF → no flash). Two things then established the *why*, from source/DLL reads:

- **Upload is synchronous, main-thread-only, one GL context.** `VintagestoryLib.dll`
  (`ClientPlatformWindows`) guards every upload entrypoint with *"Texture uploads must happen in the
  main thread. We only have one OpenGL context"* and does a bare blocking `GL.TexImage2D`. So any
  cold upload on a live frame stalls it. VS itself never hitches because it uploads **all** its
  block/item/entity atlases during the loading screen (`TextureAtlasManager.ComposeTextureAtlasses_
  StageA/B/C` behind `GuiScreenLoadingGame`) — nothing is uploaded mid-gameplay.

- **LibGUI runs its own Skia GL surface on that same single context.** `SkiaRenderer`
  (`vslibgui …/Rendering/SkiaRenderer.cs`) creates one `GRContext` + `SKSurface` wrapping the live
  framebuffer, reused for the process; `GrContext` is a **public** getter (used by
  `VsIconTextureCache`/`ItemStackRenderer`). The textured-box / `Image` path hands a **raster
  `SKBitmap`** to `canvas.DrawBitmap`/`DrawImage`, and Skia lazily uploads it to a GPU texture **on
  first draw**, keyed by generation id, then reuses it. LibGUI does **not** pre-warm asset bitmaps;
  it avoids a visible hitch only because (a) its GL/Skia pipeline is already warm from earlier
  drawing and (b) it holds stable long-lived bitmaps so the upload happens once. For content that
  genuinely can't upload in-frame (3D item renders, Cairo icons) it uses a different idiom: upload
  once to a GL texture, then wrap it as a **GPU-resident `SKImage` via `SKImage.FromTexture(GrContext,
  …)`** and draw *that* (`VsIconTextureCache.cs:56`, `ItemStackRenderer.cs:924`).

**Where we are now (interim work this session):** `GetBackdropSource`/`BakeTint` mark the bitmap
`SetImmutable()`, `ScribePixelArtBackdrop` draws it via `SKImage.FromBitmap` + `canvas.DrawImage`, and
the backdrop PNGs are being re-exported to their authored 128×145 native size (nearest-neighbour
upscale). This moved the flash from *every open* → *first open only* (measured: WhiteFlash4 shows one
big terrain-dropout spike per surface's first open, repeat opens clean) — but **did not reach zero**,
because `SKImage.FromBitmap` produces a **raster** image whose GPU upload is still lazy on first
draw. The shrink also proved the flash is **size-independent** (72 KB flashes as hard as 4.75 MB).

## Goals / Non-Goals

**Goals:**
- Zero opaque-terrain-pass dropout on **any** open of a backdropped Scribe dialog, including the
  first open of each surface in a session (the case that still flashes today).
- Do it via LibGUI's own sanctioned GPU-resident-image idiom, no `gui` fork, no speculative GL.

**Non-Goals:**
- The backdrop shrink to 128×145 + nearest-neighbour crispness is a **separate** improvement (asset
  size, memory, pixel-art look) and is *not* the flash fix — but it is kept, because it also makes
  warming every backdrop at load cheap (~14 specs × ~72 KB ≈ 1 MB total vs ~66 MB at 1024).
- No `Core` changes (pure client-render). No new mod dependency. No save-format impact.

## Decisions

### D1 — Make the backdrop a GPU-resident `SKImage`, uploaded off the live frame (mirror `VsIconTextureCache`)

Split the cost in two so nothing cold lands on the open frame:

1. **Cold upload at load (behind the loading screen):** at `StartClientSide` (main thread, during
   world load — the same window VS uploads its atlases in), for each backdrop bitmap we will use,
   call `capi.Render.LoadTexture(bitmap, …)` (the `SKBitmap`/`IBitmap` overload) to upload it to a GL
   texture in the shared context, and **hold the resulting `LoadedTexture`** resident for the mod's
   lifetime. This is the "sanctioned mod-side pre-warm": `GL.TexImage2D` is paid where a one-frame
   hitch is invisible.
2. **Cheap wrap at first paint:** in `ScribePixelArtBackdrop`'s render object, build the drawable
   image once via `SKImage.FromTexture(GuiModSystem.Instance.SkiaRenderer.GrContext, backendTexture,
   …)` where `backendTexture` references the already-resident GL texture id
   (`GRBackendTexture(w, h, new GRGlTextureInfo(target, texId, format))`). `FromTexture` **references**
   an existing texture — it does **not** re-upload — so this is cheap and does not stall. Cache the
   `SKImage`; on subsequent paints just `canvas.DrawImage(image, src, dst, Nearest, paint)`.

**Why over the alternative (Route 2, "pre-warm the lazy upload"):** we could keep `FromBitmap` and
just force the first `DrawBitmap` early by drawing each backdrop once during a covered frame. Rejected
as timing-fragile — it still relies on a live-frame draw, and if the warm draw lands on a visible
terrain frame it *is* the flash, moved. D1 removes the in-frame upload entirely and is exactly the
pattern LibGUI ships for this problem. **Why not accept a one-time first-open flash:** rejected by the
author; the flash is fixable to zero with an established idiom.

### D2 — Upload eagerly for all backdrop specs at load, not lazily per first use

A lazy "upload the first time this surface opens" would move the cold upload back onto a live frame —
the exact thing we're removing. So warm **all** specs that can be opened (Lectern/Notebook page,
Clockmaker page, the clay-tablet variants, wax) at load. The shrink makes this cheap (~1 MB total).

### D3 — Keep `SetImmutable` + the 128×145 shrink + nearest-neighbour

They are complementary and independently correct: immutability lets Skia cache; the shrink cuts
memory and gives the crisp pixel-art look the author wants and makes D2's eager warm cheap; nearest
sampling stays (an `SKSamplingOptions(Nearest)` on `DrawImage` works identically for a `FromTexture`
image). None of them is the flash fix, but none is removed.

### D4 — Lifetime & disposal

`ScribeModSystem` owns the `LoadedTexture`s (uploaded at load) and the cached `SKImage`s (wrapped at
first paint), and disposes both in `Dispose` alongside the existing `backdropCache` bitmaps. The GL
texture lives in the shared context; free it via the `LoadedTexture` dispose path.

## Risks / Trade-offs

- **[Texture origin / colour-format mismatch]** a `FromTexture`-wrapped VS texture can render
  flipped or channel-swapped if `GRSurfaceOrigin` / `SKColorType` / `GRGlTextureInfo` format don't
  match how `capi.Render.LoadTexture` laid it out. → Mirror `VsIconTextureCache.cs:46-62` exactly, and
  verify visually (backdrop looks correct, not upside-down/blue) *in addition to* the frame-trace.
- **[GrContext null at wrap time]** `GrContext` is created lazily on `SkiaRenderer`'s first `Begin()`.
  → We wrap at **first paint**, not at load; by the time a dialog paints, LibGUI's pipeline has run,
  so `GrContext` is non-null. Guard for null and fall back to the current `FromBitmap` path if ever
  absent (degrades to today's first-open-only behaviour, never crashes).
- **[Warm draw itself flashing]** avoided by construction — the cold cost is `LoadTexture` at load
  (no draw, behind loading screen); `FromTexture` + `DrawImage` on the live frame do no upload.
- **[GL texture leak]** holding ~14 textures for the process is intentional and tiny; ensure
  `Dispose` frees them so a mod reload doesn't leak.

## Migration Plan

Pure client-render, no persistence. Deploy = build + restage. Rollback = revert
`ScribePixelArtBackdrop` to the `FromBitmap` path and drop the load-time warm (returns to the
current first-open-only flash, no data risk).

## Open Questions

- Exact `capi.Render.LoadTexture` overload + the matching `GRGlTextureInfo`/`SKColorType`/origin
  triple — resolve during §2.1 by reading `VsIconTextureCache` in full and the `LoadTexture(SKBitmap)`
  path in `VintagestoryLib.dll`; settle by visual + frame-trace verification, not by guessing.
