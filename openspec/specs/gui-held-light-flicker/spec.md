# gui-held-light-flicker Specification

## Purpose
TBD - created by archiving change unify-held-light-flicker. Update Purpose after archive.
## Requirements
### Requirement: Held-light flicker passes through to the GUI shade when a flicker mod is present

When a light-flicker mod (e.g. Immersive Lanterns) is installed and active, and the light shading
the Scribe GUI is dominated by the player's **held** light source, the GUI's rendered brightness
SHALL follow that held light's per-frame flicker rather than being flattened by the illumination
smoothing. The mod SHALL detect the flicker mod's presence via the engine's mod-enabled check
(`ICoreClientAPI.ModLoader.IsModEnabled`), evaluated once and cached (not per frame). The
pass-through SHALL be achieved by not applying (or applying only a negligible amount of) the
brightness smoothing to the held-light contribution — the mod SHALL NOT read the flicker mod's
internal state, settings, or curve, and SHALL NOT add a compile-time or runtime dependency on it.

Only the held-light **brightness** term SHALL bypass smoothing. The color-temperature tint and the
brightness derived from the **environment** (block-grid light, sun, sky, weather) SHALL retain
their existing smoothed treatment.

#### Scenario: Held flicker reaches the page when the flicker mod is active

- **WHEN** the flicker mod is installed and the player holds a flickering light source that is the
  dominant light on an open Scribe GUI
- **THEN** the GUI's brightness visibly flickers in step with the held light's flicker (its
  amplitude and cadence matching the flicker mod's own settings), rather than holding a steady
  smoothed level

#### Scenario: Environmental transitions stay smoothed even with the flicker mod active

- **WHEN** the flicker mod is active and the player — while the held-light flicker is passing
  through — also walks between a lit area and shadow (an environmental light change)
- **THEN** the environmental portion of the brightness change still eases smoothly over the normal
  transition interval, while only the held-flicker component is unsmoothed

### Requirement: With no flicker mod installed, shading behavior is unchanged

When no supported light-flicker mod is active, the Scribe GUI illumination SHALL behave exactly as
`respect-local-illumination` defines it: the held-light contribution is smoothed together with the
environment, a static scene reaches a steady value, and no additional per-frame paint-cache
re-recording is introduced by this change.

#### Scenario: Vanilla player sees no change

- **WHEN** the player does not have a supported flicker mod installed and views a Scribe GUI under
  any lighting, including while holding a steady torch or lantern
- **THEN** the GUI brightness is smoothed as before and a static scene settles to a steady value,
  with no flicker and no extra paint-cache churn

#### Scenario: No hard dependency on the flicker mod

- **WHEN** the flicker mod is not present
- **THEN** the Scribe mod loads and runs normally with the illumination effect intact, never
  failing or degrading due to the absent optional mod

