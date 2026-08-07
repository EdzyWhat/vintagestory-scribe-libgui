# cuneiform-handwriting-jitter Specification

## Purpose
TBD - created by archiving change add-cuneiform-handwriting-feel. Update Purpose after archive.
## Requirements
### Requirement: A deterministic Core transform jitters a stroke within the 2-point + weight model

Scribe SHALL provide a pure, VS-API-free Core transform that perturbs a `GlyphStroke` — nudging its endpoint
positions (thereby its angle and length) and its weight (width) — as a function of the stroke, an integer
seed, and a strength value, returning a new `GlyphStroke`. It SHALL use seeded randomness (mirroring
`ScribeTextCorruptor`) so the same (stroke, seed, strength) always yields the same perturbed stroke. A
strength of zero SHALL return the stroke unchanged. Perturbation magnitude SHALL be bounded and expressed
relative to the glyph grid size so it scales with glyph size.

#### Scenario: The transform is a pure, reproducible function

- **WHEN** the jitter transform is applied twice with the same stroke, seed, and strength
- **THEN** it produces byte-identical output both times

#### Scenario: Strength zero is the identity

- **WHEN** the jitter transform is applied with strength zero
- **THEN** the returned stroke equals the input stroke exactly (today's crisp geometry is preserved)

#### Scenario: Different seeds diverge, magnitude stays bounded

- **WHEN** the jitter transform is applied to the same stroke with two different seeds at a positive strength
- **THEN** the two outputs differ, and each output's endpoints and weight stay within the strength-bounded
  range of the input

### Requirement: Jitter is stable per stroke identity, not per frame

The jitter seed SHALL be derived from a stable stroke identity (a per-field/document base seed combined with
the stroke's source character index and glyph-local stroke ordinal), NOT from any frame counter or wall-clock
value. Consequently repeated glyphs of the same character SHALL render differently from each other, while any
one stroke SHALL render identically on every frame (no shimmer) and on every re-open at the same base seed.

#### Scenario: A stroke does not shimmer between frames

- **WHEN** the same text is rendered on consecutive frames without editing
- **THEN** each stroke's jittered geometry is identical frame to frame

#### Scenario: Repeated characters differ from one another

- **WHEN** a line contains the same character more than once
- **THEN** the repeated glyphs are not pixel-identical (their strokes jitter differently by identity)

### Requirement: Jitter is visual-only and never affects layout, caret, or hit-testing

Jitter SHALL be applied only to the drawn stroke geometry at paint time. Layout metrics — total width,
character boundaries, line wrapping — and the caret position, selection region, and click hit-testing SHALL
all be computed from the un-jittered layout, so enabling jitter never moves the caret, changes where a click
lands, or changes how text wraps.

#### Scenario: Layout metrics are identical with jitter on or off

- **WHEN** the same text is laid out with jitter enabled and with jitter disabled
- **THEN** the total width, character boundaries, and wrapped line breaks are identical

#### Scenario: The caret and clicks ignore jitter

- **WHEN** jitter is enabled in an editable cuneiform field
- **THEN** the caret sits at the same position and a click selects the same character index as with jitter
  disabled

### Requirement: Stroke identity is carried through layout

`PositionedStroke` SHALL carry a stable identity for each stroke — its source character index within the line
and its glyph-local stroke ordinal — populated by `CuneiformLineLayout` at emit time, so both the jitter seed
and the per-letter reveal can key off it. Adding this identity SHALL NOT change the emitted stroke geometry or
construction order.

#### Scenario: Each positioned stroke knows its letter and ordinal

- **WHEN** a line is laid out
- **THEN** every emitted `PositionedStroke` reports the source character index it belongs to and its ordinal
  within that glyph, and the stroke geometry and order are unchanged from before the identity was added

