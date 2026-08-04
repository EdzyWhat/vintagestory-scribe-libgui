## Why

The cuneiform "handwriting feel" currently randomizes only stroke endpoints (per-vertex
jitter). In playtesting the jitter reads a touch too loose, and every glyph still sits
perfectly upright — real impressed/carved text tilts slightly from character to character.
Toning the jitter down and adding a small whole-character tilt (the effect already prototyped
in the `glyph-forge` tool) makes the text read as hand-pressed rather than machine-set.

## What Changes

- Reduce the per-vertex jitter **distance** by 25% (endpoint displacement coefficient
  `GlyphStrokeJitter.MaxPositionFraction` from `0.05` to `0.0375`). Weight variation and the
  `0.6` strength dial are unchanged; this is a re-tune of a bounded constant, not a spec change.
- Add a new visual-only **whole-character rotation**: each rendered glyph is rotated rigidly by
  a small angle in `[-8°, +8°]`, chosen deterministically per character instance so every stroke
  of a glyph shares the same angle (the character tilts as a unit, it does not shear).
- The rotation is a pure, VS-API-free Core transform (mirroring `GlyphStrokeJitter`),
  deterministically seeded from stable stroke identity so it is stable per frame/re-open and
  differs between repeated characters, and applied at paint time **after** jitter.
- Rotation, like jitter, is strictly visual: it never affects layout metrics, the caret,
  selection, or hit-testing.

## Capabilities

### New Capabilities
- `cuneiform-character-rotation`: a deterministic, visual-only rigid rotation of each rendered
  cuneiform glyph about its own box center, bounded to a small max angle, composed after jitter
  and never affecting layout, caret, or hit-testing.

### Modified Capabilities
<!-- None. The jitter-distance reduction only re-tunes the bounded MaxPositionFraction constant;
     the cuneiform-handwriting-jitter requirement ("magnitude SHALL be bounded and expressed
     relative to grid size") is unchanged, so it is an implementation tweak, not a spec change. -->

## Impact

- **Core** (`src/Core/Cuneiform/`): `GlyphStrokeJitter.MaxPositionFraction` retuned; new pure
  rotation transform (e.g. `GlyphStrokeRotation`) with its own seeding, plus Core unit tests.
- **Mod** (`src/Mod/`): the two paint-time call sites that apply jitter today
  (`CuneiformText.cs`, `ScribeCuneiformField.cs`, and the title path in
  `ScribeCuneiformTitleField.cs`) also apply the rotation, and thread a max-rotation-degrees
  value from a `CuneiformMetrics` default alongside the existing jitter/glow parameters.
- No new package/mod dependency; no persistence, network, or layout changes.
