## 1. Thread stroke identity through layout (Core)

- [x] 1.1 Add stable identity fields to `PositionedStroke` (`src/Core/Cuneiform/CuneiformLineLayout.cs`): the
  source character index within the line and the glyph-local stroke ordinal. Keep the struct game-agnostic.
- [x] 1.2 Populate them in `CuneiformLineLayout.LayoutSegment` at emit time (line ~362), deriving the source
  char index from the pen walk / `CharBoundaries` and the ordinal from the glyph's stroke position.
- [x] 1.3 Assert the addition changes nothing else: emitted stroke geometry, construction order, `TotalWidth`,
  and `CharBoundaries` are unchanged. Add/extend a Core test to lock this.

## 2. Deterministic jitter transform (Core)

- [x] 2.1 Add a pure Core type (e.g. `GlyphStrokeJitter`) with `Jitter(GlyphStroke stroke, int seed, double
  strength) -> GlyphStroke` using `new Random(seed)`, mirroring `ScribeTextCorruptor`. Perturb both endpoints
  (position → angle/length) and `Weight`; bound displacement as a fraction of grid size.
- [x] 2.2 Strength 0 returns the input unchanged (identity). Keep it allocation-light (struct in/out).
- [x] 2.3 Core tests: pure/reproducible (same inputs → identical output), strength 0 == identity, different
  seeds diverge, and perturbation stays within the bounded range.

## 3. Apply jitter at paint time (Mod) — visual only

- [x] 3.1 In `CuneiformTextRender.PaintInternal` (`src/Mod/CuneiformText.cs`), apply the jitter transform to
  each `PositionedStroke.Stroke` immediately before `Corners()`, seeding from the base seed + stroke identity
  (`GlyphStrokeJitter.SeedFor`). Layout metrics untouched.
- [x] 3.2 Same in `ScribeCuneiformFieldRender.PaintInternal` (`src/Mod/ScribeCuneiformField.cs`), for every
  wrapped line. The caret bar, selection box, and hit-testing continue to read the un-jittered layout.
- [x] 3.3 Establish the per-field/document base seed in the Mod layer (ScribeTextCorruptor precedent — Core
  never picks a seed). Editable rows seed off the stable `TaskId`; the title band uses a fixed constant;
  display `CuneiformText` seeds off its own text (`CuneiformMetrics.SeedFromString`). Same field → same seed →
  stable handwriting each frame/open; typing does not reseed prior letters.
- [x] 3.4 Audit: confirm no jittered stroke ever feeds `TotalWidth`/`CharBoundaries`/wrapping/caret. Core test
  `Jitter_DoesNotAlterLayoutMetrics` locks that the source `PositionedStroke` layout reads is never mutated by
  a jitter call; jitter is applied only to a returned copy at paint time. (266 Core tests green.)

## 4. Per-letter stroke progression (Mod)

- [~] 4.1 Generalize the display-only reveal (`CuneiformTextRender.RevealFraction`,
  `src/Mod/CuneiformText.cs:85-89`) to a stroke-count / per-letter-progress model driven off the source
  character index (task 1.1), so within-letter strokes reveal fast and letters are gapped. DEFERRED: the
  display-only render is harness-only (no live typing); the per-letter model was implemented on the editable
  path where it matters. The pure timing math lives in Core `CuneiformReveal` and is reusable if the display
  path ever needs it.
- [x] 4.2 Add reveal state (a revealed stroke count + a driver) to the editable `ScribeCuneiformFieldRender`.
  Uses a new `AnimationController` in `ScribeMultilineFieldState` driving elapsed-ms; the render converts
  elapsed-ms + baseline char count into revealed strokes per `Scribe.Core.Cuneiform.CuneiformReveal`.
- [x] 4.3 Trigger at the commit seam: `ScribeMultilineFieldState.Commit()` calls `UpdateReveal()`, which diffs
  the tracked text vs the new text and animates ONLY a pure-append suffix (baseline = prior length; new run
  presses in on the per-letter schedule).
- [x] 4.4 Deletions/mid-line edits (any non-append change) snap: reveal is deactivated and the controller
  stopped, so text shows fully. Prior letters keep their baseline when a run is extended mid-flight (elapsed
  preserved across the Duration change), so already-revealed letters don't replay.
- [x] 4.5 Mirror the trigger for the single-line title field via its `TextEditingController` seam
  (`ScribeCuneiformTitleField.cs` `UpdateReveal`, called from `OnControllerChanged`).

## 5. Optional ghost lead-in (Mod)

- [x] 5.1 Behind the same setting, render the not-yet-revealed strokes as a faint low-alpha fill ahead of the
  filled pressings. Shipped the plain progressive fill first; this adds the ghost. SCOPE EXPANDED per the
  2026-08-05 playtest: the ghost paints the WHOLE not-yet-pressed tail (all remaining strokes), not just the
  currently-animating letter, so a fast typist sees the full word immediately with the crisp fill catching up.
  Implemented as a `StrokePass.Ghost` pass in `ScribeCuneiformField.cs` at `GhostLeadInOpacity = 0.22` (tuned
  in-game from 0.28), over the exact complement of the crisp reveal set (no double-draw), centralized so it
  covers the multiline editor AND the title field. Final appearance matches the no-ghost result once every
  stroke presses in (the tail empties → ghost draws nothing). Confirmed 2026-08-05 (7.7).

## 6. Client config (Mod)

- [~] 6.1 Add a stroke-progression enable to the client settings. DONE for the enable toggle:
  `ScribePlayerSettings.CuneiformProgression` (default false, client-local, never synced), read at
  (re)build time by the tablet so toggling repaints an open tablet. NOT done: jitter strength and reveal
  speed are still code constants (`DefaultJitterStrength`, `RevealPerStrokeMs`/`RevealPerLetterMs`), tuned
  in-game 2026-08-03 rather than exposed. Note: `ScribeClientConfig` is retired — the real settings home is
  `ScribePlayerSettings` / `scribe-hud-config.json`.
- [~] 6.2 OBSOLETE 2026-08-06: jitter strength 0 + progression off can't be exercised because the
  jitter-strength knob was deliberately designed away (progression is a default-off toggle; jitter is a
  fixed constant), so there is no "jitter-0" state to verify against. Retired, not done — see TESTING.md
  `42a3a1d1`. Superseded by the shipped design (constants, not knobs).
- [x] 6.3 Surfaced the progression toggle in Scribe Settings ("Cuneiform press-in"), paired with the
  existing "Cuneiform tablets" toggle; lang key + help added. Jitter strength/speed remain unsurfaced
  (constants) for this round.

## 7. Verification

- [x] 7.1 `dotnet build` clean; `dotnet test` — Core coverage for jitter (task 2.3) and stroke identity
  (task 1.3) and the layout-metrics-invariant-under-jitter check (task 3.4). DONE: `CuneiformTests.cs`
  covers jitter (`Jitter_StrengthZero_IsIdentity`, `_SameInputs_AreReproducible`, `_Displacement_StaysWithinBounds`,
  `_ScalesWithGridSize`, …), stroke identity (`Layout_PositionedStroke_CarriesSourceCharIndexAndOrdinal`,
  `Layout_StrokeOrdinal_CountsWithinGlyph`), and the layout invariant (`Jitter_DoesNotAlterLayoutMetrics`);
  Core suite green (286/286).
- [x] 7.2 In-game: type a line with repeated characters; confirm repeated glyphs look different (jitter) and
  the line does NOT shimmer frame to frame. — Confirmed 2026-08-03 playtest (`1d8a57eb`).
- [x] 7.3 In-game: confirm the caret sits correctly and clicking selects the right character with jitter on
  (jitter must not move the caret or shift hit-testing). — Confirmed 2026-08-03 playtest (`d76d4f84`).
- [x] 7.4 In-game: type into a tablet/lectern cuneiform field; confirm new letters press in stroke-by-stroke
  with a pause between letters, and earlier letters don't re-animate on each keystroke. — Confirmed
  2026-08-03 playtest (`d077766c`).
- [x] 7.5 In-game: delete/edit mid-line; confirm text snaps to the new state with no reverse animation.
  — Confirmed 2026-08-03 playtest (`8fccf5a0`).
- [~] 7.6 OBSOLETE 2026-08-06: same as 6.2 — no jitter-0 setting exists (jitter is a fixed constant by
  design), so "identical to crisp behaviour" has no reachable state to test. Retired, not done (TESTING.md
  `42a3a1d1`).
- [x] 7.7 In-game: evaluate the ghost lead-in; keep only if it reads well, else leave it off by default.
  — Confirmed 2026-08-05 playtest ("the ghost/preview works!"). Opacity tuned 0.28 → 0.22 to read as a
  subtle lead-in. Kept on whenever stroke-progression is on (no separate setting).
