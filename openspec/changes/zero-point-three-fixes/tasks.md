## 1. Fix the schematic recipe grid fit

- [x] 1.1 Repack `src/Mod/assets/scribe/recipes/grid/scribeclockmakernotebook-schematic.json` from the
  unusable `width: 4, height: 1` (`"BGMS"`) to a 3×2 layout (`width: 3, height: 2`, pattern `"BGM,S__"` —
  Notebook-Gear-MetalParts on the top row mirroring the trait recipe, schematic in the bottom-left cell),
  keeping the same four ingredient definitions, the same output (one Clockmaker's Notebook), and
  `consume: false` on the schematic ingredient. No ingredient or output change.
- [x] 1.2 Give the two recipes DISTINCT `recipegroup` values so the handbook renders them as two separate
  "Created by" grids: `recipegroup: 1` on the trait-gated `scribeclockmakernotebook.json` (otherwise
  unchanged — still `"BGM"`, `width: 3, height: 1`, `requiresTrait: "tinkerer"`) and `recipegroup: 2` on the
  schematic recipe. NOTE (corrected via decompile of `addCreatedByInfo`): the handbook buckets grid recipes
  by `RecipeGroup` and renders one cycling grid per distinct value; recipes that OMIT `recipegroup` all
  default to `0` and collapse into ONE cycling grid — the original "leave them ungrouped" plan was backwards.
  `RecipeGroup` is display-only (not in `GridRecipe.Matches`), so this doesn't affect craftability.

## 2. Add the crouch + right-click quench rehydration path

- [x] 2.1 In `src/Mod/ItemScribeTablet.cs`, add a helper that, given a `BlockSelection`/`IWorldAccessor`,
  returns whether the aimed-at block is a water-filled liquid container — detect via
  `BlockLiquidContainerBase` + `GetContent(pos)` / `WaterTightContainableProps` (water portion) using the
  shared base API rather than per-block casts, so bucket/barrel/tureen work uniformly.
- [x] 2.2 Extend `OnHeldInteractStart` so that when `byEntity.Controls.ShiftKey` is held AND the helper
  reports a water container under `blockSel`, the quench branch runs and takes precedence over the existing
  `GroundStorable` shift-passthrough. Every other crouch-right-click still falls through to the existing
  passthrough unchanged.
- [x] 2.3 In the quench branch, act only when `ReadHard(stack)` is true (wet and fired tablets no-op). On the
  server, call the existing `Soften(stack, world)`, assign the softened stack to the slot, and `slot.MarkDirty()`;
  set `handling = EnumHandHandling.PreventDefault` so the container's own fill/pour interaction does not also fire.
- [x] 2.4 Add client-side feedback in the quench branch: play a water splash/sizzle sound (and optionally a
  small particle burst) so the quench reads as a deliberate action. *(Reuses the container's own
  `WaterTightContainableProps.FillSound` so it matches whatever liquid it holds; played on both sides. No
  particle burst added — the sound alone reads the gesture; can add later if it wants more punch.)*

## 3. Raise the clay-tablet recipe cost from 8 to 12 clay

- [x] 3.1 In `src/Mod/assets/scribe/recipes/grid/scribetablet-clay.json`, repack all three variants
  (`clay-red`/`clay-blue`/`clay-fire`) from `"KCC,SCC"` (3×2, 4 clay cells × 2 = 8 clay) to `"KCC,SCC,_CC"`
  (3×3, 6 clay cells × 2 = 12 clay) — a 2×3 clay block in the right two columns, keeping the knife (`K`) and
  stick (`S`) in the left column and the same single-tablet output. Leave `scribetablet-wax.json` unchanged.

## 4. Fix the read-only tablet's transparent backdrop

- [x] 4a.1 Add `src/Mod/ScribeBackdropPaintReset.cs` — a `ScribeResetPaintColor : SingleChildWidget` whose
  render object extends `RenderProxyBox` and overrides `Paint` to set `context.SharedPaint.Color =
  SKColors.White` before `base.Paint` (so the reset happens immediately before the wrapped child paints).
  It draws nothing of its own. Document the cross-frame `SharedPaint` leak + why the read-only view is the
  only one affected.
- [x] 4a.2 In `src/Mod/ScribeDialogBase.Layout.cs`, wrap the backdrop `Container` returned by
  `WrapBackdrop` (pixel-art path) in `ScribeResetPaintColor` so the reset runs each frame before the
  backdrop's `DrawMaskedBox`. Frame-order-independent (does not rely on painting an opaque element last).

## 5. Fix the tablet's ground-placement orientation (lies on its edge)

- [x] 5.1 In `src/Mod/assets/scribe/itemtypes/scribetablet.json`, change `groundStorageTransform.rotation.z`
  from `90` to `0` in all three transform blocks (base wet/wax, `*-hard`, `*-fired`). Root cause: the
  transform was copy-pasted from `scribenotebook.json`, whose `z:90` roll correctly lays a spine-up BOOK
  model flat — but the `item/tablet-clay` model is already built lying flat (body `tablet1` is thin in Y with
  the `writing1` face on top), so the same `z:90` rolls the tablet onto its edge. Keep the `y:35` yaw (a
  pleasant diagonal, matching the notebook convention) and the translation/origin. Leave the notebook and the
  tablet's `groundTransform` (already `z:0`) unchanged.

## 4. Verification

- [x] 4.1 Build the solution and run the Core test suite — confirm 0 errors and the suite stays green; verify no
  new `Vintagestory.*` reference leaked into `src/Core/`. *(Build clean 0 errors; Core 283/283; Core purity
  intact — all changes in `src/Mod/`. `BlockLiquidContainerBase` comes from the already-referenced
  VSSurvivalMod assembly, no new dependency.)*
- [ ] 4.2 `bash build/restage.sh Debug`, then in-game: confirm the schematic recipe is craftable at 2×2 and the
  Clockmaker's Notebook handbook shows both recipes as separate grids with the `* Requires Tinkerer trait`
  asterisk on the trait one only.
- [x] 4.3 In-game: crouch + right-click a bucket/barrel of water while holding a hard tablet → it softens and
  keeps its document; repeat aimed at an empty/non-water container and at open ground → no softening and the
  ground-storage placement still works; confirm a wet tablet and a fired tablet both no-op on the gesture.
- [ ] 4.4 In-game: craft a clay tablet of each color and confirm the recipe now consumes 12 clay (a 2×3 block)
  rather than 8, still yields one tablet, and the wax tablet recipe is unchanged.
- [ ] 4.5 If the container swallows the crouch-right-click (quench does nothing in-game), add
  `handleLiquidContainerInteract: true` to `scribetablet.json` (design D5 fallback) and retest.
- [ ] 4.6 In-game: open a hand-fired AND a hardened (dried-but-unfired) clay tablet and confirm the GUI
  backdrop is fully OPAQUE — no uniform see-through onto the world behind it — at every scroll position.
  Then open a wet tablet's editor and a tabbed Lectern/Notebook view and confirm those backdrops look
  unchanged. (Requires a full client relaunch after restage, since assets load at boot.)
- [ ] 4.7 In-game: crouch + right-click a tablet onto open ground and confirm it now lies FLAT with the
  writing face up (at a slight `y:35` diagonal), not standing/rolled on its edge. Check a wet, a hard, and a
  fired tablet (all three transform blocks). Confirm the held/dropped-item render is unchanged.

## 5. Recast the cuneiform reveal timing model (stroke-count-driven, no fixed per-letter slot)

> **Context / intent (from the 2026-08-05 session — read this first).** The stroke-progression reveal
> currently gives EVERY character a fixed time slot of `PerLetterMs` (150 ms), with a character's own strokes
> ticking `PerStrokeMs` (50 ms) apart INSIDE that slot. So a 2-stroke `L` and a 5-stroke `B` take exactly the
> same wall-clock time — the per-letter slot dominates and the stroke count is cosmetic. The author dislikes
> that flatness. The new model: **time is a pure running total of strokes; there is no per-letter slot.** Each
> stroke advances the clock by `PerStrokeMs`; a SPACE contributes a fixed 2 stroke-units of pause but draws
> nothing. A stroke's reveal time = (count of all stroke-units before it in the line, counting each glyph's
> real authored stroke count and each space as 2) × `PerStrokeMs`. This makes complex letters genuinely take
> longer — the "human/manual, some letters are more work to scribe" feel the author wants. `PerLetterMs` is
> REMOVED from the model entirely (do not keep it as a 0 — untangle it). Worked example the author gave, at
> `PerStrokeMs = 80`, typing `"A B"`: `A` (3 strokes) = 240 ms, the space (2 units) = 160 ms, `B` (5 strokes)
> = 400 ms — i.e. `B`'s first stroke reveals at t=240+160=400 ms and its 5 strokes then tick 80 ms apart.
> Target speed for this pass: **`PerStrokeMs = 80`** (up from 50 — the author wants it a little slower).
> Stroke counts per glyph are authored data (A–Z average ≈3.1, min 2, max 5; punctuation 1–7); see
> `src/Mod/assets/scribe/textures/fonts/cuneiform-glyphs-1.json` and `GlyphBundle`.

- [x] 5.1 **Core — make the reveal engine stroke-count aware (`src/Core/Cuneiform/CuneiformReveal.cs`).** The
  current `IsStrokeRevealed(globalCharIndex, strokeOrdinal, baselineChars, elapsedMs, schedule)` computes
  `letterOffset * PerLetterMs + strokeOrdinal * PerStrokeMs`, which assumes the fixed-slot model. Replace the
  time basis with a cumulative stroke-unit count. The pure function no longer has enough info from
  `(charIndex, strokeOrdinal)` alone — it needs, per source character from the baseline up to the target
  stroke, how many stroke-units precede it (each glyph = its authored stroke count; each space = 2; a
  missing-glyph gap — decide: treat as 0 or as a small fixed pause, recommend matching the space's 2 or a new
  small constant, document the choice). Design decision for the picking-up agent: EITHER (a) pass a precomputed
  `IReadOnlyList<int>` of per-character stroke-units into a new overload and have the function sum a prefix, OR
  (b) add a small `CumulativeStrokeUnits` helper in Core that the Mod builds from the laid-out line and feeds
  in. Keep it pure and VS-API-free (Core invariant). `RevealSchedule` should drop `PerLetterMs`; keep
  `PerStrokeMs`. Also decide how a space's "2 units" is expressed — a named constant (e.g.
  `SpaceStrokeUnits = 2`) is clearest.
  *(DONE. `RevealSchedule` now carries only `PerStrokeMs`. `IsStrokeRevealed` takes a new
  `IReadOnlyList<int> cumulativeStrokeUnits` prefix-sum (design option a) and prices a stroke at
  `(unitsBeforeChar − unitsBeforeBaseline + strokeOrdinal) × PerStrokeMs`. Added `CumulativeStrokeUnits`
  (prefix-sum builder), named constants `SpaceStrokeUnits = 2` and `MissingGlyphStrokeUnits = 2` (the
  5.1 decision: a missing glyph reads as the same 2-unit pause as a space, so it never zero-times), and the
  hoisted speed `CuneiformReveal.PerStrokeMs = 80`. The per-character units come from a new
  `CuneiformLineLayout.StrokeUnitsFor(text)` that applies the SAME fold/alias/glyph lookup as layout, so
  units line up exactly with drawn strokes. All pure/BCL — Core purity intact.)*
- [x] 5.2 **Core — rewrite `TotalDurationMs`** to return `(total stroke-units from baseline to end) *
  PerStrokeMs` instead of `newChars * PerLetterMs`, so the animation controller's Duration matches the new
  model exactly (the last stroke must reveal at or before Duration). Include the trailing letter's own strokes
  (the old +1 letter-slot tail is no longer needed — the sum already covers every stroke).
- [x] 5.3 **Mod — feed per-character stroke-units into the reveal at both call sites.** In
  `ScribeMultilineField.cs` (`UpdateReveal`, ~L938) and `ScribeCuneiformTitleField.cs` (`UpdateReveal`, ~L150),
  the State already has the laid-out `CuneiformLine`(s) via the render path and the `GlyphBundle`
  (`Widget.CuneiformBundle`). Build the per-character stroke-unit list for the current text (glyph → authored
  `Strokes.Count`; space → 2; missing glyph → the 5.1 decision) and thread it through the same seam the
  schedule uses. The render object (`ScribeCuneiformField.cs`) calls `IsStrokeRevealed` per stroke in
  `DrawStrokePass`; update that call to the new signature. NOTE the render object already carries
  `SourceCharIndex`/`StrokeOrdinal` per `PositionedStroke` — the stroke-unit prefix can be derived from the
  same line the strokes came from, so consider computing it render-side from `lines` rather than plumbing a
  parallel array through the widget (whichever keeps the widget's prop surface smaller — the render object is
  the natural owner since it already holds the laid-out lines).
  *(DONE. The render object (`ScribeCuneiformField.cs`) is the owner: it builds
  `revealCumulativeUnits` in `PerformLayout` from the WHOLE buffer via `layout.StrokeUnitsFor(text)` →
  `CumulativeStrokeUnits` (absolute char indices, wrapping-independent) and passes it to the updated
  `IsStrokeRevealed` in `DrawStrokePass`. Both field states build the same prefix-sum via a small
  `CumulativeRevealUnits(text)` helper (bundle → `StrokeUnitsFor`; falls back to 1-unit-per-char before the
  bundle loads) purely to size `TotalDurationMs`. The two `revealPerStrokeMs`/`revealPerLetterMs` widget
  props were REMOVED (smaller prop surface) since the speed is now single-homed in Core.)*
- [x] 5.4 **Set `PerStrokeMs = 80`** and REMOVE `PerLetterMs` at both Mod constant sites
  (`ScribeMultilineField.cs:547-548`, `ScribeCuneiformTitleField.cs:96-97`) and anywhere the render object
  defaults it (`ScribeCuneiformField.cs` `revealPerLetterMs`, and the widget ctor params
  `revealPerLetterMs`). These two constants are DUPLICATED across the two field files today — while here,
  hoist them into ONE shared constant (e.g. a `CuneiformRevealTiming` static in Core, or a shared const on the
  render object) so the editor and title band can't drift. The author explicitly wants them unified.
  *(DONE. Hoisted to `CuneiformReveal.PerStrokeMs = 80` in Core — the ONE home. Both field states hold a
  `static readonly RevealScheduleShared = new(CuneiformReveal.PerStrokeMs)` and the render object reads
  `CuneiformReveal.PerStrokeMs` directly, so editor rows and the title band share one value. `PerLetterMs`
  is gone from the model everywhere — the `RevealSchedule` struct, both field constants, and the render
  object/widget defaults + ctor params.)*
- [x] 5.5 **Core tests (`tests/Core.Tests/CuneiformTests.cs`).** The existing reveal tests
  (`Reveal_NewLetters_PressInOnSchedule`, `Reveal_StrokeOrdinalOffsetsFromLetterStart`,
  `Reveal_BaselineOffsetsTheSchedule`, `Reveal_TotalDuration_*`) all encode the OLD per-letter-slot math
  (`perLetterMs: 100`) and WILL fail — rewrite them for the stroke-count model. Add a test that pins the
  author's worked example: with `PerStrokeMs = 80` and per-char units `[3, 2(space), 5]` for `"A B"`, the
  space-then-`B` boundary reveals `B`'s first stroke at 400 ms (399 → false, 400 → true) and `B`'s last stroke
  at 400 + 4*80 = 720 ms. Keep the baseline-always-revealed and snap-on-non-append behaviors covered.
  *(DONE. Rewrote the reveal tests for the stroke-count model: `Reveal_WorkedExample_ABSpaceB_At80MsPerStroke`
  pins the author's example (B's first stroke 399→false / 400→true, last stroke 720),
  `Reveal_ComplexLetterDelaysTheNextLetterMoreThanASimpleOne` proves a 5-stroke letter pushes the next later
  than a 2-stroke one, plus baseline/ordinal/duration coverage. Added `StrokeUnitsFor_*` tests, including one
  that reads the SHIPPED bundle and confirms "A B" → `[3, 2, 5]` — tying the pinned numbers to real authored
  data (A=3, B=5 strokes).)*
- [~] 5.6 **Verify + playtest.** `dotnet build` clean, Core suite green, no `Vintagestory.*` in `src/Core/`.
  Then `bash build/restage.sh Debug`, fully relaunch, and with cuneiform + stroke-progression ON: type a word
  and confirm complex letters (B/Q/G/M/O/R/W) visibly take longer to press in than simple ones (C/L/T/V/X/Y),
  spaces read as a short pause, and the overall pace feels right at 80 ms/stroke. Tune `PerStrokeMs` from
  there if needed. Confirm the ghost lead-in still leads correctly under the new timing (it shares the same
  reveal gate).
  *(Code half DONE: `dotnet build` clean (0 errors; the 4 warnings are pre-existing/unrelated), Core suite
  286/286 green, Core purity intact (no `Vintagestory.*` in `src/Core/`). The in-game restage + playtest
  (complex-vs-simple letter pacing, space pause, ghost lead-in under the new timing) is DEFERRED to the
  author — left unchecked as `[~]`.)*
