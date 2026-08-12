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

Constraints: `src/Core/` must never reference the VS API. The client render/sampling code
therefore lives entirely in `src/Mod/`, but two pieces are pure math/state and legitimately
belong in `src/Core/` (unit-testable, no VS API): the brightness **response curve**
(`ScribeBrightnessCurve`) and the config **floor** field (on `ScribePlayerSettings`). This is a
deliberate refinement of the original "no Core change" scope, made once the author supplied a
hand-drawn non-linear curve that wants unit coverage (see D1/D5). No new mod dependencies; the
`gui` (LibGUI) mod is a hard dependency consumed as a pre-built binary (`lib/Gui.dll`,
`Private=false`) and MUST NOT be forked; ConfigLib is at most an optional soft dependency, so
the config knob uses the existing `ScribePlayerSettings` JSON, not a hard ConfigLib requirement.

## Goals / Non-Goals

**Goals:**

- Shade the whole composed Scribe GUI by the real light reaching the player — brightness AND
  color temperature — matching how the surrounding world is lit (daylight neutral, torch warm,
  rain dim, darkness near-unreadable).
- One injection point covering all `ScribeDialogBase` surfaces (lectern/notebook/tablet/future).
- Follow a hand-drawn NON-LINEAR brightness response curve, not a linear trace of local light,
  so darkness is punishing but a little light already reads comfortably.
- A configurable minimum-brightness floor (default dim-but-legible), which is the curve's
  leftmost (x=0) anchor point.
- Zero `gui` fork; zero new dependencies; render-only (no interaction regression). The only
  `src/Core/` additions are the pure response curve + the floor config field (both unit-tested).

**Non-Goals:**

- Reading flickering / dynamic point lights (Immersive Lanterns): VS point lights are
  shader-only and unreadable via public API — deferred as a follow-up.
- Tinting non-Scribe / vanilla GUIs.
- Any layout, persistence, sync, or document-model change.
- Exact physically-correct reproduction of the engine's sunset-tint math — a faithful
  approximation is the goal, not a pixel match to world rendering.

## Decisions

### D1 — Light sampling: `GetLightRGBs` + `AmbientManager` + held light, combined as the engine does

The engine's canonical "position → light the shader multiplies by" is `IRenderAPI
.PreparedStandardShader`, which uses **two** grid/ambient inputs — and the player's own held
light is emitted separately (`EntityPlayer.LightHsv`), so the sampler mirrors **three** inputs:

- `RgbaLightIn = IBlockAccessor.GetLightRGBs(pos)` — a `Vec4f` where **XYZ = block-light RGB**
  (a torch's warm hue is baked in via each block's `LightHsv`) and **W = sun-brightness scalar
  (0..1)**.
- `RgbaAmbientIn = IAmbientManager.BlendedAmbientColor` — the sky/daylight **color** (which is
  NOT in the block grid) and, via `BlendedSceneBrightness`, the weather/rain darkening.
- **Held light** — the item in either hand (`RightHandItemSlot`/`LeftHandItemSlot` →
  `Collectible.GetLightHsv`, merged with `ColorUtil.MergeLightHSV` exactly as
  `EntityPlayer.LightHsv` does). The block grid at the player's *own* block does **not** contain
  the player's held light (it is added dynamically as an entity light), so `GetLightRGBs` misses
  it — a held offhand torch/lantern would otherwise leave the GUI dark. Its level is mapped
  through the live `BlockLightLevels` table (so a held lantern lands on the *same* curve point as
  a placed one) and its hue is taken straight from the item's own `lightHsv`, so a held torch,
  lantern, and oil lamp each read their own color temperature for free from game data — no
  per-item hardcoding.

So the sampler reads all three and folds them into one **raw** brightness scalar + one RGB tint:
rawBrightness ≈ `max(blockLightLuma, heldLuma, W * BlendedSceneBrightness)`; hue ≈ block/held
light RGB blended toward `BlendedAmbientColor` weighted by which is actually lighting the player.
Held light is folded in by **MAX, not sum** — VS light is max-based (flood-fill
`Math.Max(...)`, not additive), confirmed in `ChunkIlluminator`, so a held lantern in an
already-lit room doesn't over-brighten. Exact blend math is an implementation tuning detail
(design leaves headroom); the requirement is only that all three inputs contribute so daylight
isn't colorless, torches read warm, and a held light lifts the GUI even in a dark cave.

**Tint strength (author tuning).** The raw hue skew is pulled back toward neutral by one third
(`TintStrength = 2/3`: `channel = 1 + (channel − 1)·2/3`) so the color/temperature effect reads
present-but-gentle rather than as an aggressive wash. Level is unaffected — only the hue is
softened.

**Non-linear response (author-drawn).** The raw brightness is NOT used linearly. It is passed
through `ScribeBrightnessCurve` (new, in `src/Core/` — pure math, unit-tested), a
piecewise-linear evaluator encoding a hand-drawn curve: the author plotted the desired GUI
brightness (output) against local light (input) on paper, and the curve digitizes that shape.
Control points (input→output): the `x=0` anchor is the config floor (D5), then `0.45→0.50`,
`0.814→0.85`, and `1.00→1.00`. The two high anchors are pinned to VS's own `blockLightLevels`
table so the curve is calibrated in real light-index terms, not guessed: `x=0.814` is
`blockLightLevels[20]` (a large lantern, V=20) and the author set it to read 85%; `x=1.0` is
where the table saturates (V≥26) and reads full. The result sits just below the linear identity
for most of its range (a little light reads brighter than a strict trace would give), rising
through a gentle high shoulder to full only at the very top, never crossing above identity. Each
control point is lifted to `max(y, floor)` so raising the floor lifts the dark end without
bending the curve non-monotonic. Living in Core keeps the shape unit-testable and tunable
without a game launch; only the *sampling* of live light needs the VS API. Only the tint hue
tracks light directly — the curve governs brightness alone.

**Alternatives rejected:** `GetLightLevel(pos, MaxTimeOfDayLight)` alone — a scalar 0..32 with
no color, so no torch warmth or sky hue. `GetLightRGBs` alone — misses sun *color* and weather
(W is only a brightness scalar), so daylight would look neutral-grey and rain wouldn't dim, and
it misses the player's own held light entirely. `EntityPlayer.LightHsv` alone — only the held
light, no environment.

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
surfaces MUST be measured in-game (a task, not an assumption). D7's smoothing deliberately
relaxes the strict "only on change" guard for the bounded handful of frames a transition takes
to settle — see D7.

### D4 — Insertion point: wrap the shared body once in `ScribeDialogBase`

`ScribeGlobalTint` wraps the composed body at the shared body/backdrop wrap point in
`ScribeDialogBase` (around `BuildBodyTree`/`WrapBackdrop`), so backdrop + chrome + text are all
inside the tinted layer and every surface inherits it without per-dialog wiring. This is the
same seam `ScribeGearEffect` and the theme system already hook.

### D5 — Config: one `ScribePlayerSettings` field = the curve's x=0 anchor, no ConfigLib requirement

A single minimum-brightness-floor field (`IlluminationFloor`) is added to `ScribePlayerSettings`
(Core; the existing on-disk `ModConfig/scribe-client-config.json` client settings — NOT the
retired `ScribeClientConfig`). Default `0.03` = the drawn curve's `x=0` anchor (dim-but-legible);
range `MinIlluminationFloor 0.02` (a hair above black) .. `MaxIlluminationFloor 1.0` (always
full-bright → opt out of the effect). Clamped via `ClampIlluminationFloor` and normalized in
`Normalized()`. Absent key → code default (existing config semantics). Unit-tested.

The floor is **not a post-hoc clamp** on the curve output — it IS the curve's leftmost (x=0)
control point (revised from the original "clamp" framing). Total darkness therefore renders at
exactly the floor, and raising the floor lifts the dark end of the curve while the floor-lift on
every point keeps the shape monotonic. No hard ConfigLib dependency; if a ConfigLib settings
surface exists it can expose the knob, but the value's source of truth is the JSON. Per-machine,
not synced (matches other client theme prefs).

### D6 — Filter lifetime / paint discipline

Color filters are cached by quantized `(brightness, r, g, b)` key and **never disposed
per-frame** — disposing a filter mid-recording corrupts the picture (the pattern
`ScribeGearEffect.TintCache` and `PaintingContext.GetBlurFilter` already follow). The
`SaveLayer` uses its own cached `SKPaint`, sidestepping the `SharedPaint` state-leak class that
`ScribeBackdropPaintReset` exists to guard against; if `SharedPaint` is touched at all, save/
restore `Color`/`ColorFilter`/`ImageFilter`/`BlendMode` around `base.Paint`.

### D7 — Smooth the transition (exponential ease) so brightness glides, not jumps

Without smoothing, walking through changing light steps the GUI between quantization buckets in
visible jumps. The sampler eases the continuous (pre-quantization) shade toward each freshly
sampled target with frame-rate-independent exponential smoothing: `alpha = 1 − e^(−dt/τ)` with
`τ = 0.2s` (~86% of a step in 400ms, ~95% in 600ms — the author's "~400ms glide" request).
Smoothing runs **before** quantization, so the reported bucket steps through the intermediate
values over the transition rather than snapping. The first frame adopts the target directly so
an opening dialog isn't seen fading up from black.

This is a **first-order ease toward the current target each frame, not a fixed-duration tween** —
which is deliberate: because there is no start/end snapshot, it stays continuous even while the
target keeps moving (walking into ever-brighter/darker light just keeps chasing the moving value
with no restart or velocity snap). A fixed-duration A→B lerp would have to restart mid-transition
and kink. The only cost of the longer τ is a longer lag/"tail" behind abrupt changes — measure
the feel in-game.

This is a **deliberate, bounded** relaxation of D3: a light transition re-records the paint
cache for the handful of frames it takes to settle, then a static scene settles and holds so the
cache stays valid at rest. The cost is the same class D3 flags for measurement.

## Risks / Trade-offs

- **[Paint-cache re-record cost on parchment backdrops]** → Quantize the light value (D3) so
  re-records fire only on meaningful changes; measure in-game before considering the widget
  final. If cost is unacceptable even quantized, the fallback is coarser quantization or a
  throttled update cadence, not a LibGUI fork.
- **[Flicker unsupported may disappoint]** → The steady level+hue of a held or placed
  torch/lantern/lamp IS supported; only per-frame flicker is missed. Held-light flicker
  (optionally unified with Immersive Lanterns' modifiable flicker, if installed) is a soft,
  dependency-optional follow-up — never a hard dep. Placed point-light flicker would need
  reflection into a private engine field; deferred.
- **[Lighting mods baked-off may go stale]** → Avoided by reading every light input live from an
  engine source each frame (never a hardcoded table), so WarmerLighting/ImmersiveLight and future
  lighting mods are honored automatically. The only baked value is the author's curve shape (a
  deliberate design choice, not an engine snapshot).
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

- Exact blend weighting between block-light RGB and `BlendedAmbientColor` is a tuning value to
  settle during implementation/playtest, not fixed here. (The brightness→output response curve
  is now RESOLVED — the author-drawn `ScribeBrightnessCurve`, D1 — its control points remain
  tunable in that one Core file if the in-game feel disagrees with the paper sketch.)
- Position source is now block position (`entity.Pos.AsBlockPos`) — chosen for the stable
  grid-aligned cache key; eye-vs-block is immaterial at block granularity.
- Whether to smooth (lerp) the quantized value across a few frames to avoid visible stepping is
  deferred — the bucket cadence is expected imperceptible; revisit only if the playtest shows it.
