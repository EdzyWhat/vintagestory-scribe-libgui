## Context

Scribe's GUIs render at a fixed, uniform brightness. In total darkness they are painfully
bright, and they never take on the color of the light around the player (torch warmth,
overcast dimness). The Vintage Story engine already computes, per frame, the light reaching
any world position — and it does so with a specific two-input recipe that a mod can reproduce.
This change reads that light client-side and shades the composed Scribe GUI to match, so the
book looks lit by its actual surroundings.

Two research passes (decompiled `VintagestoryLib`/`VintagestoryAPI`, and the LibGUI source at
`reference/vslibgui/`) established the load-bearing facts this design rests on. They are
recorded in Decisions below and should be mirrored into `VSAPI-NOTES.md` when implemented.

Constraints: `src/Core/` must never reference the VS API (this is client render code, so it
lives entirely in `src/Mod/`); no new mod dependencies; the `gui` (LibGUI) mod is a hard
dependency consumed as a pre-built binary (`lib/Gui.dll`, `Private=false`) and MUST NOT be
forked; ConfigLib is at most an optional soft dependency, so the config knob uses the existing
`ScribeClientConfig` JSON, not a hard ConfigLib requirement.

## Goals / Non-Goals

**Goals:**

- Shade the whole composed Scribe GUI by the real light reaching the player — brightness AND
  color temperature — matching how the surrounding world is lit (daylight neutral, torch warm,
  rain dim, darkness near-unreadable).
- One injection point covering all `ScribeDialogBase` surfaces (lectern/notebook/tablet/future).
- A configurable minimum-brightness floor (default dim-but-legible).
- Zero `gui` fork; zero new dependencies; zero `src/Core/` change; render-only (no interaction
  regression).

**Non-Goals:**

- Reading flickering / dynamic point lights (Immersive Lanterns): VS point lights are
  shader-only and unreadable via public API — deferred as a follow-up.
- Tinting non-Scribe / vanilla GUIs.
- Any layout, persistence, sync, or document-model change.
- Exact physically-correct reproduction of the engine's sunset-tint math — a faithful
  approximation is the goal, not a pixel match to world rendering.

## Decisions

### D1 — Light sampling: `GetLightRGBs` + `AmbientManager`, combined as the engine does

The engine's canonical "position → light the shader multiplies by" is `IRenderAPI
.PreparedStandardShader`, which uses **two** inputs, not one:

- `RgbaLightIn = IBlockAccessor.GetLightRGBs(pos)` — a `Vec4f` where **XYZ = block-light RGB**
  (a torch's warm hue is baked in via each block's `LightHsv`) and **W = sun-brightness scalar
  (0..1)**.
- `RgbaAmbientIn = IAmbientManager.BlendedAmbientColor` — the sky/daylight **color** (which is
  NOT in the block grid) and, via `BlendedSceneBrightness`, the weather/rain darkening.

So the sampler reads both and folds them into one brightness scalar + one RGB tint:
brightness ≈ `max(blockLightLuma, W * BlendedSceneBrightness)`; hue ≈ block-light RGB blended
toward `BlendedAmbientColor` weighted by sun vs. block contribution. Exact blend math is an
implementation tuning detail (design leaves headroom); the requirement is only that both
inputs contribute so daylight isn't colorless and torches read warm.

**Alternatives rejected:** `GetLightLevel(pos, MaxTimeOfDayLight)` alone — a scalar 0..32 with
no color, so no torch warmth or sky hue. `GetLightRGBs` alone — misses sun *color* and weather
(W is only a brightness scalar), so daylight would look neutral-grey and rain wouldn't dim.

Player position source: the client player's eye/entity position (render thread). Read on the
render/main thread only — block-accessor reads off the relight background thread are unsafe.

### D2 — Tint mechanism: mod-side `SaveLayer` + `SKColorFilter.CreateColorMatrix`, no fork

The shade is applied by a mod-side `RenderProxyBox` subclass (`ScribeGlobalTint`) that does
`Canvas.SaveLayer(paint)` around `base.Paint(context)` with `paint.ColorFilter =
SKColorFilter.CreateColorMatrix(m)`, where the 4×5 matrix encodes brightness scale + RGB
temperature in a single filter. `SaveLayer` renders the whole child subtree into an offscreen
layer and applies the filter once on composite — exactly "one filter over the whole dialog."

This is the **same mechanism already shipping in `src/Mod/ScribeGearEffect.cs`** (which swaps
`paint.ColorFilter = SKColorFilter.CreateBlendMode(...)` on a `RenderProxyBox`), and it
generalizes the shipped `RenderOpacity` (which SaveLayers an alpha-only paint). A color matrix
is chosen over a multiply-overlay rectangle because a matrix can **brighten as well as darken**
and composites the flattened layer predictably under premultiplied alpha, whereas a multiply
rect can only darken and chews the anti-aliased text/backdrop alpha edges.

**Why not fork LibGUI's `GuiBase.DrawPicture` to take a composite paint (the theoretically
cheaper hook):** LibGUI is consumed as a pre-built binary and installed *separately by players*
as the shared `gui` mod. "Modifying" it means forking `Gui.dll`, shipping our fork (colliding
with the real `gui` mod) or forcing players onto our fork instead of the official one, and
re-forking on every upstream update. The per-frame cost saving does not justify breaking the
shared-dependency model. (User decision, 2026-08-12.)

### D3 — Quantize the light value to protect LibGUI's paint cache

`GuiBase.OnRenderGUI` records each dialog's tree into a cached `SKPicture` and only re-records
when `NeedsPaint`/`ChildNeedsPaint` is set. A tint widget must `MarkNeedsPaint()` whenever its
value changes, which forces a re-record — so a *continuously* changing light value would
re-record every frame, defeating the cache on the pixel-art parchment backdrops.

Mitigation: **quantize** the sampled brightness+RGB to coarse steps (e.g. ~16–32 brightness
buckets, coarse hue buckets) before it reaches the widget, and only propagate (and thus mark
needs-paint) when the quantized value actually changes. On a static scene the value is stable
and the cache stays valid; it re-records only on meaningful light changes. The grid light is
itself quantized (sun brightness is a 0..32 lookup), so this loses little fidelity. Ambient
values vary smoothly, so they must be explicitly bucketed. The re-record cost on the parchment
surfaces MUST be measured in-game (a task, not an assumption).

### D4 — Insertion point: wrap the shared body once in `ScribeDialogBase`

`ScribeGlobalTint` wraps the composed body at the shared body/backdrop wrap point in
`ScribeDialogBase` (around `BuildBodyTree`/`WrapBackdrop`), so backdrop + chrome + text are all
inside the tinted layer and every surface inherits it without per-dialog wiring. This is the
same seam `ScribeGearEffect` and the theme system already hook.

### D5 — Config: one `ScribeClientConfig` field, no ConfigLib requirement

A single minimum-brightness-floor field is added to `ScribeClientConfig` (the existing on-disk
`ModConfig/scribe-client-config.json`), read when the dialog opens. Default = dim-but-legible;
range down to near-black. Absent key → code default (existing config semantics). No hard
ConfigLib dependency; if a ConfigLib settings surface exists it can expose the knob, but the
value's source of truth is the JSON. Per-machine, not synced (matches other client theme prefs).

### D6 — Filter lifetime / paint discipline

Color filters are cached by quantized `(brightness, r, g, b)` key and **never disposed
per-frame** — disposing a filter mid-recording corrupts the picture (the pattern
`ScribeGearEffect.TintCache` and `PaintingContext.GetBlurFilter` already follow). The
`SaveLayer` uses its own cached `SKPaint`, sidestepping the `SharedPaint` state-leak class that
`ScribeBackdropPaintReset` exists to guard against; if `SharedPaint` is touched at all, save/
restore `Color`/`ColorFilter`/`ImageFilter`/`BlendMode` around `base.Paint`.

## Risks / Trade-offs

- **[Paint-cache re-record cost on parchment backdrops]** → Quantize the light value (D3) so
  re-records fire only on meaningful changes; measure in-game before considering the widget
  final. If cost is unacceptable even quantized, the fallback is coarser quantization or a
  throttled update cadence, not a LibGUI fork.
- **[Flicker mods unsupported may disappoint]** → Documented as an explicit non-goal; steady
  placed lanterns still register via the grid. Follow-up change can revisit point-light
  reflection if it proves stable.
- **[Sun-color approximation is not physically exact]** → Acceptable; the goal is a convincing
  match, and we reuse the engine's own two-input recipe (D1) rather than inventing one.
- **[Premultiplied-alpha / SharedPaint leak]** → Use `SaveLayer` + a dedicated cached paint and
  the documented save/restore discipline (D6); confirm no bleed into other dialogs in the same
  frame (a spec scenario + a playtest item).
- **[Config drift]** → New key falls back to the code default when absent; the `what-to-test`
  config-drift guard covers stale on-disk values shadowing the new default at playtest time.

## Migration Plan

Additive and client-only. No data migration, no save-compat concern, no server change. Ships
by adding the sampler + `ScribeGlobalTint` wrap + config field; players get the effect on the
next client update with no world migration. Rollback = remove the wrap (the GUI reverts to
full brightness); nothing persisted depends on it.

## Open Questions

- Exact blend weighting between block-light RGB and `BlendedAmbientColor`, and the
  brightness→matrix response curve (linear vs. eased), are tuning values to settle during
  implementation/playtest, not fixed here.
- Whether the effect should read the player's eye position or block position, and whether to
  smooth (lerp) the quantized value across a few frames to avoid visible stepping when light
  changes — decide during implementation after the in-game measure.
