## ADDED Requirements

### Requirement: A backdrop spec may tint its texture

A backdrop specification MAY declare an optional tint color multiplied into its texture. When a tint is
declared, the dialog backdrop-loading logic SHALL bake that tint into a cached copy of the decoded bitmap
(an `SKColorFilter` modulate — the same tint primitive the GUI framework's icon renderer uses) and render
the tinted copy through the existing stretch-to-fill texture path, so the same source PNG can back several
visually-distinct specs without additional art. A backdrop specification that declares no tint (every
full-page illustration spec) SHALL be rendered from the decoded bitmap unchanged through the existing
stretch-to-fill path.

Note: an earlier draft of this requirement anticipated tiling a small vanilla material swatch at native
resolution plus a composited page-frame overlay. Implementation found (a) the authored clay backdrops are
full-page illustrations that take the existing stretch path directly, so no tiling was needed, and (b) the
GUI framework's `BoxStyle` texture path only ever stretches one bitmap to fill and exposes no tint, and it
lives in the read-only `gui` dependency, so tiling/frame-overlay could not be added there. The tint is
therefore baked at the bitmap level and the tiling/overlay machinery was dropped — the full-page authored
art is the design's own stated target state, reached directly.

#### Scenario: An optional tint distinguishes same-source specs

- **WHEN** two backdrop specs name the same source PNG but declare different tint colors
- **THEN** each renders that PNG in its own tint so the two are visually distinguishable

#### Scenario: Full-page specs are unchanged

- **WHEN** an existing full-page backdrop spec (declaring no tint) is drawn
- **THEN** it renders through the existing stretch-to-fill path exactly as before
