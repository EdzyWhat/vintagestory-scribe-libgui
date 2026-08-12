## ADDED Requirements

### Requirement: GUI brightness tracks light reaching the player

The Scribe GUI SHALL render at a brightness derived from the light reaching the player's
current position, so that the GUI is fully bright in bright surroundings and progressively
dimmer as the surroundings darken. The brightness input SHALL combine the sun-brightness
component of `IBlockAccessor.GetLightRGBs(playerPos)` (the W component) with the ambient
scene brightness from `IAmbientManager` (`BlendedSceneBrightness`), so that both time-of-day
and weather/rain darkening are reflected — matching how the surrounding world is lit.

#### Scenario: Bright neutral daylight

- **WHEN** the player views a Scribe GUI outdoors at noon under clear sky
- **THEN** the GUI renders at full brightness, visually unchanged from the pre-illumination
  behavior

#### Scenario: Reduced light under rain

- **WHEN** the player views a Scribe GUI outdoors while it is raining (the surrounding scene
  is darker than clear noon)
- **THEN** the GUI renders dimmer than it does under clear noon, tracking the reduced ambient
  scene brightness

#### Scenario: Darkness with no light source

- **WHEN** the player views a Scribe GUI in a location with no sun light and no block light
  reaching them
- **THEN** the GUI renders at its minimum-brightness floor (see the configurable-floor
  requirement), making it dim and effortful to read rather than fully lit

### Requirement: GUI color temperature tracks the light's hue

The Scribe GUI SHALL tint its rendered colors toward the color/temperature of the light
reaching the player, not brightness alone. The color input SHALL combine the block-light RGB
component of `GetLightRGBs(playerPos)` (which carries a torch's warm hue) with the sky color
from `IAmbientManager.BlendedAmbientColor` (daylight/weather hue, which is absent from the
block-light grid). The tint SHALL be applied as a single color transform over the whole
composed dialog.

#### Scenario: Warm torch light in a cave

- **WHEN** the player views a Scribe GUI in a dark cave lit only by a nearby placed torch
- **THEN** the GUI takes on the warm/orange cast of the torch light, at the reduced brightness
  the torch provides

#### Scenario: Neutral daylight hue

- **WHEN** the player views a Scribe GUI in open daylight
- **THEN** the GUI's colors are not warm-shifted, matching the neutral cast of daylight

### Requirement: Configurable minimum-brightness floor

The mod SHALL expose a client-config value that sets the minimum brightness the GUI can be
shaded to in total darkness. The value SHALL live in `ScribeClientConfig` (client-side,
per-machine, not synced). The default SHALL keep the GUI dim-but-faintly-legible in total
darkness; the value SHALL be lowerable to an effectively-unreadable floor for players who
prefer maximum immersion. When the on-disk config key is absent, the code default SHALL apply.

#### Scenario: Default floor keeps text faintly legible

- **WHEN** a player with the default configuration views a Scribe GUI in total darkness
- **THEN** the GUI is heavily dimmed but its text remains faintly legible (not fully black)

#### Scenario: Lowered floor approaches unreadable

- **WHEN** a player has lowered the minimum-brightness floor to its minimum and views a Scribe
  GUI in total darkness
- **THEN** the GUI is shaded to near-black, so it cannot be read without introducing a light
  source

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
