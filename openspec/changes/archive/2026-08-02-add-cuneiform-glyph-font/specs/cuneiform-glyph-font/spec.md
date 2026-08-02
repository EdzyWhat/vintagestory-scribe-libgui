## ADDED Requirements

### Requirement: A game-agnostic glyph model parses glyph-forge stroke geometry

The system SHALL provide a Core (game-agnostic) glyph model that parses `glyph-forge` glyph
geometry into a per-character record of ordered strokes on a square em-grid, where each stroke is a
centerline (`start`, `end`) plus a `weight`, expressed in grid units. The model MUST NOT reference
the Vintage Story API, so it is unit-testable with `dotnet test` and no game install. The stroke
order MUST be preserved exactly as authored (it is the carving/reveal sequence and must never be
sorted or reordered).

#### Scenario: A glyph parses into ordered strokes

- **WHEN** a glyph's JSON with an ordered `strokes` array is parsed
- **THEN** the model exposes the same strokes in the same order, each with its `start`, `end`, and
  `weight` in grid units
- **AND** the glyph exposes its `gridSize`, `leftWidth`/`rightWidth`, and
  `leftPadding`/`rightPadding`

#### Scenario: Legacy glyph shapes are migrated on read

- **WHEN** a glyph file has no `leftWidth`/`rightWidth` (an older shape carrying only `width`, only
  `advanceWidth`, only strokes, or nothing)
- **THEN** the model derives `leftWidth`/`rightWidth` per the export-format migration ladder (even
  split for `width`/`advanceWidth`; bounding-box-derived for strokes-only; a fixed default for an
  empty glyph) rather than rejecting the file

### Requirement: Each stroke resolves to an explicit rectangle

The system SHALL compute each stroke's four rectangle corners from its centerline and weight as an
oriented (arbitrary-angle) rectangle centered on the centerline with square-cut ends, using the
perpendicular-vector construction defined by the glyph export format. It MUST NOT approximate a
stroke as an axis-aligned box.

#### Scenario: Corner geometry matches the export format

- **WHEN** the four corners of a stroke with a known `start`, `end`, and `weight` are computed
- **THEN** the corners equal `start ± p`, `end ± p` where `p` is the unit perpendicular scaled to
  half the weight, matching the export format's corner computation
- **AND** a diagonal stroke yields a rotated rectangle, not an axis-aligned bounding box

### Requirement: A string lays out into positioned strokes with proportional advance

The system SHALL lay a string out into a flat, construction-ordered list of positioned strokes plus
a total advance width, in grid units. Layout SHALL advance the pen by each glyph's own footprint
(`leftWidth + rightWidth`), position each glyph's strokes relative to its box's left edge (not local
x = 0) per the export format, and separate neighbors by a hard padding floor
(`rightPadding(prev) + leftPadding(next)`) that a per-pair kerning value MAY widen but MUST NOT
narrow past the floor. The construction order of strokes across the whole line SHALL be preserved so
that rendering the first N strokes yields a partial reveal.

#### Scenario: Adjacent glyphs advance by footprint and padding

- **WHEN** two glyphs are laid out in sequence
- **THEN** the second glyph's strokes are offset from the first by the first glyph's footprint plus
  the padding floor between them
- **AND** the returned total width equals the pen position after the last glyph

#### Scenario: Kerning only widens the gap

- **WHEN** a kerning entry exists for an ordered character pair
- **THEN** a positive kerning value widens the gap beyond the padding floor
- **AND** a value that would narrow the gap below the padding floor is clamped to the floor

#### Scenario: Partial reveal is a prefix of the stroke list

- **WHEN** the first N positioned strokes of a laid-out line are rendered
- **THEN** they are exactly the first N strokes in authored construction order across the line

### Requirement: Input is folded to the authored character set

The authored glyph set is uppercase A–Z, digits 0–9, and a fixed set of punctuation — it contains
no lowercase letters and no space glyph. The layout SHALL fold input to uppercase before glyph
lookup, SHALL advance a fixed word-gap for a space character while emitting no strokes for it, and
SHALL handle a character with no authored glyph without throwing (advancing a small gap and emitting
no strokes).

#### Scenario: Lowercase input renders using uppercase glyphs

- **WHEN** a string containing lowercase letters is laid out
- **THEN** each lowercase letter is rendered using its uppercase glyph's strokes

#### Scenario: Spaces advance without strokes

- **WHEN** a string containing a space is laid out
- **THEN** the pen advances by the fixed word-gap at the space
- **AND** no strokes are emitted for the space

#### Scenario: An unauthored character does not crash layout

- **WHEN** a string contains a character with no authored glyph and no space handling
- **THEN** layout completes without throwing, advancing a small gap and emitting no strokes for that
  character

### Requirement: The glyph geometry ships as one bundled, scanned asset

The mod SHALL ship the full authored glyph set as a single combined JSON asset produced by the
`glyph-forge` bundler, placed under the mod's `textures/` asset tree so the game's asset scanner
loads it, and load it client-side by asset location (re-fetching the asset bytes on demand so the
game unloading asset data does not leave a stale null). The mechanism SHALL add no new package or
mod dependency and SHALL NOT register the geometry through the Skia `FontRegistry` (that path is for
real TTF typefaces only).

#### Scenario: The bundle loads and parses at client init

- **WHEN** the client initializes with the bundled glyph JSON present under the mod's `textures/`
  tree
- **THEN** the mod loads the asset by location and parses it into the glyph model
- **AND** the loaded bundle contains the full authored character set

#### Scenario: The bundle survives asset unload

- **WHEN** the game unloads asset data and the glyph geometry is next needed
- **THEN** the mod re-fetches the asset bytes rather than using a stale null reference

### Requirement: A custom render widget paints cuneiform strokes on the Skia canvas

The mod SHALL provide a custom LibGUI render widget that paints a laid-out line's stroke rectangles
by filling each stroke's oriented quad on the LibGUI Skia canvas, scaling grid units to pixels by
the requested em size, using the color supplied by the active theme. It SHALL size itself to the
laid-out line and SHALL guard against a null canvas.

#### Scenario: A line of text renders as filled stroke quads

- **WHEN** the widget is given a string and an em size and painted
- **THEN** it fills each stroke's four-corner quad on the canvas, scaled from grid units to pixels
- **AND** the widget's measured size matches the laid-out line's total advance and line height

#### Scenario: Painting is skipped when the canvas is unavailable

- **WHEN** the widget is painted while the canvas is null
- **THEN** it returns without throwing

### Requirement: The reveal animation is optional and defaults to fully revealed

The render widget SHALL support an optional stroke-by-stroke reveal in authored construction order,
driven so that a reveal fraction maps to a count of leading strokes shown. When no animation is
active the widget SHALL display the full line (all strokes revealed).

#### Scenario: No animation shows the whole line

- **WHEN** the widget is displayed with no active reveal animation
- **THEN** all strokes of the line are painted

#### Scenario: A reveal fraction shows a leading prefix

- **WHEN** the reveal fraction is set to a value between 0 and 1
- **THEN** only the leading strokes up to that fraction of the line's total stroke count are painted,
  in authored construction order

### Requirement: A client setting disables the cuneiform font and falls back to the task font

The system SHALL provide a per-player, client-local `DisableCuneiformFont` setting that defaults to
off (cuneiform enabled), persists across restarts, and is never synchronized to the server. When the
setting is on, text that would render in cuneiform SHALL instead render in the player's selected task
font, resolved through the existing task-font resolution chokepoint, at a single branch point rather
than scattered conditionals.

#### Scenario: The setting exists, defaults off, and persists

- **WHEN** a player has not changed the setting
- **THEN** `DisableCuneiformFont` is off and cuneiform rendering is active
- **AND** a changed value persists across a game restart and is not sent to the server

#### Scenario: Disabling falls back to the selected task font

- **WHEN** `DisableCuneiformFont` is on
- **THEN** text that would render in cuneiform renders instead in the player's selected task font,
  resolved through the task-font resolution chokepoint

### Requirement: A dev harness renders the glyphs in-game

The mod SHALL provide a developer-only way to view the rendered cuneiform glyphs in a running game
(a demo string rendered through the custom widget), so the script's legibility and spacing can be
judged and tuned. The harness SHALL NOT be part of any shipped player-facing feature in this change.

#### Scenario: The harness displays demo text

- **WHEN** the dev harness is opened in a running game
- **THEN** it renders a demo string through the cuneiform render widget
- **AND** it is not reachable through any normal player-facing item or block in this change
