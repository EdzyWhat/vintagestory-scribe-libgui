## Context

Cuneiform text renders as filled Skia quads from a fixed stroke model:

- **Core (pure BCL, deterministic):** `GlyphStroke` (`src/Core/Cuneiform/GlyphStroke.cs`) is a 2-point
  centerline (`Start`, `End`) + `Weight`; `Corners()` (lines 43-57) derives an oriented rectangle. The whole
  layout path — `Glyph`, `GlyphBundle`, `CuneiformLineLayout.LayoutSegment` (line 309), emitting
  `PositionedStroke`s in authored construction order (line 362) — is a pure function of (text, bundle). No
  `Random`, no hashing. `PositionedStroke` (`CuneiformLineLayout.cs:9-22`) carries only `Stroke` + `GridSize`
  today.
- **Mod draw path (Skia fill):** two render objects call `Corners()` → `SKPath` → `Canvas.DrawPath(Fill)`:
  the display-only `CuneiformTextRender.PaintInternal` (`src/Mod/CuneiformText.cs:114-159`) and the editable
  `ScribeCuneiformFieldRender.PaintInternal` (`src/Mod/ScribeCuneiformField.cs:141-213`). The Mod layer maps
  grid units → pixels at paint time (`scale = renderedHeight / gridHeight`).
- **Existing reveal (partial, display-only):** `CuneiformTextRender.RevealFraction`
  (`src/Mod/CuneiformText.cs:85-89`) paints only the first `round(fraction * total)` strokes — the "first N
  strokes = partial reveal" contract in the `GlyphStroke` doc-comment. It is driven by an `AnimationController`
  in `CuneiformTextState` (lines 242-289), but `AnimateReveal` defaults false and is used ONLY by the dev
  harness (`GuiDialogCuneiformHarness.cs:73`). The reveal is a fraction of the WHOLE line, not per-letter, and
  advances monotonically once on mount. The editable `ScribeCuneiformFieldRender` has NO reveal at all — it
  always paints every stroke.
- **Determinism precedent:** `src/Core/ScribeTextCorruptor.cs` — `Corrupt(text, strength, seed)` uses
  `new Random(seed)` so a given (text, strength, seed) is reproducible; its doc states the invariant that
  `src/Core` must be deterministic/unit-testable without a game install and the Mod layer owns picking seeds.
- **Typing seam:** in the editable field, `ScribeMultilineFieldState.Commit()`
  (`src/Mod/ScribeMultilineField.cs:861`) fires on every character insert (`OnKeyChar`→`Insert`→`Commit`) and
  the render's `Text` setter (`ScribeCuneiformField.cs:75`) relayouts. The single-line title field commits via
  a `TextEditingController` (`ScribeCuneiformTitleField.cs:84`). The editable path's only ticker today is the
  caret-blink `Ticker` in `ScribeMultilineFieldState`.

## Goals / Non-Goals

**Goals:**
- Make repeated glyphs look hand-pressed via a deterministic, visual-only per-stroke jitter (position, angle,
  width) within the existing 2-point + weight model, seeded for stability (no per-frame shimmer).
- Guarantee jitter NEVER perturbs layout metrics, caret, selection, or hit-testing — only the drawn geometry.
- Reveal newly-typed text per-letter (strokes within a letter in quick succession, a pause between letters),
  animating only the newly-added run, in the editable field.
- Keep all randomness in `src/Core` as pure functions; the Mod layer supplies seeds (ScribeTextCorruptor
  model). Strength 0 / instant reveal reproduces today's exact output.
- Make both effects toggleable/tunable via client config.

**Non-Goals:**
- Migrating strokes to 4 independent free corners / tapered, non-rectangular strokes (deferred — a later
  change if 2-point jitter proves too tame). This also spares the glyph-forge export format + authored glyphs.
- Persisting a per-glyph handwriting identity across sessions (the seed is stable within a render, not saved).
- Animating/jittering the resting display-only labels beyond making them share the same Core transform.
- Any VS-API usage in Core, any new mod dependency, any change to layout/caret/wrap behaviour.

## Decisions

### 1. Jitter is a pure Core transform over the 2-point + weight model
Add a Core type (e.g. `GlyphStrokeJitter`) with a pure method that maps `(GlyphStroke stroke, int seed, double
strength) → GlyphStroke`, using `new Random(seed)` exactly like `ScribeTextCorruptor`. Within the current
model it can perturb: each endpoint's position (small dx/dy), which also changes the stroke's angle and
length, and the `Weight` (width). It returns a new `GlyphStroke`; `Corners()` is unchanged and still derives
the rectangle. `strength = 0` returns the stroke unchanged (identity), so the current crisp look is preserved.
Bounds are expressed as a fraction of `GridSize` so jitter scales with glyph size. Pure and unit-testable:
same inputs → same output; strength 0 → identity; bounded displacement.

*Alternative — jitter the 4 corners independently (non-rectangular/tapered).* Deferred (Non-Goal): it forces
a storage-model change (`PositionedStroke`/`GlyphStroke` → 4 corners), a glyph-forge export-format change, and
re-touching every authored glyph, for a look we may not need. Revisit if 2-point jitter reads too uniform.

### 2. Seed is stable per (document, character, stroke) — not per frame
The jitter must be identical every frame or the text shimmers. Seed = a hash of a per-field/document base seed
combined with the stroke's stable identity (source character index in the line + glyph-local stroke ordinal).
The Mod layer owns the base seed (e.g. derived from the field/document id), mirroring how the corruptor's
caller picks the seed. Because layout is otherwise pure, the same text renders the same handwriting each frame
and each session-open (unless the base seed deliberately varies).

### 3. Thread stroke identity through `PositionedStroke`
`PositionedStroke` currently carries only `Stroke` + `GridSize`. Add stable identity: the source character
index (derivable from `CuneiformLine.CharBoundaries`/`SourceStart`) and the glyph-local stroke ordinal.
`CuneiformLineLayout.LayoutSegment` populates these at emit time. This single addition serves BOTH the jitter
seed (Decision 2) and the per-letter reveal (Decision 5) — both need "which letter / which stroke" identity
that the flat construction-order index alone doesn't give cleanly across wrapping.

### 4. Jitter applied at paint, layout stays un-jittered
Both render objects apply the jitter transform to each `PositionedStroke.Stroke` immediately before
`Corners()` in the paint loop (`CuneiformText.cs:123-141`, `ScribeCuneiformField.cs:187-197`). Everything that
must stay stable — `TotalWidth`, `CharBoundaries`, wrapping, the caret bar (`ScribeCuneiformField.cs:205-212`),
the selection box (152-173), and click hit-testing (`CuneiformLine.NearestBoundary`) — continues to read the
un-jittered layout. This cleanly satisfies "jitter never moves the caret or changes where a click lands."

### 5. Per-letter reveal generalizes the existing fraction model
Generalize the display-only `RevealFraction` into a reveal keyed to a stroke count / letter progress, and add
the same reveal state to the editable `ScribeCuneiformFieldRender` (which has none today). Progression schedule:
strokes within a letter advance fast (short per-stroke interval); a longer gap between letters (keyed off the
source-character-index change from Decision 3). The reveal covers only newly-added strokes — on commit, the
already-revealed count is preserved and only the new run animates in.

### 6. Trigger reveal at the commit seam; animate only the delta
Hook `ScribeMultilineFieldState.Commit()` / the render `Text` setter: on text change, diff against the prior
text to find the newly-added stroke run, set the reveal target to the new total, and let a driver advance the
revealed count from the prior total to the new one on the per-letter schedule. Deletions/edits snap to the new
total (no reverse animation). The title field mirrors this via its controller-changed seam. The driver is an
`AnimationController` or a dedicated ticker (the editable path already runs a caret-blink ticker to model on).

### 7. Optional ghost lead-in
Explore rendering the not-yet-pressed strokes of the currently-animating letter as a faint outline (stroke
paint at low alpha, or `SKPaintStyle.Stroke` instead of `Fill`) that the filled pressings catch up to. Gated
behind the same config and behind a "does it actually look good" in-game check — ship the plain progressive
fill first, add the ghost only if it reads well. Kept a sub-decision, not a hard requirement.

### 8. Client-config toggles
Add to `ScribeClientConfig`: a jitter strength (0 = off, default a tasteful low value) and a
stroke-progression enable (default on) with the reveal speed reusing/So extending the existing
`RevealDurationMs` notion. Both read at (re)build time like other client settings. Off + strength 0 == today.

## Risks / Trade-offs

- **Shimmer if the seed isn't stable:** the single biggest failure mode. Mitigate with Decision 2 (seed from
  stable stroke identity, never a frame/time value) and a Core test asserting the transform is a pure function
  of its inputs.
- **Jitter leaking into layout:** if jitter is ever applied before metrics are computed, the caret/wrap drift.
  Mitigate with Decision 4 (paint-time only) and an audit that no jittered stroke feeds `TotalWidth`/
  `CharBoundaries`/hit-testing; a test that layout metrics are byte-identical with jitter on vs off.
- **Per-letter reveal complexity / re-animation bugs:** naively resetting reveal on each keystroke would
  re-animate the whole line. Mitigate with Decision 6 (animate only the delta; preserve prior revealed count)
  and a check that mid-line edits don't replay earlier letters.
- **Two render objects diverging:** jitter + reveal must behave the same in the display-only and editable
  renderers. Mitigate by routing both through the same Core transform + a shared reveal helper rather than
  duplicating logic.
- **Performance:** jitter is a handful of float ops per stroke per frame; strokes per visible line are modest.
  Acceptable, but avoid per-frame allocation (reuse buffers / structs) in the paint loop.
- **Determinism vs. "every open looks different":** a stable seed means the same note looks identical each
  open. That's the safe default (no shimmer, testable). If per-open variety is wanted later, vary the base
  seed by session — a Mod-layer choice that leaves Core untouched.
