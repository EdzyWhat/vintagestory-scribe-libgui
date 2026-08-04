# Design

## Context

Cuneiform "handwriting feel" today is one pure Core transform, `GlyphStrokeJitter.Jitter`,
applied at paint time to each `PositionedStroke` immediately before `GlyphStroke.Corners()`,
seeded off the stroke's stable identity (`SeedFor(baseSeed, SourceCharIndex, StrokeOrdinal)`).
It perturbs the two endpoints and the weight; it is visual-only and never touches layout, caret,
or hit-testing. The three paint sites are `CuneiformText.cs` (display widget),
`ScribeCuneiformField.cs` (editable multiline), and `ScribeCuneiformTitleField.cs` (single-line
title).

`glyph-forge/compositor.js` already prototypes the target rotation: per character instance it
picks one angle in `[-maxDegrees, +maxDegrees]` and rotates every stroke of that glyph about the
glyph's box center, applied **after** per-vertex jitter ("jitter → rotate → shift"). We mirror
that here in Core.

## Goals / Non-Goals

**Goals**
- Reduce per-vertex jitter distance by 25% via the single bounded constant, nothing else.
- Add a rigid, deterministic, visual-only per-character rotation bounded to `±8°` by default.
- Keep the Core discipline: pure, VS-API-free, unit-tested transform; Mod owns the base seed.

**Non-Goals**
- No runtime/dev-command tuning of rotation (matches jitter — it is a baked default).
- No config/settings UI, no persistence, no network, no layout change.
- No change to the reveal/progression animation or the outer glow.

## Decision 1 — Jitter distance is a constant re-tune, not a spec change

Change `GlyphStrokeJitter.MaxPositionFraction` from `0.05` to `0.0375` (a 25% reduction). Leave
`MaxWeightFraction` (`0.25`) and the `DefaultJitterStrength` dial (`0.6`) untouched — the request
is specifically about *distance*. The existing jitter requirement only says magnitude "SHALL be
bounded and expressed relative to grid size," which still holds, so no delta spec is needed; the
locked Core test asserting "endpoints stay within the strength-bounded range" still passes because
the bound simply tightened. Update the constant's doc comment tuning-history note.

## Decision 2 — Rotation is a new pure Core transform, `GlyphStrokeRotation`

Add `src/Core/Cuneiform/GlyphStrokeRotation.cs` mirroring `GlyphStrokeJitter`:

```
public static class GlyphStrokeRotation
{
    public const double DefaultMaxDegrees = 8.0;   // referenced by the Mod default

    // One angle per character instance; seed must NOT include the stroke ordinal.
    public static int SeedFor(int baseSeed, int sourceCharIndex);

    // Rotate a single stroke's endpoints about `pivot` by the seed-chosen angle.
    public static GlyphStroke Rotate(GlyphStroke stroke, Vec2 pivot, int seed, double maxDegrees);
}
```

- `Rotate` picks `angleDeg = (rand.NextDouble()*2 - 1) * maxDegrees` (uniform in `[-max,+max]`),
  converts to radians, and applies the standard 2D rotation matrix to `Start` and `End` about
  `pivot`. `Weight` is unchanged (rigid rotation). `maxDegrees <= 0` returns the stroke unchanged.
- Seeding deliberately **omits** `StrokeOrdinal` so every stroke of one character draws the same
  `rand` sequence → the same angle → the glyph tilts as a rigid unit. Reuse the same `Mix`
  ("lowbias32") finalizer as jitter (extract to a shared internal helper or duplicate the tiny
  method — favor extraction for one source of truth).

**Why a separate seed stream from jitter:** jitter keys on (char, ordinal); rotation keys on
(char) only. Sharing a base seed but different mixing keeps them independent and each stable.

## Decision 3 — The pivot is the glyph box center in line-local space

`PositionedStroke` coordinates are already shifted into line-local space (pen walk applied).
The rotation pivot must be the character's box center in that same space:

- **Pivot X** = midpoint of the character's advance box, from `CuneiformLine.CharBoundaries`:
  `(CharBoundaries[i] + CharBoundaries[i+1]) / 2` for `i = ps.SourceCharIndex`.
- **Pivot Y** = `ps.GridSize / 2` (grid vertical center, matching glyph-forge's `gridSize/2`).

This is all reachable at paint time from the `CuneiformLine` + the stroke's `SourceCharIndex`
with **no layout change** — `CharBoundaries` already exists and is the un-jittered source of truth
the caret/hit-testing use, so the pivot cannot drift from layout. (Alternative considered: adding
a per-stroke pivot field to `PositionedStroke` at layout time — rejected as unnecessary struct
growth when the boundaries already carry the information.)

## Decision 4 — Compose after jitter at each paint site

At each of the three paint sites, wrap the existing jitter call:

```
GlyphStroke drawStroke = ps.Stroke;
if (jitterStrength > 0f)
    drawStroke = GlyphStrokeJitter.Jitter(drawStroke, jitterSeedFor(ps), jitterStrength, ps.GridSize);
if (rotationMaxDegrees > 0f)
    drawStroke = GlyphStrokeRotation.Rotate(
        drawStroke, PivotFor(line, ps), GlyphStrokeRotation.SeedFor(rotationSeed, ps.SourceCharIndex),
        rotationMaxDegrees);
Vec2[] corners = drawStroke.Corners();
```

Order is jitter → rotate, matching glyph-forge. The rotation base seed can reuse the same
per-field/document base seed already threaded for jitter (`jitterSeed`) — the different `SeedFor`
mixing keeps the streams independent, so no new seed plumbing to the call sites is required beyond
the max-degrees value.

## Decision 5 — Thread `rotationMaxDegrees` alongside `jitterStrength`

Add a `CuneiformMetrics.DefaultRotationDegrees = 8.0f` (mirrors `DefaultJitterStrength`). Thread a
`RotationDegrees` / `CuneiformRotation` property through the same widgets and render objects that
already carry `JitterStrength` / `CuneiformJitter` (`CuneiformText.cs`, `ScribeCuneiformField.cs`,
`ScribeCuneiformTitleField.cs`, and the row/field style structs), defaulting callers to the new
constant exactly where they default jitter to `DefaultJitterStrength` today
(`GuiDialogScribeTablet.cs`, `ScribeEditorContent.cs`). Clamp to `>= 0`.

## Risks / Trade-offs

- **Clipping at glyph box edges.** A rotated glyph can push strokes slightly past its advance box.
  glyph-forge added a `RANDOMIZATION_SAFETY_MARGIN_UNITS = 15` padding for this. At ±8° the
  displacement is small and our widgets are not tightly scissored to per-glyph boxes, so we ship
  without extra padding first and only revisit if in-game testing shows clipping at line edges.
- **Combined jitter+rotation legibility.** Both effects stack; ±8° plus the (now smaller) jitter
  should still read as legible cuneiform. Tunable via the two constants if a playtest disagrees.
- **Degenerate strokes.** `Corners()` already guards zero-length strokes; rotation of a point about
  a pivot is still a point, so no new NaN path.

## Migration

None. Visual-only, no stored data or format touched. Existing saves render identically except for
the new tilt and slightly tighter jitter.

## Open Questions

- Final max angle: `8°` is the requested target; confirm in-game it doesn't over-tilt tall glyphs.
- Whether rotation should scale down for very small em sizes (HUD/inline) — deferred; ship uniform.
