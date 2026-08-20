## 1. Confirm the runtime data (measure, don't theorize)

- [ ] 1.1 Add a client-only dev command `.scribeprobe [code]` (held item if no code) that prints, for
      each matched grid recipe: the output's `PageCodeForStack`, the current `SignatureOf`, the derived
      ingredients (code + per-craft qty), and any notes. Gate/register it alongside the existing dev
      diagnosis commands (`.scribelight` precedent). Client-side, no Core.
- [ ] 1.2 With `.scribeprobe` on a copper vs. a gold metal lantern, capture the two outputs and confirm
      (a) their `PageCodeForStack` strings differ by `material`, and (b) the OLD `SignatureOf` strings
      are identical (the collision). Record the captured strings in the task notes as ground truth.
- [ ] 1.3 Capture the liquid/container repro behind 10.9 with `.scribeprobe` (a recipe that consumes a
      liquid portion and/or a container) so §4's fix targets the real case, not a guess.

## 2. Re-key recipe identity onto the Handbook page code

- [ ] 2.1 Add `OutputPageCode(GridRecipe)` to `ScribeCraftRecipeProbe` = `PageCodeForStack(
      recipe.Output.ResolvedItemStack)` (guarded for a null resolved output → `"?"`). Uses
      `Vintagestory.GameContent.GuiHandbookItemStackPage` (already referenced).
- [ ] 2.2 Change `SignatureOf` to `{OutputPageCode(recipe)}|{IngredientPattern}|{WxH}`. Verify via
      `.scribeprobe` that copper and gold now yield distinct signatures and common items yield a stable
      `class-shortcode|...` signature.
- [ ] 2.3 Replace the `Output.ResolvedItemStack.Satisfies(stack)` test in `MatchingRecipes` with
      page-code equality: compute `want = PageCodeForStack(pageStack)` once, yield recipes where
      `recipe.ShowInCreatedBy && OutputPageCode(recipe) == want`. Keep the null/primary-output guards.
- [ ] 2.4 Confirm `ResolveBySignature` still round-trips: a signature produced by `SignatureOf` in the
      same session re-resolves the same recipe (generator/self-heal path unchanged in shape).

## 3. Persisted-signature graceful degrade (D5)

- [ ] 3.1 Confirm that an old-format (bare-code) persisted signature returns `null` from
      `ResolveBySignature` and that the Craft parent then degrades to a plain output tracker with child
      trackers intact (no crash/mis-bind) — i.e. the existing D3 path handles the format change. Add a
      Core.Tests case for the degrade contract if one does not already cover an unresolvable signature.
- [ ] 3.2 Do NOT add a migration/compat shim (unreleased feature). Note in code why the format change
      is safe (comment referencing the D5 rationale).

## 4. Wildcard family names + liquid/container notes

- [ ] 4.1 In ingredient derivation, when an ingredient is a genuine wildcard (`MatchingType != Exact`
      and code contains `*`), display a readable family name (resolve a representative member's base
      name, or a `Lang` key `scribe:scribe-gui-craft-any-family` parameterized by it). Keep the stored
      matching code as the wildcard. Fall back to the raw code if no name resolves (no regression).
- [ ] 4.2 Add the `scribe:scribe-gui-craft-any-family` lang key (+ pt-br English fallback) if used.
- [ ] 4.3 Verify/tighten the liquid path (D4): a `MatterState == Liquid` resolved ingredient stays a
      note; confirm the captured 10.9 container case (§1.3) is either counted correctly as a discrete
      item or noted — whichever the repro shows is correct. No litre math.

## 5. Tests + build

- [ ] 5.1 `dotnet build src/Mod/Mod.csproj -c Debug` clean (0 warnings/errors).
- [ ] 5.2 `dotnet test tests/Core.Tests/Core.Tests.csproj` green (no Core change expected; confirm the
      degrade-contract test from §3.1 passes).

## 6. Docs

- [ ] 6.1 Add a `VSAPI-NOTES.md` entry: `GuiHandbookItemStackPage.PageCodeForStack` is the
      attribute-qualified variant-identity primitive (folds attributes minus `IgnoredStackAttributes` +
      `durability`, sorted); it is what VS uses per-page and what Tallybook keys recipe groups on. Note
      it lives in `VSSurvivalMod.dll` / `GameContent` and is `public static`.

## 7. In-game verification (playtest gate)

- [ ] 7.1 In-game: open several metal lanterns' Handbook pages, add a Crafting Task from each, and
      confirm each task's ingredient subtasks list THAT metal's plate/nails (and lining, if lined) —
      copper ≠ gold ≠ iron, no two identical.
- [ ] 7.2 In-game: confirm a lined vs. unlined lantern page produces the right ingredient set (lining
      subtask present only for the lined variant).
- [ ] 7.3 In-game: confirm a common attribute-less item (e.g. planks) still adds its Crafting Task with
      the same ingredients/labels as before (no regression).
- [ ] 7.4 In-game: confirm a wildcard-ingredient recipe shows a readable family name on the subtask,
      and a liquid-consuming recipe shows the liquid as a note.
