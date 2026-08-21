## MODIFIED Requirements

### Requirement: Cuneiform strokes render with a per-stroke outer glow for contrast

Cuneiform text rendered on the tablet (both the display-only title via `CuneiformTextRender` and the
editable rows via `ScribeCuneiformFieldRender`) SHALL paint a soft outer glow behind the ink strokes to
boost legibility over the mid-tone textured clay backdrop. The glow SHALL be produced by a blurred copy
of the stroke geometry drawn in a halo color, over which the crisp ink is drawn. The glow's color, blur
amount, and light-vs-dark polarity SHALL be per-clay-type values so each material's ink separates well
from its own backdrop.

For the light-ish clay and wax palettes (dark ink on a pale-to-mid ground), the halo polarity SHALL be
**dark** — a soft, tight dark halo that reads as a seating outline / engraved shadow behind the dark
ink — because a light halo on a light-mid ground reduces edge contrast (it bleeds into the dark stroke
edges and adds no separating luminance step) rather than increasing it. A light halo remains correct
only for the inverse case (light ink on a dark ground), which the clay/wax palettes are not. The dark
halo SHALL be derived from (or tuned to) each palette's own near-black ink so it stays in-hue, and its
blur SHALL be tight enough to read as an outline rather than a wide aura.

The glow SHALL be a fixed, in-game-tuned effect (baked constants, in the manner of the jitter and
reveal-timing constants); it SHALL NOT be a persisted user setting.

#### Scenario: Ink reads clearly over the clay backdrop

- **WHEN** cuneiform text is rendered on a tablet with Pixel-Art Display ON
- **THEN** each stroke is backed by a soft dark halo that increases contrast against the light-mid
  backdrop, with the crisp ink drawn on top

#### Scenario: Glow uses per-material parameters

- **WHEN** the same cuneiform text is rendered on a red vs. a blue vs. a fire vs. a wax tablet
- **THEN** the dark halo color for each is the one authored for that clay type (and wax uses its own
  seed rather than riding the fire seed)

#### Scenario: A dark halo does not darken the glyph interior

- **WHEN** a glyph is rendered with the dark halo active
- **THEN** the crisp ink drawn on top overwrites the halo inside the letterform, so the dark halo shows
  only as a thin darkened fringe where it spills past the ink onto the clay
