# gui-ambient-illumination Specification

## Purpose
TBD - created by archiving change respect-local-illumination. Update Purpose after archive.
## Requirements
### Requirement: GUI brightness tracks light reaching the player

The Scribe GUI SHALL render at a brightness derived from the light reaching the player's
current position, so that the GUI is fully bright in bright surroundings and progressively
dimmer as the surroundings darken. The brightness input SHALL combine the sun-brightness
component of `IBlockAccessor.GetLightRGBs(playerPos)` (the W component) with the ambient
scene brightness from `IAmbientManager` (`BlendedSceneBrightness`) AND the player's own held
light (the light emitted by the item in either hand, read as `EntityPlayer.LightHsv` does), so
that both time-of-day/weather darkening AND a held light source are reflected — matching how
the surrounding world is lit. Held light SHALL be combined by MAX (not summed), matching the
engine's max-based light model, and its level SHALL be mapped through the engine's live block-
light-level table so a held light source shades the GUI equivalently to a placed one of the same
level. Every light input SHALL be read live from an engine source each frame (never a baked
snapshot), so a mod that changes the light reaching the player is honored automatically.

The mapping from that light input to the rendered brightness SHALL follow a fixed non-linear
response curve rather than a linear (identity) trace of the light: a small amount of light
SHALL render brighter than a strict linear mapping would give, and the GUI SHALL reach full
brightness slightly before the light input reaches its maximum. The curve SHALL be
monotonic-non-decreasing (more light never renders darker) and SHALL anchor its darkest (zero-
light) output at the configurable minimum-brightness floor.

#### Scenario: Bright neutral daylight

- **WHEN** the player views a Scribe GUI outdoors at noon under clear sky
- **THEN** the GUI renders at full brightness, visually unchanged from the pre-illumination
  behavior

#### Scenario: Reduced light under rain

- **WHEN** the player views a Scribe GUI outdoors while it is raining (the surrounding scene
  is darker than clear noon)
- **THEN** the GUI renders dimmer than it does under clear noon, tracking the reduced ambient
  scene brightness

#### Scenario: Response is non-linear, not a linear trace

- **WHEN** the light reaching the player is at a mid-range level (neither near-dark nor near-full)
- **THEN** the GUI's rendered brightness is higher than a straight linear mapping of that light
  level would produce, and increasing the light never decreases the rendered brightness

#### Scenario: Darkness with no light source

- **WHEN** the player views a Scribe GUI in a location with no sun light and no block light
  reaching them
- **THEN** the GUI renders at its minimum-brightness floor (see the configurable-floor
  requirement), making it dim and effortful to read rather than fully lit

#### Scenario: Held light source lifts the GUI in darkness

- **WHEN** the player stands in a dark location with no surrounding light but holds a lit
  torch/lantern/lamp in a hand (including the offhand)
- **THEN** the GUI renders lifted toward the brightness that held light provides — not left at
  the darkness floor — with a held lantern reading equivalently to a placed lantern of the same
  level and a dimmer held source (e.g. a torch) reading proportionally lower

### Requirement: GUI color temperature tracks the light's hue

The Scribe GUI SHALL tint its rendered colors toward the color/temperature of the light
reaching the player, not brightness alone. The color input SHALL combine the block-light RGB
component of `GetLightRGBs(playerPos)` (which carries a torch's warm hue) with the sky color
from `IAmbientManager.BlendedAmbientColor` (daylight/weather hue, which is absent from the
block-light grid) and, when a held light dominates, the hue of that held item's own light (so a
held torch, lantern, and oil lamp each read their own color temperature, taken from the item's
game data rather than hardcoded). The strength of the hue skew SHALL be softened (rendered
gentler than the physical light's hue), affecting only color and not the rendered brightness
level. The tint SHALL be applied as a single color transform over the whole composed dialog.

#### Scenario: Warm torch light in a cave

- **WHEN** the player views a Scribe GUI in a dark cave lit only by a nearby placed torch
- **THEN** the GUI takes on the warm/orange cast of the torch light, at the reduced brightness
  the torch provides

#### Scenario: Neutral daylight hue

- **WHEN** the player views a Scribe GUI in open daylight
- **THEN** the GUI's colors are not warm-shifted, matching the neutral cast of daylight

#### Scenario: Held sources differ in color temperature

- **WHEN** the player views a Scribe GUI in darkness while holding, in separate trials, a torch,
  a lantern, and an oil lamp
- **THEN** the GUI's hue differs between the three, each matching the color temperature of the
  held item's own emitted light rather than a single fixed warm cast

### Requirement: Illumination transitions are smoothed, not stepped

As the light reaching the player changes (walking between light and shadow, day/night shift, a
held light coming and going), the GUI's rendered brightness and hue SHALL ease toward the new
value over a short interval (on the order of ~400ms) rather than snapping instantly between
values. The smoothing SHALL be frame-rate independent. A newly opened dialog SHALL adopt the
current light immediately (no visible fade-up on open).

#### Scenario: Brightness glides as the player moves

- **WHEN** the player walks from a lit area into shadow (or vice versa) with a Scribe GUI open
- **THEN** the GUI's brightness transitions smoothly over a short interval rather than jumping
  abruptly from one level to the next

### Requirement: Configurable minimum-brightness floor

The mod SHALL expose a client-config value that sets the minimum brightness the GUI can be
shaded to in total darkness. The value SHALL live in the client settings
(`ScribePlayerSettings`, persisted to `ModConfig/scribe-client-config.json`; client-side,
per-machine, not synced). This value SHALL be the zero-light anchor of the brightness response
curve, not a separate post-curve clamp. The default SHALL keep the GUI dim-but-faintly-legible
in total darkness; the value SHALL be lowerable to an effectively-unreadable floor for players
who prefer maximum immersion, and raisable to full brightness to opt out of the effect entirely.
When the on-disk config key is absent, the code default SHALL apply.

#### Scenario: Default floor keeps text faintly legible

- **WHEN** a player with the default configuration views a Scribe GUI in total darkness
- **THEN** the GUI is heavily dimmed but its text remains faintly legible (not fully black)

#### Scenario: Lowered floor approaches unreadable

- **WHEN** a player has lowered the minimum-brightness floor to its minimum and views a Scribe
  GUI in total darkness
- **THEN** the GUI is shaded to near-black, so it cannot be read without introducing a light
  source

#### Scenario: Raised floor opts out of the effect

- **WHEN** a player has raised the minimum-brightness floor to its maximum (full brightness) and
  views a Scribe GUI in any lighting, including total darkness
- **THEN** the GUI renders at full brightness regardless of the surrounding light, effectively
  disabling the illumination effect

#### Scenario: Absent config key uses the default

- **WHEN** the on-disk `scribe-client-config.json` does not contain the minimum-brightness key
- **THEN** the mod applies the code-default floor rather than failing or rendering fully bright

### Requirement: Illumination applies uniformly across Scribe surfaces

The illumination shading SHALL apply to every Scribe surface that inherits the shared dialog
base (lectern, notebook, tablet, and future documents) by wrapping the shared composed body
once, rather than being wired per surface. The shading SHALL cover the dialog's backdrop,
chrome, and text together so no element remains fully bright while the rest is dimmed.

#### Scenario: Same shading on every document surface

- **WHEN** the player opens any Scribe document surface (lectern, notebook, or tablet) under
  the same lighting conditions
- **THEN** each surface is shaded by the same brightness and color as the others, with backdrop
  and text dimmed together

### Requirement: Illumination shading must not regress GUI interaction or stability

The illumination shading SHALL be a render-time-only effect. It SHALL NOT alter layout, hit
testing, caret position, scroll, focus, or any input behavior, and SHALL NOT reference the
Vintage Story API from `src/Core/`. Light sampling SHALL occur on the render/main thread. The
effect SHALL follow the established `SharedPaint` save/restore discipline so it does not leak
paint state into other GUI draws.

#### Scenario: Interaction is unaffected by shading

- **WHEN** the player types, clicks, scrolls, or focuses a row in a shaded Scribe GUI
- **THEN** the caret position, click target, scroll offset, and focus behave exactly as they do
  without the shading — the tint changes only rendered color, not interaction

#### Scenario: No paint-state leak into other draws

- **WHEN** a shaded Scribe dialog renders in the same frame as other GUI content
- **THEN** the color transform is confined to the Scribe dialog and does not tint or corrupt
  other GUI elements

### Requirement: Illumination shading extends to Scribe hover/overlay surfaces

The illumination shading SHALL also cover hover tooltips and other transient overlay surfaces that
belong to a Scribe document dialog, not only the dialog body. Because such surfaces render in the
LibGUI Overlay layer — outside the dialog body's single shading wrap — each Scribe-owned overlay
surface SHALL be shaded by re-wrapping its own content in the illumination tint built from the live
shade (the same approach the existing drop-up menu uses). A hover surface SHALL NOT render at full
brightness while the dialog body it belongs to is dimmed.

#### Scenario: Nav-button hover matches the body in low light

- **WHEN** the player hovers a Scribe dialog's nav button (or title-bar / editor / pinned-tab hover)
  while the dialog body is shaded down by low ambient light
- **THEN** the hover tooltip is shaded by the same live light, so it reads as part of the surface
  rather than a full-brightness panel floating above it

#### Scenario: Hover shading tracks light changes live

- **WHEN** the ambient light reaching the player changes while a Scribe hover tooltip is visible
- **THEN** the tooltip's shading updates to match, consistent with the dialog body

### Requirement: Hover surfaces are shaded at a reduced strength

Scribe hover/overlay surfaces SHALL be shaded at a reduced "hover strength" relative to the dialog
body: the applied shade SHALL be the live shade blended a fixed fraction of the way back toward
identity (no tint), yielding slightly less darkening than the body for legibility of transient text.
The body itself SHALL remain at full (unchanged) illumination strength. At full daylight (identity
shade) the reduced-strength shade SHALL likewise be identity, so bright conditions are unaffected.

#### Scenario: Tooltip is slightly brighter than the body in medium light

- **WHEN** the dialog body is shaded in medium light and the player hovers a Scribe-owned tooltip
- **THEN** the tooltip is dimmed by the same hue but by slightly less than the body (reduced hover
  strength), remaining legible without breaking the shared-lighting look

#### Scenario: No effect in full daylight

- **WHEN** ambient light is full/neutral (the body shade is identity)
- **THEN** the reduced-strength hover shade is also identity — hovers render exactly as they do
  today with no tint applied

### Requirement: Scribe Settings is excluded from hover shading

The Scribe Settings surface SHALL remain excluded from the illumination pass, including its hover
tooltips. Settings renders on the player's global LibGUI theme with no illumination wrap, so its help
tooltips SHALL continue to render at canonical (un-shaded) brightness.

#### Scenario: Settings tooltips stay canonical

- **WHEN** the player hovers a help tooltip inside the Scribe Settings dialog in any lighting
- **THEN** the tooltip is not shaded by the illumination pass — it renders at the global theme's
  normal brightness, matching the un-shaded Settings form

