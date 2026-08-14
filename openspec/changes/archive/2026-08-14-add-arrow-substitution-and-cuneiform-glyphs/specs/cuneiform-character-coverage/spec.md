## ADDED Requirements

### Requirement: Arrows and angle brackets are authored cuneiform glyphs, not aliases

The cuneiform font SHALL gain four REAL authored glyphs for `←` (U+2190), `→` (U+2192), `<` (U+003C), and `>` (U+003E), so tablet text can render them as strokes. These characters MUST NOT be added to the alias map (`CuneiformLineLayout.Aliases`): no existing authored glyph resembles an arrow or an angle bracket, so there is no valid alias target, and an alias must always point at real authored ink. Until the authored stroke art exists in the shipped bundle, these characters SHALL continue to fall through the existing safe missing-glyph path (a small gap, no strokes, never a throw) rather than aliasing to an absent or wrong target. The `←` / `→` glyphs are the tablet-side companion to the typed-arrow substitution (a substituted arrow stored in the document renders as cuneiform ink on a tablet); `<` / `>` render whenever typed literally and are independent of substitution.

#### Scenario: The four characters are authored, not aliased

- **WHEN** the alias map is inspected after this change
- **THEN** `←`, `→`, `<`, and `>` have NO alias entry
- **AND** each resolves to its own authored glyph in the regenerated bundle rather than to another character's glyph

#### Scenario: A substituted arrow renders as cuneiform on a tablet

- **WHEN** a document containing `→` or `←` (from typed-arrow substitution or otherwise) is displayed on a tablet with the authored glyphs present
- **THEN** the arrow renders as its authored cuneiform strokes

#### Scenario: The characters degrade safely before the art lands

- **WHEN** the authored stroke art for these characters is not yet present in the shipped bundle
- **THEN** laying out text containing them completes without throwing, advancing the small missing-glyph gap and emitting no strokes for those characters

## MODIFIED Requirements

### Requirement: New authored glyphs regenerate the bundle and update the count assertion

When approved glyphs are authored in `glyph-forge`, the combined bundle SHALL be regenerated
via the `glyph-forge` bundler and re-committed to the mod's scanned asset path, and the Core test
that asserts the shipped bundle's authored-character count SHALL be updated from its current shipped
value (54) to the new total (58) once the four new glyphs (`←`, `→`, `<`, `>`) are present. The
regeneration process MUST introduce no new build coupling — the bundle remains a committed artifact
produced out-of-band — and the render smoke-test SHALL be performed via the `.cuneiform <text>`
client harness.

#### Scenario: The bundle is regenerated and re-committed after new art

- **WHEN** one or more approved glyphs are authored in `glyph-forge`
- **THEN** the bundle is regenerated with the `glyph-forge` bundler and the updated
  `cuneiform-glyphs-1.json` is re-committed under the mod's `textures/` asset tree

#### Scenario: The character-count assertion tracks the new total

- **WHEN** the four new glyphs (`←`, `→`, `<`, `>`) land in the shipped bundle
- **THEN** the Core test asserting the authored-character count is updated from 54 to 58 so the
  shipped-bundle assertion continues to pass

#### Scenario: The new glyphs are smoke-tested via the cuneiform harness

- **WHEN** the regenerated bundle is loaded in-game
- **THEN** rendering `←`, `→`, `<`, and `>` via `.cuneiform <text>` shows their authored strokes
