## Why

The cuneiform text renderer draws every glyph from a fixed, mathematically-perfect set of stroke rectangles
(`GlyphStroke` = a 2-point centerline + weight → `Corners()`), and it draws them all at once. The result
reads as a printed font, not something a person pressed into clay: identical glyphs are pixel-identical, and
a freshly-typed line simply pops into existence fully formed.

Two cheap touches make the same geometry feel hand-made:

- **Hand-written randomness (jitter).** Perturb each stroke slightly — position, angle, and width — so
  repeated glyphs differ and the text looks pressed by hand rather than printed. Glyph Forge demonstrated
  that randomizing the stroke JSON reads convincingly as handwriting.
- **Stroke progression while typing.** Instead of a whole letter appearing at once, lay its strokes down
  quickly one after another, with a slightly longer pause between letters — fast enough to feel natural, but
  giving the writing a satisfying "being pressed in" beat as you type.

Both are polish on the *appearance* of already-correct text; neither changes what the text says, where the
caret goes, or how lines wrap.

## What Changes

- **Deterministic per-stroke jitter (Core).** A new pure, VS-API-free Core transform perturbs a stroke
  within the existing 2-point + weight model — nudging its endpoints (position + angle) and its weight
  (width) — seeded so the result is *stable*: a given (document seed, character position, stroke) always
  jitters the same way, so glyphs don't shimmer between frames and identical characters still differ from
  each other. It follows the `ScribeTextCorruptor` seed pattern (Core is deterministic; the Mod layer picks
  the seed). Jitter strength is configurable, and strength 0 reproduces today's exact geometry.
- **Jitter is visual-only.** It is applied to the drawn stroke geometry, NOT to layout: `TotalWidth`,
  `CharBoundaries`, line wrapping, caret placement, selection, and hit-testing all continue to use the
  un-jittered layout. Jitter never moves the caret or changes where a click lands.
- **Stroke identity threaded through layout.** `PositionedStroke` gains stable identity (its source
  character index within the line and its glyph-local stroke ordinal) so a per-stroke jitter seed — and the
  per-letter reveal below — can key off a stable value instead of a frame counter.
- **Per-letter stroke progression (typing animation).** As text is committed in an editable cuneiform field,
  newly-added strokes reveal progressively — strokes within one letter appear in quick succession, with a
  longer pause between letters — rather than the whole new text popping in. Already-written text is not
  re-animated on each keystroke; only the newly-added run catches up. The existing display-only
  `RevealFraction` reveal is generalized to a per-letter/stroke-count model, and reveal support (absent
  today) is added to the editable field renderer. A ghost-outline lead-in (a faint upcoming letter the
  pressings fill in) is explored as an optional flourish.
- **Both effects are toggleable / tunable** via client config, defaulting on at a tasteful strength, and can
  be turned fully off (jitter 0 + instant reveal) to restore the current crisp behaviour.

## Capabilities

### New Capabilities
- `cuneiform-handwriting-jitter`: deterministic, visual-only per-stroke perturbation (position, angle,
  width) within the 2-point + weight model, seeded for stability, with a configurable strength; must not
  affect layout metrics, caret, selection, or hit-testing.
- `cuneiform-stroke-progression`: a per-letter typing reveal in the editable cuneiform field (strokes within
  a letter appear quickly, with a pause between letters), animating only newly-added text, generalizing the
  existing whole-line reveal fraction and adding reveal state to the editable renderer.

### Modified Capabilities
_(none — both are additive rendering capabilities; the layout contract is unchanged and preserved.)_

## Impact

- **Code (Core, VS-API-free, unit-tested):** a new jitter transform (pure function of stroke + seed +
  strength → perturbed stroke), stroke-identity fields on `PositionedStroke` populated by
  `CuneiformLineLayout`, and (if needed) a small reveal/stroke-count helper — all deterministic and covered
  by `Core.Tests`.
- **Code (Mod):** `CuneiformTextRender` and `ScribeCuneiformFieldRender` call the jitter transform before
  `Corners()` and pass a stable per-field seed; the editable field renderer gains a reveal fraction/stroke
  count plus a driver (an `AnimationController`/ticker) triggered at the commit seam
  (`ScribeMultilineFieldState.Commit()` / the render `Text` setter); the title field mirrors it. Client
  config gains jitter-strength and stroke-progression toggles.
- **Assets:** none — this is geometry/animation over existing glyph data and fonts.
- **Persistence:** none — jitter and reveal are render-time only; nothing is stored on the document or stack.
- **Constraints honoured:** `src/Core` stays VS-API-free and deterministic (no `Math.random` in the layout
  path; seeds come from the Mod layer per the `ScribeTextCorruptor` precedent); no new mod dependency; no
  change to what text says or how it lays out.
- **Non-goals:** migrating strokes to 4 independent corners / tapered non-rectangular strokes (kept the
  2-point + weight model this round — a later change if jitter isn't expressive enough); animating the
  display-only resting labels beyond what already exists; persisting a per-glyph "handwriting" so it's
  identical across sessions (the seed is stable within a render, not saved).
