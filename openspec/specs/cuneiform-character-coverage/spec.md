# cuneiform-character-coverage Specification

## Purpose
TBD - created by archiving change add-cuneiform-character-coverage. Update Purpose after archive.
## Requirements
### Requirement: A data-driven alias map substitutes characters before glyph lookup

The cuneiform line layout SHALL apply a many-to-one character alias map at the same pre-lookup layer
as its existing uppercase-folding step — after folding input to uppercase and before looking a glyph
up in the bundle. An aliased character MUST resolve to, and render identically to, the authored
glyph it maps to (same strokes, same advance, same total width). The alias map MUST be expressed as
data (a lookup keyed by character) so entries can be added or removed without changing layout logic,
and it MUST remain in `Scribe.Core` with no Vintage Story API reference. A character that is neither
authored nor aliased SHALL continue to be handled by the existing safe missing-glyph path (a small
gap, no strokes, never a throw).

#### Scenario: An aliased character renders identically to its target glyph

- **WHEN** a string containing an aliased character (for example `[`) is laid out
- **THEN** the resulting positioned strokes and total advance width are identical to laying out the
  alias target character (`(`) at the same position

#### Scenario: Bracket and brace aliases reuse the authored parenthesis glyphs

- **WHEN** the characters `[` and `{` are laid out
- **THEN** each renders using the authored `(` glyph
- **AND** the characters `]` and `}` each render using the authored `)` glyph

#### Scenario: Aliasing is applied after uppercase folding

- **WHEN** an aliased character is looked up
- **THEN** the alias substitution happens on the already-uppercase-folded character, at the same
  pre-lookup layer as folding, not in any caller

#### Scenario: A character that is neither authored nor aliased still degrades safely

- **WHEN** a string contains a character with no authored glyph and no alias entry
- **THEN** layout completes without throwing, advancing the small missing-glyph gap and emitting no
  strokes for that character

### Requirement: Alias entries that depend on unauthored art are ordered after that art lands

The alias map SHALL distinguish aliases that target an already-authored glyph (which ship
immediately) from aliases whose target glyph is not yet authored (which MUST NOT be added until the
target glyph exists in the shipped bundle). Specifically, the `[` `{` → `(` and `]` `}` → `)` aliases
SHALL ship in the first, immediately-shippable step; the `&` → `+` alias SHALL be added only after a
`+` glyph is authored and present in the regenerated bundle, so `&` never aliases to a glyph the
bundle lacks.

#### Scenario: Immediately-shippable aliases target authored glyphs

- **WHEN** the bracket and brace aliases are added
- **THEN** each alias target (`(`, `)`) is already present in the shipped bundle, so the alias
  renders real ink from the moment it ships

#### Scenario: The ampersand alias waits for the plus glyph

- **WHEN** the `&` → `+` alias is being considered for addition
- **THEN** it is added only if a `+` glyph is authored and present in the shipped bundle
- **AND** until then, `&` continues to fall through to the safe missing-glyph gap rather than
  aliasing to an absent target

### Requirement: A recommended new-glyph wishlist is published for glyph-forge authoring

The change SHALL publish an explicit, enumerated list of additional characters recommended for
authoring in the `glyph-forge` sister project, each with a short rationale, framed as a
RECOMMENDED set for the author to approve or prune rather than a locked commitment. The wishlist MUST
include `+` (so `&` can subsequently alias to it). Authoring the glyph art itself is out of scope for
this change; the wishlist is a deliverable output, not implemented geometry.

#### Scenario: The wishlist enumerates candidates with rationale and includes plus

- **WHEN** the change's artifacts are reviewed
- **THEN** they contain an enumerated new-glyph wishlist, each entry with a short rationale
- **AND** `+` is on the wishlist, called out as the prerequisite for the `&` → `+` alias

#### Scenario: The wishlist is advisory, not a commitment

- **WHEN** the author reviews the wishlist
- **THEN** any entry may be approved or pruned without invalidating the change
- **AND** the immediately-shippable alias step does not depend on any wishlist glyph being authored

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

