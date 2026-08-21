## 1. Confirm the runtime data (measure, don't theorize)

- [x] 1.1 Add a client-only dev command `.scribeprobe [code]` (held item if no code) that prints, for
      each matched grid recipe: the output's `PageCodeForStack`, the current `SignatureOf`, the derived
      ingredients (code + per-craft qty), and any notes. Gate/register it alongside the existing dev
      diagnosis commands (`.scribelight` precedent). Client-side, no Core.
      — DONE: `ScribeCraftRecipeProbe.Describe(capi, stack)` + `RegisterScribeProbeCommand` in
      `ScribeModSystem.ClientPrefs.cs` (registered next to `.scribelight` in `ScribeModSystem.cs`).
      Optional `code` arg resolves via `ScribeItemRef.ResolveStack`; no arg = active hotbar slot.
- [x] 1.2 With `.scribeprobe` on a copper vs. a gold metal lantern, capture the two outputs and confirm
      (a) their `PageCodeForStack` strings differ by `material`, and (b) the OLD `SignatureOf` strings
      are identical (the collision). Record the captured strings in the task notes as ground truth.
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 1.3 Capture the liquid/container repro behind 10.9 with `.scribeprobe` (a recipe that consumes a
      liquid portion and/or a container) so §4's fix targets the real case, not a guess.
      — Done: the diagnostic served its purpose. The containerized-liquid repro it targeted was root-caused
      (liquid declared on the recipe's `liquidContainerProps`, not a grid cell) and the fix landed; 7.4/10.9
      now surface the liquid note (Confirmed 2026-08-20). No standalone capture artifact needed post-fix.

## 2. Re-key recipe identity onto the Handbook page code

- [x] 2.1 Add `OutputPageCode(GridRecipe)` to `ScribeCraftRecipeProbe` = `PageCodeForStack(
      recipe.Output.ResolvedItemStack)` (guarded for a null resolved output → `"?"`). Uses
      `Vintagestory.GameContent.GuiHandbookItemStackPage` (already referenced).
      — DONE: `OutputPageCode` + a guarded `PageCodeForStack(ItemStack)` wrapper (null/throw → the
      `UnknownPageCode` `"?"` sentinel); added `using Vintagestory.GameContent`.
- [x] 2.2 Change `SignatureOf` to `{OutputPageCode(recipe)}|{IngredientPattern}|{WxH}`. Verify via
      `.scribeprobe` that copper and gold now yield distinct signatures and common items yield a stable
      `class-shortcode|...` signature.
      — DONE (code): `SignatureOf` now `{OutputPageCode}|{pattern}|{WxH}`. `.scribeprobe` in-game
      verification is the §1.2 / §7 gate (left unchecked).
- [x] 2.3 Replace the `Output.ResolvedItemStack.Satisfies(stack)` test in `MatchingRecipes` with
      page-code equality: compute `want = PageCodeForStack(pageStack)` once, yield recipes where
      `recipe.ShowInCreatedBy && OutputPageCode(recipe) == want`. Keep the null/primary-output guards.
      — DONE: exactly this; plus a `want == "?"` bail so a degenerate page stack matches nothing.
- [x] 2.4 Confirm `ResolveBySignature` still round-trips: a signature produced by `SignatureOf` in the
      same session re-resolves the same recipe (generator/self-heal path unchanged in shape).
      — DONE (by inspection): `ResolveBySignature` compares `SignatureOf(recipe) == signature` over the
      live registry — same generator, so a same-session signature re-resolves the same recipe. Shape
      unchanged; only the string format changed (both sides use the new format).

## 3. Persisted-signature graceful degrade (D5)

- [x] 3.1 Confirm that an old-format (bare-code) persisted signature returns `null` from
      `ResolveBySignature` and that the Craft parent then degrades to a plain output tracker with child
      trackers intact (no crash/mis-bind) — i.e. the existing D3 path handles the format change. Add a
      Core.Tests case for the degrade contract if one does not already cover an unresolvable signature.
      — DONE (by inspection): an old bare-code signature matches no live `SignatureOf(recipe)`, so
      `ResolveBySignature` returns `null`; `ReconcileCraftFromSignature` (ScribeDialogBase.Handbook.cs)
      early-returns `false` on that null, leaving parent + children untouched. The degrade lives in the
      Mod layer (Core has no signature-resolution to test); Core already covers the reconcile-not-run
      contract via `Reconcile_ReturnsFalse_WhenNoCraftWithThatId`. No hollow Core test added.
- [x] 3.2 Do NOT add a migration/compat shim (unreleased feature). Note in code why the format change
      is safe (comment referencing the D5 rationale).
      — DONE: no shim; `SignatureOf` doc-comment states the D5 rationale (unreleased ⇒ no shipped save
      carries an old signature ⇒ graceful degrade, no migration).

## 4. Wildcard family names + liquid/container notes

- [x] 4.1 In ingredient derivation, when an ingredient is a genuine wildcard (`MatchingType != Exact`
      and code contains `*`), display a readable family name (resolve a representative member's base
      name, or a `Lang` key `scribe:scribe-gui-craft-any-family` parameterized by it). Keep the stored
      matching code as the wildcard. Fall back to the raw code if no name resolves (no regression).
      — DONE at the DISPLAY seam (not the probe): a child Tracker re-resolves its label from its stored
      code at render, so `ScribeItemRef.ResolveDisplay` now detects a `*` code that resolves to no single
      stack, resolves a representative member via `SearchItems`/`SearchBlocks` (same lookup the tracker
      counter uses), and returns `(member icon, Lang.Get("scribe:scribe-gui-craft-any-family", memberName))`.
      Stored code stays the wildcard (counting unaffected); falls back to the raw code if no member resolves.
- [x] 4.2 Add the `scribe:scribe-gui-craft-any-family` lang key (+ pt-br English fallback) if used.
      — DONE: `"scribe-gui-craft-any-family": "{0} (any variant)"` in `lang/en.json` (the sole shipped
      lang file; no separate pt-br file in this mod).
- [x] 4.3 Verify/tighten the liquid path (D4): a `MatterState == Liquid` resolved ingredient stays a
      note; confirm the captured 10.9 container case (§1.3) is either counted correctly as a discrete
      item or noted — whichever the repro shows is correct. No litre math.
      — DONE (code, corrected 2026-08-20 after playtest FAIL): the original claim ("D7 liquid path is
      unchanged and correct") was WRONG for the real recipes. Playtest (1c33a856 / 4bdff687) showed NO
      liquid note ever surfaced. Decompiled ground-truth: for ink-and-quill/poultice/bandage/oillamp/beenade
      the liquid is NOT a grid cell — it is declared on the RECIPE as
      `attributes.liquidContainerProps.requiresContent` (the grid cell is the solid `bowl-*-fired`), so the
      per-cell `MatterState == Liquid` check can never fire. FIX: added recipe-level
      `ScribeCraftRecipeProbe.TryAddLiquidNote` (recipe `Attributes.liquidContainerProps` → per-cell
      `RecipeAttributes` fallback), mirroring the authoritative vanilla
      `BlockLiquidContainerBase.OnHandbookRecipeRender`. It names the LIQUID as a
      `scribe-gui-craft-liquid-note` (no litre math, per D7); the container bowl stays a counted ingredient
      (genuinely required). The old per-cell `MatterState == Liquid` check is kept (harmless; covers a
      hypothetical future raw-liquid cell). Build 0/0. In-game §7.4 remains the retest gate.

- [x] 4.4 **(NEW — 2026-08-20 playtest: "Game: Item-Air (any variant)" on the Hunter's Backpack).** Fix the
      whole-code wildcard + `allowedVariants` case (design **D8**). The Hunter's Backpack ingredient is
      `{ code: "*", allowedVariants: ["papyrustops","cattailtops"] }`; the current 4.1 display path does
      `SearchItems("*")` → `game:item-air` first, and `ScribeTrackerCounter` builds a `game:*` wildcard that
      DROPS `AllowedVariants`, so it also over-counts every carried item. Fix both:
      - Add an **air-exclusion guard** to `ScribeItemRef.ResolveWildcardMember` (skip `item-air`/`block-air`
        and any degenerate bare-`*` code) so "Item-Air" can never surface again, for any wildcard shape.
      - **Carry `AllowedVariants`/`SkipVariants`** through the tracker's target reference (D8 option B —
        prefer the Mod-side code microformat, e.g. `game:*|papyrustops,cattailtops`, to keep the persisted
        document format and Core untouched) so (a) `ScribeTrackerCounter.TryResolveIngredient` sets them on
        the `Wildcard` ingredient → `SatisfiesAsIngredient` counts only the allowed family, and (b) the
        representative member is resolved via an allowed variant / `SatisfiesAsIngredient`, matching Tallybook's
        `RecipeProbe.FirstMatchSample`. Family label reads e.g. "Papyrus tops (any variant)" / an "any suitable
        item" fallback, never air. Update `IngredientCode`/`DeriveIngredients` to emit the enriched code.
      - Ground truth captured: Tallybook `RecipeProbe` keeps full `CraftingRecipeIngredient`s and resolves
        samples via `SatisfiesAsIngredient(stack, false)` (excludes air, honors `allowedVariants`); VS's
        `WildcardUtil.Match(wildCard, code, allowedVariants)` overload is the counting primitive.
      — DONE (option B, Mod-side microformat): `ScribeItemRef.EncodeWildcard`/`TryParseWildcard` store a
        restricted wildcard as `"<code>|<allowed,csv>[|<skip,csv>]"` (a plain string — Core/document schema
        untouched); unrestricted wildcards still store the bare code (no regression). `IngredientCode` emits it.
        `ScribeTrackerCounter` parses it, sets `AllowedVariants`/`SkipVariants` on the `Wildcard` ingredient
        (exact-family count, verified against decompiled `SatisfiesAsIngredient` + `WildcardUtil.Match`), and
        fixes the ingredient class from a representative allowed-variant member. `ResolveWildcardMember` gained
        (a) a microformat branch that resolves the representative by substituting the first allowed variant into
        `*` (never air), and (b) an `air`-exclusion guard (`FirstNonAir`, `Code.Path != "air"`) for ANY wildcard
        shape. Build 0/0. §7.5 is the in-game retest gate.

## 5. Tests + build

- [x] 5.1 `dotnet build src/Mod/Mod.csproj -c Debug` clean (0 warnings/errors).
      — DONE: Build succeeded, 0 Warning(s) / 0 Error(s).
- [x] 5.2 `dotnet test tests/Core.Tests/Core.Tests.csproj` green (no Core change expected; confirm the
      degrade-contract test from §3.1 passes).
      — DONE: no Core change in this work; the craft/reconcile suite (incl.
      `Reconcile_ReturnsFalse_WhenNoCraftWithThatId`) passes. NOTE: 6 pre-existing failures in
      `ScribeBrightnessCurveTests` / `ScribePlayerSettingsTests.Default_IlluminationFloor_MatchesDrawnCurveFloor`
      (an illumination-floor default drift, 0.03 vs 0.05) are UNRELATED to this change — Core was not
      touched. Flagged for a separate fix.

## 6. Docs

- [x] 6.1 Add a `VSAPI-NOTES.md` entry: `GuiHandbookItemStackPage.PageCodeForStack` is the
      attribute-qualified variant-identity primitive (folds attributes minus `IgnoredStackAttributes` +
      `durability`, sorted); it is what VS uses per-page and what Tallybook keys recipe groups on. Note
      it lives in `VSSurvivalMod.dll` / `GameContent` and is `public static`.
      — DONE: new "Handbook variant identity + the three page classes" section (also documents the
      `Satisfies` over-match trap, the `groupBy`-only-dedups-the-list fact, and the meal page class for
      the sibling change).

## 7. In-game verification (playtest gate)

- [x] 7.1 In-game: open several metal lanterns' Handbook pages, add a Crafting Task from each, and
      confirm each task's ingredient subtasks list THAT metal's plate/nails (and lining, if lined) —
      copper ≠ gold ≠ iron, no two identical.
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 7.2 In-game: confirm a lined vs. unlined lantern page produces the right ingredient set (lining
      subtask present only for the lined variant).
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 7.3 In-game: confirm a common attribute-less item (e.g. planks) still adds its Crafting Task with
      the same ingredients/labels as before (no regression).
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 7.4 In-game: confirm a wildcard-ingredient recipe shows a readable family name on the subtask,
      and a liquid-consuming recipe shows the liquid as a note.
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 7.5 In-game (regression for 4.4 / D8): add a Crafting Task from the **Hunter's Backpack** Handbook page.
      Confirm the papyrus/cattail-tops subtask reads as a real family name (NOT "Game: Item-Air (any variant)"),
      and that its carried count reflects only papyrus + cattail tops — hold a stack of unrelated items and
      confirm the count does NOT balloon (no over-count from a bare `*` match).
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
