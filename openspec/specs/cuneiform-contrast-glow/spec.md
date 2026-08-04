# cuneiform-contrast-glow Specification

## Purpose
TBD - created by archiving change add-tablet-clay-type-themes. Update Purpose after archive.
## Requirements
### Requirement: Cuneiform strokes render with a per-stroke outer glow for contrast

Cuneiform text rendered on the tablet (both the display-only title via `CuneiformTextRender` and the
editable rows via `ScribeCuneiformFieldRender`) SHALL paint a soft outer glow behind the ink strokes to
boost legibility over the mid-tone textured clay backdrop. The glow SHALL be produced by a blurred copy
of the stroke geometry drawn in a halo color, over which the crisp ink is drawn. The glow's color, blur
amount, and light-vs-dark polarity SHALL be per-clay-type values so each material's ink separates well
from its own backdrop.

The glow SHALL be a fixed, in-game-tuned effect (baked constants, in the manner of the jitter and
reveal-timing constants); it SHALL NOT be a persisted user setting.

#### Scenario: Ink reads clearly over the clay backdrop

- **WHEN** cuneiform text is rendered on a tablet with Pixel-Art Display ON
- **THEN** each stroke is backed by a soft glow that increases contrast against the backdrop, with the
  crisp ink drawn on top

#### Scenario: Glow uses per-material parameters

- **WHEN** the same cuneiform text is rendered on a red vs. a blue vs. a fire tablet
- **THEN** the glow color/polarity for each is the one authored for that clay type

### Requirement: Overlapping strokes within a glyph do not compound the glow

The glow SHALL be rendered in two passes over the visible (revealed) stroke range: a first pass drawing
all strokes' blurred halos, then a second pass drawing all strokes' crisp ink fills on top. As a
result, where strokes overlap within a glyph the halos SHALL NOT stack into a darker/brighter seam —
the crisp ink SHALL overwrite the halos so the glow is visible only where it extends past the ink onto
the backdrop. When jitter is active, both passes SHALL use the identical (jittered) stroke geometry so
the halo tracks the drawn ink.

#### Scenario: Dense/overlapping strokes glow uniformly

- **WHEN** a glyph with overlapping or adjacent strokes is rendered with the glow active
- **THEN** the halo appears as one uniform soft glow behind the whole letterform, with no darkened or
  doubled seam where strokes meet

#### Scenario: Reveal range is respected

- **WHEN** only part of a line is revealed (the press-in progression is mid-animation)
- **THEN** the glow passes cover exactly the revealed strokes, matching the crisp ink that is shown

### Requirement: Glow rendering leaves the shared paint state clean

The glow SHALL use the cached blur mask filter from the painting context (never disposing it
per-frame). After using it, the renderer SHALL reset the shared paint's mask filter to null (between
the two passes and before returning) and restore any other shared-paint properties it mutates, so that
subsequent unrelated draw operations are unaffected.

#### Scenario: Later draws are not blurred

- **WHEN** cuneiform glow rendering completes for a widget
- **THEN** the shared paint's mask filter is null and its color/style are restored, so the next widget
  painted with the shared paint is unaffected by the glow's filter

### Requirement: A dev console command tunes the glow at runtime

A client-side (dot-prefixed) dev console command SHALL allow adjusting the glow parameters (strength /
blur and halo polarity) at runtime and force a repaint of an open tablet, so tuning values can be
found in-game and reported back for baking. The command SHALL mutate in-memory tuning state only and
SHALL NOT persist anything. It is a developer aid, consistent with the existing `.cuneiform` harness.

#### Scenario: Live glow tuning

- **WHEN** a developer runs the glow dev command with new parameter values while a tablet is open
- **THEN** the open tablet repaints with the adjusted glow, and nothing is written to disk

