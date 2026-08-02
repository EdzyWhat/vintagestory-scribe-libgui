## ADDED Requirements

### Requirement: A backdrop spec may tile a material swatch and composite a frame overlay

A backdrop specification MAY declare that its texture is a small tiling material swatch rather than a
full-page illustration. When it does, the dialog backdrop-wrapping logic SHALL paint that swatch at its
native pixel resolution repeated (tiled) across the dialog area instead of stretching one copy to fill,
so the material reads crisp rather than blurrily upscaled. A backdrop specification MAY also declare an
optional tint color applied to the swatch, and an optional page-frame overlay texture composited on top
of the tiled swatch so the result reads as a framed page. A backdrop specification that declares none of
these (the existing full-page specs) SHALL continue to be rendered by the current stretch-to-fill path
unchanged.

#### Scenario: A tiling swatch is rendered crisp, not stretched

- **WHEN** a backdrop spec declaring a small tiling material swatch is drawn behind a dialog with
  themed mode ON
- **THEN** the swatch is painted at native resolution tiled across the dialog area, rather than a single
  copy stretched to fill and upscaled blurry

#### Scenario: An optional tint distinguishes same-source swatches

- **WHEN** two backdrop specs name the same material swatch but declare different tint colors
- **THEN** each renders the swatch in its own tint so the two are visually distinguishable

#### Scenario: An optional frame overlay is composited over the tile

- **WHEN** a tiling backdrop spec also declares a page-frame overlay texture
- **THEN** the frame overlay is drawn on top of the tiled swatch so the backdrop reads as a framed page

#### Scenario: Full-page specs are unchanged

- **WHEN** an existing full-page backdrop spec (declaring no tiling, tint, or frame overlay) is drawn
- **THEN** it renders through the existing stretch-to-fill path exactly as before
