## 1. Core model + math (no VS API)

- [x] 1.1 Extend `ScribeCraftIngredient` (src/Core/ScribeCraftMath.cs) with `bool IsLiquid` and
      `double LitresPerCraft`, both defaulted so existing solid-ingredient construction is unchanged; update
      the XML doc-comment to explain the litre semantics.
- [x] 1.2 Add `ScribeCraftMath.LitreTarget(double litresPerCraft, int craftsNeeded)` = `max(1, ceil(litresPerCraft × craftsNeeded))`
      (pure BCL, integer result, non-positive inputs clamped), with a doc-comment stating ceil is applied to
      the batch total, not per craft.
- [x] 1.3 In `ScribeDocument.ReconcileCraftIngredients`, branch per ingredient: a liquid ingredient's child
      target is `ScribeCraftMath.LitreTarget(ing.LitresPerCraft, craftsNeeded)`; a solid's stays
      `PerCraftQuantity × craftsNeeded`. Child creation/matching by `TargetItemCode` is unchanged.

## 2. Core unit tests (tests/Core.Tests)

- [x] 2.1 `ScribeCraftMath.LitreTarget`: 1 L × 1 craft → 1; 2 L × 3 → 6; 0.1 L × 10 → 1 (ceil at total);
      0.25 L × 1 → 1; zero/negative litres or crafts → 1.
- [x] 2.2 `ReconcileCraftIngredients` with a liquid ingredient creates a depth-1 Tracker whose target is the
      ceil litre total, preserves an existing matched child's id + `CurrentQuantity` on rescale, and does not
      disturb sibling solid children.
- [x] 2.3 Two liquid ingredients with the same `ItemCode` (pre-merged by the Mod layer) reconcile to a single
      child; two distinct liquid codes reconcile to two children.

## 3. Mod: recipe probe — emit a liquid ingredient, note as fallback

- [x] 3.1 Refactor `ScribeCraftRecipeProbe.TryAddLiquidNote` into `TryAddLiquid`, reading
      `requiresContent.code`/`type` AND the sibling `requiresLitres` float (per the decompiled
      `BlockLiquidContainerBase.OnHandbookRecipeRender` path in design.md).
- [x] 3.2 On full resolution (concrete code, `requiresLitres > 0`, item/block stack resolves), emit a liquid
      `ScribeCraftIngredient(code, PerCraftQuantity: 1, IsLiquid: true, LitresPerCraft: requiresLitres)`;
      merge duplicate liquid codes by summing litres before returning.
- [x] 3.3 On any failure (wildcard code, missing/non-positive `requiresLitres`, unresolvable stack), append
      the existing `scribe:scribe-gui-craft-liquid-note` Text note exactly as today (graceful degrade).
- [x] 3.4 Confirm the grid-cell `MatterState == Liquid` path in `DeriveIngredients` routes through the same
      resolve-or-note logic (no double-emit, no mis-count of the solid container).
- [x] 3.5 Update `.scribeprobe` (`Describe`) so a liquid ingredient prints as a counting ingredient with its
      litre-derived per-craft amount, and an unresolvable liquid still prints as a note.

## 4. Mod: carried-container litre-summing count

- [x] 4.1 In `ScribeTrackerCounter.CountCarried`, branch when `ingredient.ResolvedItemStack?.Collectible.MatterState == EnumMatterState.Liquid`:
      for each carried slot whose `Itemstack.Collectible is BlockLiquidContainerBase`, read
      `GetContent(stack)`, and if it `SatisfiesAsIngredient(content, checkStackSize:false)`, add
      `content.StackSize / (BlockLiquidContainerBase.GetContainableProps(content)?.ItemsPerLitre ?? 1f)`.
- [x] 4.2 Return `(int)Math.Floor(totalLitres + 1e-3)` from the liquid branch (epsilon absorbs float error on
      exact multiples); leave the solid stacksize-sum branch byte-identical.
- [x] 4.3 Verify `TryResolveIngredient` resolves a concrete liquid code through the existing concrete branch
      so `ResolvedItemStack` (and thus `MatterState`) is populated for the 4.1 check; no change to the
      wildcard/attribute-encoded branches.
- [x] 4.4 Confirm both count call sites — `ScribeDialogBase.TrackerCount.RecomputeTrackers` and the HUD count
      engine (`HudScribePins`) — pick up liquid counting with no change of their own (they only call
      `CountCarried`).

## 5. Build + Core suite green

- [x] 5.1 `dotnet build` clean (0 warn / 0 err); the new §2 tests all pass (35/35 in `ScribeCraftTaskTests`).
      NOTE: 7 suite failures remain in `ScribeBrightnessCurveTests` — pre-existing and unrelated (that curve
      was retuned in `e43edb3` without updating its tests; untouched by this change).
- [x] 5.2 Restage Debug (`build/restage.sh Debug`) with the client closed, ready for the playtest gate.

## 6. In-game playtest verification (manual)

- [x] 6.1 Open an ink-and-quill (or similar liquid recipe) Handbook page, Add Crafting Task, and confirm the
      liquid appears as a **Tracker child with a have/need counter**, not a Text note. Its target equals the
      recipe's litres for the chosen batch (verify via `.scribeprobe`).
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 6.2 With an empty inventory the liquid Tracker reads `0 / N`. Pick up a bucket/bowl holding the liquid;
      the count rises to the container's litres within ~1 s (event path + poll backstop).
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 6.3 Carry multiple containers of the same liquid and confirm the litres sum; a container of a different
      liquid contributes 0.
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 6.4 Carrying ≥ the target litres marks the Tracker satisfied and applies the player's tracker-completion
      setting (complete / delete / nothing); dropping below the target afterward does not re-fire.
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 6.5 The liquid Tracker also updates correctly when pinned to the HUD (same `CountCarried` path).
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 6.6 Sweep a few real recipes with `.scribeprobe` (Open Question in design.md): confirm any block-`type`
      liquid and any sub-1-litre requirement round up as expected, and that an unresolvable/wildcard liquid
      still degrades to a note.
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 6.7 Record verdicts in TESTING.md per the project's playtest workflow.
