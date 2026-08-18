## ADDED Requirements

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
