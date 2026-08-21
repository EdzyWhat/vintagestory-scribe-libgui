## Context

`ScribeCraftRecipeProbe` reads the live client grid-recipe registry and, for a Handbook page's
attributed `ItemStack`, produces the crafting-task recipe variants and their ingredient lists. VS
expands variant/wildcard grid recipes into one concrete `GridRecipe` per resolved output at load time
(`GenerateRecipesForAllIngredientCombinations` → `FillPlaceHolder`), substituting `{var}` into both
ingredient codes AND the output's `attributes` JSON. So each metal lantern is a distinct concrete
recipe whose `Output.ResolvedItemStack` carries e.g. `{material: gold, lining: plain, glass: quartz}`.

**Ground truth from the shipped assets** (`assets/survival/blocktypes/metal/lantern.json`,
`recipes/grid/lantern.json`):
- The lantern block has ONE code family `lantern-{size}-{position}`; `material`/`lining`/`glass` are
  itemstack **attributes**, not part of the code.
- `handbook: { groupBy: ["lantern-{size}-*"] }` — this **only dedups the Handbook list**; each metal
  variant still has its own `GuiHandbookItemStackPage` with a concrete attributed `Stack`. The stack
  VS hands `CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo(inSlot, ...)` (which our
  `ScribeHandbookPatch` postfixes) is that page's own `dummySlot.Itemstack` — the exact on-screen
  variant, attributes intact. So the probe already receives the right stack; it just mis-identifies
  the recipe.

**The two defects in the current probe:**
1. `SignatureOf(recipe)` = `CodeOf(recipe)|pattern|WxH` where `CodeOf` is the **bare** collectible
   code. All 13 metal fan-outs of one pattern share a signature → dedup collapses them to the first
   (copper). This is the certain, primary bug.
2. `MatchingRecipes` uses `Output.ResolvedItemStack.Satisfies(pageStack)` (an attribute-subset test).
   Its correctness depends on the subset direction over output attributes; it is a fragility we can
   remove entirely.

**Proven precedent (Tallybook 0.3.16, decompiled).** Tallybook solved this exact class of problem by
delegating variant identity to VS's `GuiHandbookItemStackPage.PageCodeForStack(stack)` rather than
comparing attributes by hand. That method serializes a stack into a stable, attribute-qualified code:
```
block-lantern-large-up-{"glass":"quartz","lining":"plain","material":"gold"}   // gold
block-lantern-large-up-{"glass":"quartz","lining":"plain","material":"copper"} // copper
```
(clone attributes → remove `IgnoredStackAttributes` + `durability` → `SortedCopy(true)` →
`TreeAttribute.ToJsonToken`; attribute-less stacks return just `class-shortcode`). Tallybook's per-
variant signature is `{OutputPageCode}|{Pattern}|{Width}x{Height}` and it matches recipes by bucketing
on the bare short code, then filtering `group.OutputPageCode == PageCodeForStack(viewedStack)`. It uses
`Satisfies`-style tests only for **ingredients**, never the output.

`GuiHandbookItemStackPage.PageCodeForStack` is `public static` in `VSSurvivalMod.dll`
(`Vintagestory.GameContent`), which our Mod project already references — callable directly, no
reflection, no new dependency.

## Goals / Non-Goals

**Goals:**
- A Crafting Task created from a metal lantern's Handbook page binds THAT lantern's recipe and derives
  THAT metal's ingredient list (its own plate/nails/lining), not copper's.
- Remove the `Satisfies`-direction fragility from output matching.
- Attribute-less common items behave byte-identically to today (same links, same ingredient lists).
- Genuine wildcard ingredients (`metalplate-*`) render a readable family name, not a raw code.
- Liquid/container ingredients render as non-counting notes (10.9), never mis-counted.

**Non-Goals:**
- Migrating already-persisted old-format signatures (unreleased feature; graceful degrade covers it).
- Letting the player pick a *different* metal than the page they opened (the page already fixes the
  variant; multi-metal selection is out of scope).
- Counting liquids by litres or handling barrel/cooking recipes (grid recipes only, as today).
- Any change to batch-scaling math or the child-tracker reconcile.

## Decisions

**D1 — Identity via `PageCodeForStack`.** Introduce one helper:
```csharp
private static string OutputPageCode(GridRecipe r)
    => r.Output?.ResolvedItemStack is { } s ? GuiHandbookItemStackPage.PageCodeForStack(s) : "?";
```
`SignatureOf(recipe)` becomes `{OutputPageCode(recipe)}|{IngredientPattern}|{WxH}`. For an
attribute-less item this equals `class-shortcode|pattern|WxH` — a stable rename from the old
`domain:code|...`, but never colliding across attribute variants.

**D2 — Match by page-code equality, not `Satisfies`.** `MatchingRecipes(capi, pageStack)` computes
`string want = PageCodeForStack(pageStack)` once, then yields recipes where `recipe.ShowInCreatedBy`
and `OutputPageCode(recipe) == want`. This is exact and direction-free. It also naturally honors the
"primary output only" intent (we only ever compare the recipe's primary `Output`).

*Why not keep `Satisfies` as a fallback:* mixing an equality filter with a subset fallback re-opens
the over-match door (a partially-attributed page stack could subset-match many variants). Page-code
equality is the single rule Tallybook relies on; we adopt it wholesale for outputs.

**D3 — Wildcard family names.** In `IngredientCode`/display, keep the current split: an `Exact`
`{var}`-bound ingredient resolves to a concrete stack and keeps its exact name. A genuine wildcard
(`MatchingType != Exact`, code contains `*`) currently shows the raw `domain:code-*`. Replace that
display with a family label. Cheapest robust source: resolve the ingredient's first matching stack
(the probe already has `capi.World` and the resolved ingredient) and use its collectible's *base*
name, or a `Lang` key `scribe:scribe-gui-craft-any-family` parameterized by that name (e.g. "Any metal
plate"). The child Tracker's **stored code** stays the wildcard (so it still counts any member); only
the display name changes. If a clean family name can't be derived, fall back to the current raw code
(no regression).

**D4 — Liquid / container notes (10.9).** Keep the D7 rule (a `MatterState == Liquid` resolved
ingredient → a note, not a counted item). Verify the container case: a liquid *portion* ingredient in
grid recipes typically resolves to the liquid collectible (already caught), but confirm a bare
container ingredient (an empty bowl/bucket used structurally) is either counted correctly as an item
or noted — decide per the actual playtest repro captured in tasks §1. No litre math.

**D5 — Persisted-signature compatibility.** A Craft parent persists its signature to re-resolve one
recipe on document open (`ResolveBySignature`) and to self-heal/rescale. Changing the format means an
old-format signature returns `null` from `ResolveBySignature`. Per the existing D3 graceful-degrade
contract this leaves the parent as a plain output tracker with its child trackers intact — no crash,
no mis-bind, no data loss; only auto-rescale stops until the task is re-created. Since Crafting Tasks
are unreleased, no shipped save carries an old-format signature. We therefore do **no** migration and
add **no** compatibility shim; we only confirm the degrade path is exercised (tasks §3).

**D6 — Dev-probe confirmation.** Add a lightweight client dev command (e.g. `.scribeprobe`) that, for
the held item (or a supplied code), prints each matched recipe's `OutputPageCode`, signature, derived
ingredients, and notes. This is the cheap "measure don't theorize" instrument to confirm gold vs
copper resolve distinctly in-game before the playtest gate, mirroring `.scribelight`/`.geartune`
precedent. Gate it behind the existing dev-diagnosis toolkit conventions.

**D8 — Whole-code wildcard + `allowedVariants` (the item-air trap).** *Added after the 2026-08-20
playtest:* the Hunter's Backpack grid recipe uses a **whole-code** wildcard ingredient —
`{ code: "*", allowedVariants: ["papyrustops", "cattailtops"] }` — not a path-prefix wildcard like
`metalplate-*`. D3 assumed the stored wildcard code still "counts any member," but that assumption
only holds for a prefix wildcard whose `SearchItems` members ARE the family. For a bare `*`:

- **Display** — `ResolveWildcardMember` does `SearchItems(new AssetLocation("*"))`, which matches
  *every* game item; the registry returns `game:item-air` first, so the child Tracker reads
  "**Game: Item-Air (any variant)**". The playtest-reported symptom.
- **Counting** — `ScribeTrackerCounter.TryResolveIngredient` builds a `Wildcard` ingredient with
  `Code = game:*` and **drops `AllowedVariants`**, so `SatisfiesAsIngredient` → `WildcardUtil.Match`
  matches *every carried item*. The tracker over-counts massively (not just the reported cosmetic bug).

Root cause: the child Tracker's target is a **plain string** (`ScribeBlock.TargetItemCode`), by
design, so a wildcard ingredient's `AllowedVariants`/`SkipVariants` are lost in the round-trip. VS's
own `WildcardUtil.Match(wildCard, code, allowedVariants)` overload and
`CraftingRecipeIngredient.SatisfiesAsIngredient` DO honor `AllowedVariants` — *if the ingredient
carries them*. This is exactly the case Tallybook's `RecipeProbe` handles by never reducing an
ingredient to a bare code: it keeps the full `CraftingRecipeIngredient` (its `FirstMatchSample`
resolves a representative by iterating collectibles and testing `SatisfiesAsIngredient(stack, false)`,
which excludes air and honors `allowedVariants`), and counts via the same `SatisfiesAsIngredient`.

Options (final choice deferred to implementation; **B is recommended**):
- **A — Store a representative concrete code.** For a wildcard with `AllowedVariants`, store the first
  allowed variant's concrete code (Exact). *Simple, correct display, but under-counts* a multi-variant
  family (counts only one of papyrus/cattail tops). Rejected as the sole fix.
- **B — Carry `AllowedVariants` through the target reference (recommended).** Extend the stored tracker
  reference to persist the wildcard's `AllowedVariants` (+ `SkipVariants`) alongside the code — either
  as a new optional field on the block/`ScribeCraftIngredient` (a persisted-schema addition; note it is
  a plain `string[]`, so **Core stays VS-API-free** — no `Vintagestory.*` reference) with codec
  round-trip, or a Mod-side code microformat (`game:*|papyrustops,cattailtops`) parsed only where the
  probe/counter/`ResolveWildcardMember` consume it (no Core/schema change). Then set `AllowedVariants`
  on the counting ingredient (correct count) and resolve the representative member via
  `SatisfiesAsIngredient`/an allowed variant (correct display, never air). Persistence decision — schema
  field vs. code microformat — to be made at implementation; the microformat keeps the change Mod-only
  and avoids touching the document format, matching the "stored references are plain strings" invariant.
- **C — Suppress air + degrade.** At minimum, `ResolveWildcardMember` must skip `*-air` members and any
  degenerate bare-`*` code so it never shows "Item-Air"; combine with B for a real family, or fall back
  to a generic "any suitable item" label (Tallybook's own fallback) when no allowed set is available.

Regardless of A/B/C, `ResolveWildcardMember` gains an **air-exclusion guard** (skip `item-air`/`block-air`)
so the cosmetic failure can never recur even for an unforeseen wildcard shape.

## Risks / Trade-offs

- **`PageCodeForStack` attribute set vs. recipe output attributes.** The method strips
  `IgnoredStackAttributes` + `durability`. If a recipe output carried an attribute that VS ignores but
  that we'd want to distinguish on, the page codes could still merge. For lanterns the distinguishing
  attributes (`material`/`lining`/`glass`) are NOT ignored, so this is safe; the dev-probe (D6)
  confirms it empirically. Trade-off accepted: we identify variants exactly as VS's own Handbook does,
  which is the correct definition of "a distinct page/variant."
- **Signature rename churn for common items.** Every common-item signature string changes format.
  Because the feature is unreleased and the format is a client-side key (not cross-client wire state),
  this is invisible in practice. Documented in D5.
- **Wildcard family-name resolution cost.** Resolving a representative stack per wildcard ingredient
  is a per-link cost paid only when the Handbook page is opened (not per frame); negligible, and it
  falls back to the raw code if resolution fails.
- **Coupling to a `GameContent` internal.** `PageCodeForStack` is a public static method of a survival-
  mod GUI type. It ships with the base game (same dependency posture as our existing
  `CollectibleBehaviorHandbookTextAndExtraInfo` patch) and is stable API-shaped; if it ever changed we
  would notice via the probe/Atlas. Acceptable given it is the exact primitive VS and Tallybook both
  use for this purpose. Recorded as a `VSAPI-NOTES.md` entry (tasks §4).
