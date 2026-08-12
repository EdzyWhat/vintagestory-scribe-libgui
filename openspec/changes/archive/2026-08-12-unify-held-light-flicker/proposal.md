## Why

`respect-local-illumination` shades the Scribe GUI by the player's held light and eases every
transition through a ~400ms exponential low-pass filter so brightness glides instead of jumping.
But if a player runs **Immersive Lanterns** (IL), that same low-pass filter flattens the exact
signal IL exists to produce: IL Harmony-patches `CollectibleObject.GetLightHsv` to make a held
torch/lantern/lamp's brightness *flicker* (a 100–300ms sine for torches, 500–1000ms for lanterns,
amplitude/cadence set by the player in IL's own config). Our sampler already calls that patched
method, so we already **receive** the flickered value every frame — we just smooth it away. The
result: an IL player sees their held light flicker in the world but a dead-steady Scribe page.
Unifying the two keeps immersion for players who chose IL, at effectively zero cost to everyone
else.

## What Changes

- When IL is installed, let a **held** light source's per-frame flicker pass through to the Scribe
  GUI shade instead of being flattened by the 400ms smoothing. The GUI page then flickers in sync
  with the held light, matching the player's IL settings.
- Split the **held-light brightness contribution** out of the combined shade so it bypasses (or
  near-zero-smooths) the exponential ease, while **environmental** transitions (walking sun↔shade,
  day/night, weather) keep the full 400ms glide. Only the held-flicker component is unsmoothed.
- **Gate the whole behavior on `capi.ModLoader.IsModEnabled("immersivelanterns")`.** With IL
  absent, nothing changes: held light stays smoothed, a static scene keeps LibGUI's paint cache
  fully valid, and there is zero added per-frame cost. This is a **soft, optional** integration —
  **no hard dependency, no new NuGet/mod reference, graceful no-op when IL is not present.**
- No attempt to read IL's internals, settings, or flicker curve directly. We reproduce IL's
  flicker for free by simply *not filtering* the already-flickered `GetLightHsv` value we sample —
  same amplitude, cadence, and player settings, no matching code, no reflection.
- Hue is unaffected: IL flickers V (brightness index) only, and this change likewise only relaxes
  smoothing on the held **brightness** term; the color-temperature tint keeps its existing
  treatment.

Non-goals (out of scope; noted for possible later work):

- **Placed-block lantern flicker.** IL's patch deliberately flickers only held/inventory items
  (`pos == null`); placed blocks light the grid steadily. We mirror that scope — placed-lantern
  flicker is not synthesized here.
- **Synthesizing flicker without IL.** With IL absent we do not invent a flicker of our own; the
  GUI simply stays smooth as it does today.
- No change to the brightness curve, the color-temperature tint math, the config floor, tint
  mechanism, persistence, sync, or the document model.

## Capabilities

### New Capabilities
- `gui-held-light-flicker`: when a flickering held light source is present (via an installed
  light-flicker mod such as Immersive Lanterns), the Scribe GUI's rendered brightness follows that
  flicker instead of being smoothed flat; with no such mod, the GUI shading is unchanged and fully
  smoothed.

### Modified Capabilities
<!-- None as delta files. This adds a new, self-contained capability that carves out a held-flicker
     exception to the smoothing behavior introduced by respect-local-illumination. That change's
     gui-ambient-illumination spec is not yet archived, so its smoothing requirement is amended in
     place there (this proposal does not restate it as a delta) and the new pass-through behavior
     lives in its own capability to avoid archive-order header drift between two unarchived changes. -->

## Impact

- **Modified code (`src/Mod/ScribeAmbientLightSampler.cs`):** separate the held-light brightness
  contribution from the environment-derived brightness before `Smooth(...)`, so the held term can
  bypass (or near-zero-τ) the ease when IL is active and a held light dominates. Detect IL once via
  `ICoreClientAPI.ModLoader.IsModEnabled("immersivelanterns")` (cached, not per-frame).
- **Paint cache (D3 of respect-local-illumination):** letting flicker through means the tint
  widget re-records LibGUI's `SKPicture` roughly every frame *while a flickering held light drives
  an open Scribe GUI*. This is bounded to that specific situation and only when IL is enabled;
  vanilla players keep the fully-cached static scene. The re-record cost in that situation MUST be
  measured in-game; if unacceptable, the fallback is a modest flicker quantization / update cadence
  cap (still passing the flicker through visibly), never a wider change.
- **Dependencies:** none added. `IsModEnabled` is vanilla `VintagestoryAPI`; IL remains entirely
  optional and is never referenced at compile time.
- **Risk / prior art:** builds directly on the shipped `respect-local-illumination` sampler and its
  quantization discipline. The only new class of cost is the D3 re-record cadence, already
  identified there as the thing to watch.
