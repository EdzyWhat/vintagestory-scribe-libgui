# tablet-readability-style Specification

## Purpose
TBD - created by archiving change adopt-glyph-forge-tablet-themes. Update Purpose after archive.

## Requirements

### Requirement: A single readability bundle is the source of truth per tablet view

The tablet's text readability values SHALL be modeled as one bundle keyed by the pair
`(clay-material, drying-state)` — one bundle per distinct tablet view. The bundle SHALL carry
the four value groups that the glyph-forge readability tool exports for that view: the **body
ink** color, the **link ink** color, a **stroke-weight scale** (a multiplier on the cuneiform
stroke weight), and the **outer glow** (halo color, strength, blur fraction, and directional
offset). The bundle values SHALL be baked constants seeded from the glyph-forge exports; they
SHALL NOT be loaded from a shipped asset or persisted as a user setting.

There SHALL be a bundle for each of the ten authored views: clay red/blue/fire × wet/hard/fired,
plus wax (wet only). `wax` has no hard/fired variant, so it SHALL only ever resolve as wet, and
any unrecognized material SHALL resolve to the fire bundle for the same state (its backdrop
twin), so the resolved readability values always agree with the resolved theme and backdrop.

#### Scenario: Each view resolves its own bundle

- **WHEN** a tablet of a given clay material and drying state is opened with Pixel-Art Display ON
- **THEN** exactly one readability bundle is resolved for that `(material, state)` pair, carrying
  that view's body ink, link ink, stroke-weight scale, and glow

#### Scenario: Wax and unknown materials resolve safely

- **WHEN** the bundle is resolved for a `wax` tablet, or for an unrecognized material in any state
- **THEN** wax resolves its own wet bundle (it has no hard/fired variant) and an unrecognized
  material resolves the fire bundle for the same state, matching the theme/backdrop fallback

### Requirement: The dialog resolves the bundle once and decomposes it into existing seams

The tablet dialog SHALL resolve the readability bundle a single time when it opens (or rebuilds)
for its current material and state, then decompose it into the styling seams that already exist,
rather than performing separate per-value lookups keyed differently. Specifically: the body ink
SHALL feed the theme's `OnSurface` role, the link ink SHALL feed the row style's `LinkColor`, the
glow SHALL feed the cuneiform render's glow parameter, and the stroke-weight scale SHALL feed the
cuneiform render's stroke weight. No other surface (Lectern, Notebook, Chalkboard, or the
non-cuneiform readable path) SHALL consume the bundle.

#### Scenario: One resolution feeds every consumer

- **WHEN** the tablet dialog builds its content for a given `(material, state)`
- **THEN** the ink, link ink, glow, and stroke-weight scale all originate from the same resolved
  bundle, so a change to one view's bundle moves all four together and cannot drift apart

#### Scenario: Non-tablet surfaces are untouched

- **WHEN** a Lectern, Notebook, or Chalkboard dialog is built, or any dialog renders on the
  non-cuneiform readable path
- **THEN** no readability bundle is resolved or applied, and those surfaces render exactly as
  before this change

### Requirement: Stroke-weight scale multiplies the cuneiform stroke weight per view

The cuneiform render SHALL multiply its base stroke weight by the resolved bundle's stroke-weight
scale so a view's strokes can be authored lighter or heavier without changing the glyph geometry.
A scale of `1.0` SHALL reproduce the current weight exactly. The scale SHALL apply only to the
cuneiform tablet render path; the non-cuneiform font path SHALL be unaffected. This SHALL NOT
require any change to `src/Core/` — the scale is a Mod-side multiplier on the existing per-stroke
weight.

#### Scenario: Heavier strokes on a fired tablet

- **WHEN** cuneiform text is rendered on a view whose bundle stroke-weight scale is greater than
  `1.0`
- **THEN** the strokes render proportionally heavier than the same text on a view whose scale is
  `1.0`, with identical stroke geometry and layout

#### Scenario: Unit scale is a no-op

- **WHEN** a view's stroke-weight scale is `1.0`
- **THEN** the cuneiform strokes render at exactly the current (pre-change) weight
