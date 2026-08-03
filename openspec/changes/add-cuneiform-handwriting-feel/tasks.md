## 1. Thread stroke identity through layout (Core)

- [ ] 1.1 Add stable identity fields to `PositionedStroke` (`src/Core/Cuneiform/CuneiformLineLayout.cs`): the
  source character index within the line and the glyph-local stroke ordinal. Keep the struct game-agnostic.
- [ ] 1.2 Populate them in `CuneiformLineLayout.LayoutSegment` at emit time (line ~362), deriving the source
  char index from the pen walk / `CharBoundaries` and the ordinal from the glyph's stroke position.
- [ ] 1.3 Assert the addition changes nothing else: emitted stroke geometry, construction order, `TotalWidth`,
  and `CharBoundaries` are unchanged. Add/extend a Core test to lock this.

## 2. Deterministic jitter transform (Core)

- [ ] 2.1 Add a pure Core type (e.g. `GlyphStrokeJitter`) with `Jitter(GlyphStroke stroke, int seed, double
  strength) -> GlyphStroke` using `new Random(seed)`, mirroring `ScribeTextCorruptor`. Perturb both endpoints
  (position → angle/length) and `Weight`; bound displacement as a fraction of grid size.
- [ ] 2.2 Strength 0 returns the input unchanged (identity). Keep it allocation-light (struct in/out).
- [ ] 2.3 Core tests: pure/reproducible (same inputs → identical output), strength 0 == identity, different
  seeds diverge, and perturbation stays within the bounded range.

## 3. Apply jitter at paint time (Mod) — visual only

- [ ] 3.1 In `CuneiformTextRender.PaintInternal` (`src/Mod/CuneiformText.cs:114-159`), apply the jitter
  transform to each `PositionedStroke.Stroke` immediately before `Corners()`, seeding from the base seed +
  stroke identity. Layout metrics untouched.
- [ ] 3.2 Same in `ScribeCuneiformFieldRender.PaintInternal` (`src/Mod/ScribeCuneiformField.cs:141-213`), for
  every wrapped line. The caret bar (205-212), selection box (152-173), and hit-testing continue to read the
  un-jittered layout.
- [ ] 3.3 Establish the per-field/document base seed in the Mod layer (ScribeTextCorruptor precedent — Core
  never picks a seed). Same field/text → same seed → stable handwriting each frame/open.
- [ ] 3.4 Audit: confirm no jittered stroke ever feeds `TotalWidth`/`CharBoundaries`/wrapping/caret. Add a
  test (or assertion) that layout metrics are identical with jitter on vs off.

## 4. Per-letter stroke progression (Mod)

- [ ] 4.1 Generalize the display-only reveal (`CuneiformTextRender.RevealFraction`,
  `src/Mod/CuneiformText.cs:85-89`) to a stroke-count / per-letter-progress model driven off the source
  character index (task 1.1), so within-letter strokes reveal fast and letters are gapped.
- [ ] 4.2 Add reveal state (a revealed stroke count + a driver) to the editable `ScribeCuneiformFieldRender`,
  which has none today — model the driver on the existing caret-blink ticker in `ScribeMultilineFieldState`
  or a new `AnimationController`.
- [ ] 4.3 Trigger at the commit seam: hook `ScribeMultilineFieldState.Commit()`
  (`src/Mod/ScribeMultilineField.cs:861`) / the render `Text` setter (`ScribeCuneiformField.cs:75`). Diff old
  vs new text, animate ONLY the newly-added run (advance revealed count from prior total to new total).
- [ ] 4.4 Deletions/mid-line edits snap to the new total (no reverse animation); already-revealed letters do
  not replay on later keystrokes.
- [ ] 4.5 Mirror the trigger for the single-line title field via its `TextEditingController` seam
  (`ScribeCuneiformTitleField.cs:84`).

## 5. Optional ghost lead-in (Mod)

- [ ] 5.1 Behind the same setting, render the not-yet-revealed strokes of the currently-animating letter as a
  faint outline (low-alpha fill or `SKPaintStyle.Stroke`) ahead of the filled pressings. Ship the plain
  progressive fill first; add the ghost only if it reads well in-game. Final appearance must match the
  no-ghost result once the letter finishes.

## 6. Client config (Mod)

- [ ] 6.1 Add to `ScribeClientConfig`: jitter strength (0 = off; default a low tasteful value) and a
  stroke-progression enable + reveal speed (reuse/extend the `RevealDurationMs` notion). Read at (re)build
  time like other settings. Document that this is client-side only (no persistence, no sync).
- [ ] 6.2 Confirm jitter strength 0 + progression off reproduces today's crisp/instant rendering exactly.
- [ ] 6.3 Surface the toggles where the other client display settings live (Scribe Settings), if that fits the
  existing settings UI; otherwise config-file only for this round (note which).

## 7. Verification

- [ ] 7.1 `dotnet build` clean; `dotnet test` — Core coverage for jitter (task 2.3) and stroke identity
  (task 1.3) and the layout-metrics-invariant-under-jitter check (task 3.4).
- [ ] 7.2 In-game: type a line with repeated characters; confirm repeated glyphs look different (jitter) and
  the line does NOT shimmer frame to frame.
- [ ] 7.3 In-game: confirm the caret sits correctly and clicking selects the right character with jitter on
  (jitter must not move the caret or shift hit-testing).
- [ ] 7.4 In-game: type into a tablet/lectern cuneiform field; confirm new letters press in stroke-by-stroke
  with a pause between letters, and earlier letters don't re-animate on each keystroke.
- [ ] 7.5 In-game: delete/edit mid-line; confirm text snaps to the new state with no reverse animation.
- [ ] 7.6 In-game: set jitter 0 + progression off; confirm rendering is identical to current crisp behaviour.
- [ ] 7.7 In-game (if built): evaluate the ghost lead-in; keep only if it reads well, else leave it off by
  default.
