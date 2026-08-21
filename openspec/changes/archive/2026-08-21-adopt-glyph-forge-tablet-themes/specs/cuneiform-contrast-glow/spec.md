## MODIFIED Requirements

### Requirement: Cuneiform strokes render with a per-stroke outer glow for contrast

Cuneiform text rendered on the tablet (both the display-only title via `CuneiformTextRender` and the
editable rows via `ScribeCuneiformFieldRender`) SHALL paint a soft outer glow behind the ink strokes to
boost legibility over the textured clay backdrop. The glow SHALL be produced by a blurred copy
of the stroke geometry drawn in a halo color, over which the crisp ink is drawn. The glow's color, blur
amount, light-vs-dark polarity, and directional **offset** SHALL be per-clay-type **and
per-drying-state** values, resolved from the tablet readability bundle for the current
`(material, state)` view, so each material's ink separates well from its own backdrop in each state.

Each `(material, state)` view SHALL author its glow **independently** — the lookup returns a
per-view value, so any view MAY diverge from any other, including two views in the same state. The
authored values SHALL follow backdrop luminance per state as the guiding polarity: **wet** clay
backdrops are light-mid tones written with dark ink, so wet views use a **dark** halo (a soft,
ink-derived seating shadow), while the darker **hard** and **fired** backdrops use a **light** halo
(lifting dark ink off a dark ground). The model SHALL NOT require views within a state to share a
value: today's authored values happen to give the three wet clays the same dark seed and align
stroke-scale/offset by state, but the table permits each cell to differ freely in a future retune.
Wax has only a wet view. The glow MAY carry a small directional offset (a fraction of the em) so the
halo reads as a seated drop rather than a symmetric aura; a zero offset SHALL reproduce a centered halo.

The glow SHALL be a fixed, in-game-tuned effect (baked constants, in the manner of the jitter and
reveal-timing constants); it SHALL NOT be a persisted user setting.

#### Scenario: Ink reads clearly over the clay backdrop

- **WHEN** cuneiform text is rendered on a tablet with Pixel-Art Display ON
- **THEN** each stroke is backed by a soft glow that increases contrast against the backdrop, with the
  crisp ink drawn on top

#### Scenario: Glow uses per-material and per-state parameters

- **WHEN** the same cuneiform text is rendered on a red vs. a blue vs. a fire tablet, and on the wet vs.
  hard vs. fired state of one clay
- **THEN** the glow color, polarity, blur, and offset for each is the one authored for that
  `(material, state)` view — wet uses the shared dark halo, hard and fired use that clay's light halo

#### Scenario: Each view's glow is authored independently

- **WHEN** the wet views of red, blue, and fire are rendered, then the hard and fired views of each
- **THEN** each view uses the halo authored for its own `(material, state)` cell — wet views use a
  dark-polarity halo and hard/fired views use a light-polarity halo — and changing one cell's value
  does not change any other cell (the current wet views happening to share a seed is data, not a rule)
