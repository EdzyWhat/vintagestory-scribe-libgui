## MODIFIED Requirements

### Requirement: Cuneiform strokes render with a per-stroke outer glow for contrast

Cuneiform text rendered on the tablet (both the display-only title via `CuneiformTextRender` and the
editable rows via `ScribeCuneiformFieldRender`) SHALL paint a soft outer glow behind the ink strokes to
boost legibility over the textured clay backdrop. The glow SHALL be produced by a blurred copy
of the stroke geometry drawn in a halo color, over which the crisp ink is drawn.

The glow's color, blur amount, and light-vs-dark polarity SHALL be selected by **both** the tablet's
clay material **and** its life-cycle state (Wet, Hard, or Fired), so each backdrop pairs with a halo
that separates the ink from it:

- **Wet** tablets SHALL use per-material dark-polarity halos (a dark halo behind dark ink over the
  lighter mid-tone wet backdrop).
- **Hard** and **Fired** tablets have darker backdrops, so they SHALL use a **light-polarity** halo (a
  light halo behind dark ink lifts the ink off the dark ground). The Hard and Fired halos SHALL be
  **distinct from each other**, and each SHALL be **uniform across all clay colors** (blue, red, and
  fire share one halo per state).
- Wax has no hardened or fired state and SHALL retain its single (wet-style) halo.

The ink color and the backdrop/theme SHALL NOT change as part of glow selection — glow is the only
thing that varies by state.

The glow SHALL be a fixed, in-game-tuned effect (baked constants, in the manner of the jitter and
reveal-timing constants); it SHALL NOT be a persisted user setting.

#### Scenario: Ink reads clearly over the clay backdrop

- **WHEN** cuneiform text is rendered on a tablet with Pixel-Art Display ON
- **THEN** each stroke is backed by a soft glow that increases contrast against the backdrop, with the
  crisp ink drawn on top

#### Scenario: Glow uses per-material parameters

- **WHEN** the same cuneiform text is rendered on a wet red vs. a wet blue vs. a wet fire tablet
- **THEN** the glow color/polarity for each is the dark-polarity halo authored for that clay type

#### Scenario: Hardened and fired tablets use a light halo

- **WHEN** cuneiform text is rendered on a hardened or a fired tablet (any clay color)
- **THEN** the glow is the light-polarity halo authored for that state, so the dark ink lifts off the
  darker backdrop, and the halo for hardened differs from the halo for fired

#### Scenario: State changes the halo without changing the ink

- **WHEN** the same tablet transitions from wet to hardened to fired
- **THEN** only the glow halo changes between states; the ink color and the theme/backdrop selection
  are unaffected by the glow lookup
