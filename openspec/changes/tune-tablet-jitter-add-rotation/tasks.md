## 1. Reduce jitter distance (Core)

- [x] 1.1 In `src/Core/Cuneiform/GlyphStrokeJitter.cs`, change `MaxPositionFraction` from `0.05` to
  `0.0375` (25% less endpoint displacement). Leave `MaxWeightFraction` and the strength dial alone.
- [x] 1.2 Update the `MaxPositionFraction` doc comment to note the new value and the 25% reduction in
  the tuning history.
- [x] 1.3 Confirm the existing jitter Core tests still pass (the "endpoints stay within the
  strength-bounded range" assertion tightens automatically; nothing should need editing). Run
  `dotnet test`.

## 2. Deterministic rotation transform (Core)

- [x] 2.1 Add `src/Core/Cuneiform/GlyphStrokeRotation.cs`: a pure, VS-API-free static class with
  `DefaultMaxDegrees = 8.0`, `SeedFor(int baseSeed, int sourceCharIndex)` (no stroke ordinal — one
  angle per character), and `Rotate(GlyphStroke stroke, Vec2 pivot, int seed, double maxDegrees)`.
- [x] 2.2 `Rotate` picks `angleDeg = (rand.NextDouble()*2 - 1) * maxDegrees`, converts to radians, and
  applies the 2D rotation matrix to `Start` and `End` about `pivot`; `Weight` unchanged. `maxDegrees
  <= 0` returns the stroke unchanged (identity).
- [x] 2.3 Share the `Mix` ("lowbias32") finalizer with `GlyphStrokeJitter` (extract to one internal
  helper) so both seed streams avalanche identically but stay independent (rotation omits ordinal).
- [x] 2.4 Core tests (`tests/Core.Tests`): pure/reproducible (same inputs → byte-identical output);
  max-angle 0 == identity; all strokes of one character rotate by the same angle about the same pivot
  (rigid); the chosen angle stays within `[-max, +max]` across many seeds; repeated char indices
  diverge.

## 3. Apply rotation at paint time (Mod) — visual only, after jitter

- [x] 3.1 In `CuneiformText.cs` `BuildStrokePath` (and the crisp/glow passes that reuse it), after the
  existing jitter call, apply `GlyphStrokeRotation.Rotate` using the glyph box-center pivot before
  `Corners()`. Order: jitter → rotate.
- [x] 3.2 Compute the pivot from the un-jittered layout: X = `(line.CharBoundaries[i] +
  line.CharBoundaries[i+1]) / 2` for `i = ps.SourceCharIndex`, Y = `ps.GridSize / 2`. Guard the
  boundary index range. Reuse the existing per-field base seed via `GlyphStrokeRotation.SeedFor`.
- [x] 3.3 Mirror in `ScribeCuneiformField.cs` `PaintInternal` for every wrapped line, and in
  `ScribeCuneiformTitleField.cs`. The caret bar, selection box, and hit-testing continue to read the
  un-jittered, un-rotated layout.
- [x] 3.4 Audit: confirm no rotated stroke ever feeds `TotalWidth`/`CharBoundaries`/wrapping/caret —
  rotation is applied only to the returned copy at paint time, exactly like jitter.

## 4. Thread the rotation parameter (Mod)

- [x] 4.1 Add `CuneiformMetrics.DefaultRotationDegrees = 8.0f` next to `DefaultJitterStrength`.
- [x] 4.2 Add a `RotationDegrees` / `CuneiformRotation` property (clamped `>= 0`) to the same widgets,
  render objects, and row/field style structs that carry `JitterStrength` / `CuneiformJitter`.
- [x] 4.3 Default the callers to the new constant wherever they default jitter today
  (`GuiDialogScribeTablet.cs` body rows + title band, `ScribeEditorContent.cs`).

## 5. Verify in-game

- [x] 5.1 `dotnet test` green; build and stage the mod.
- [ ] 5.2 Open a tablet/editor in-game: confirm glyphs tilt slightly (≤8°) and differently per
  instance, the jitter reads tighter, text is stable frame-to-frame (no wobble/shimmer), and the
  caret/selection still land correctly. Check line edges for clipping (see design Risks) and note any
  over-tilt of tall glyphs for the open questions.
