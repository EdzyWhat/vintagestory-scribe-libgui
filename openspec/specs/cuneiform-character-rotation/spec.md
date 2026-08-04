# cuneiform-character-rotation Specification

## Purpose
TBD - created by archiving change tune-tablet-jitter-add-rotation. Update Purpose after archive.
## Requirements
### Requirement: A deterministic Core transform rotates a whole character rigidly about its box center

Scribe SHALL provide a pure, VS-API-free Core transform that rotates a positioned glyph rigidly about
its own box center by a small angle, as a function of the stroke geometry, an integer seed, and a
maximum-angle value (in degrees), returning new stroke geometry. The transform SHALL apply the same
angle to every stroke of a given character instance (a rigid rotation of the whole glyph, never a
per-stroke shear), rotating each endpoint about the glyph box center using the standard 2D rotation
matrix. It SHALL use seeded randomness (mirroring `GlyphStrokeJitter`) so the same (character, seed,
max-angle) always yields the same angle. A maximum angle of zero SHALL return the geometry unchanged.
The chosen angle SHALL be bounded to `[-maxDegrees, +maxDegrees]`.

#### Scenario: The transform is a pure, reproducible function

- **WHEN** the rotation transform is applied twice to the same character with the same seed and max angle
- **THEN** it produces byte-identical output both times

#### Scenario: Max angle zero is the identity

- **WHEN** the rotation transform is applied with a maximum angle of zero
- **THEN** the returned geometry equals the input geometry exactly (today's upright glyphs are preserved)

#### Scenario: The whole character shares one angle

- **WHEN** the rotation transform is applied to a character made of more than one stroke at a positive max angle
- **THEN** every stroke of that character is rotated by the same angle about the same box center, so relative
  stroke positions within the glyph are preserved (the glyph tilts as a rigid unit)

#### Scenario: The angle stays within the bound

- **WHEN** the rotation transform is applied at a positive max angle across many seeds
- **THEN** every chosen angle lies within `[-maxDegrees, +maxDegrees]`

### Requirement: Rotation is stable per character identity, not per frame

The rotation angle SHALL be derived from a stable character identity (a per-field/document base seed
combined with the stroke's source character index), NOT from any frame counter or wall-clock value, and
SHALL NOT depend on the glyph-local stroke ordinal (so all strokes of one character resolve to the same
angle). Consequently repeated glyphs of the same character SHALL tilt differently from each other, while
any one character SHALL render at the same angle on every frame (no wobble) and on every re-open at the
same base seed.

#### Scenario: A character does not wobble between frames

- **WHEN** the same text is rendered on consecutive frames without editing
- **THEN** each character's tilt angle is identical frame to frame

#### Scenario: Repeated characters tilt differently

- **WHEN** a line contains the same character more than once
- **THEN** the repeated glyphs are not rotated by the same angle (they tilt differently by identity)

### Requirement: Rotation is visual-only and never affects layout, caret, or hit-testing

Rotation SHALL be applied only to the drawn stroke geometry at paint time, composed AFTER the jitter
transform. Layout metrics — total width, character boundaries, line wrapping — and the caret position,
selection region, and click hit-testing SHALL all be computed from the un-rotated, un-jittered layout, so
enabling rotation never moves the caret, changes where a click lands, or changes how text wraps.

#### Scenario: Layout metrics are identical with rotation on or off

- **WHEN** the same text is laid out with rotation enabled and with rotation disabled
- **THEN** the total width, character boundaries, and wrapped line breaks are identical

#### Scenario: The caret and clicks ignore rotation

- **WHEN** rotation is enabled in an editable cuneiform field
- **THEN** the caret sits at the same position and a click selects the same character index as with rotation
  disabled

#### Scenario: Rotation composes after jitter

- **WHEN** both jitter and rotation are active for a stroke
- **THEN** the stroke is first jittered (endpoints perturbed) and then the resulting geometry is rotated about
  the glyph box center, matching the `glyph-forge` jitter-then-rotate ordering

