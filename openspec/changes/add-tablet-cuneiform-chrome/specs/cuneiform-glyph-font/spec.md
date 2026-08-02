## ADDED Requirements

### Requirement: Cuneiform text wraps or truncates to a bounded width

The cuneiform layout SHALL support laying a string out into MULTIPLE lines given a maximum width,
breaking at word-gap (space) boundaries — a cuneiform analogue of normal text soft-wrap — so that
cuneiform text placed in bounded chrome does not overrun its container. This wrapping SHALL be a
`Core` layout concern (no Vintage Story API reference) and SHALL be unit-testable. The single-line
layout API SHALL be preserved for callers that do not wrap (wrapping is opt-in via a maximum width;
with no maximum, layout produces one line exactly as before). A word that is itself wider than the
maximum width SHALL NOT throw.

#### Scenario: A long string wraps at word boundaries

- **WHEN** a multi-word string is laid out with a maximum width smaller than its single-line width
- **THEN** it is broken into multiple lines at space boundaries, each no wider than the maximum where
  a break point exists
- **AND** the render widget stacks the resulting lines vertically, sizing its height to the line count

#### Scenario: No maximum width preserves single-line layout

- **WHEN** a string is laid out with no maximum width
- **THEN** it produces exactly one line, identical to the pre-existing single-line layout

#### Scenario: An over-long word does not crash layout

- **WHEN** a single word is wider than the maximum width and has no interior break point
- **THEN** layout completes without throwing (the word occupies its own line rather than being split
  mid-glyph)

### Requirement: Cuneiform text in a fixed-height band truncates rather than growing

The cuneiform render path SHALL support single-line truncation for text placed in a fixed-height band
(the tablet title bar), clipping the line at the available width rather than wrapping onto additional
lines that would push the band taller. Because the authored glyph set contains no ellipsis glyph, the
truncation SHALL use a deliberate width cutoff affordance rather than an ellipsis character.

#### Scenario: A long title clips within the title band

- **WHEN** a tablet title longer than the title band's inner width is rendered in cuneiform
- **THEN** it is clipped to the band's width on a single line, without growing the band's height and
  without overrunning the band's edge

### Requirement: Cuneiform layout exposes a per-character advance position

The cuneiform layout SHALL expose, for a laid-out line, the cumulative advance position (in grid
units) at each source-character boundary, so a caller can map a character index to a horizontal
position and a horizontal position back to the nearest character index. This mapping SHALL account
for the layout's own handling of folded case, spaces (word gaps), and missing glyphs, so that a source
character index maps to a stable position. This SHALL be a `Core` layout concern (no Vintage Story API
reference) and unit-testable. It enables an editor to place a synthetic caret and hit-test clicks
against cuneiform text; the drawing of the caret itself is a render-layer concern outside this
requirement.

#### Scenario: Character index maps to an advance position

- **WHEN** a line is laid out and a caller requests the position at character index N
- **THEN** the layout returns the cumulative advance position at that boundary, consistent with the
  positions of the rendered strokes

#### Scenario: A click position maps back to the nearest character

- **WHEN** a caller provides a horizontal position within a laid-out line
- **THEN** the layout resolves it to the nearest character boundary index

#### Scenario: Spaces and missing glyphs keep indices stable

- **WHEN** a line contains spaces or characters with no authored glyph
- **THEN** each source character still maps to a distinct, monotonically increasing advance position
  (the word gap or missing-glyph gap advances the position), so caret indices remain stable across
  those characters
