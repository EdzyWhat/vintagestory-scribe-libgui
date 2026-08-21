## Why

A Crafting Task binds to one concrete grid-recipe variant, resolved from the Handbook page the
player is looking at. For an **attribute-encoded output** — a metal lantern, whose `material` /
`lining` / `glass` live as itemstack **attributes** on a single bare block code (`lantern-large-up`),
not in the code itself — the wrong variant is bound. In the 2026-08-19 playtest, every lantern's
"Add Crafting Task" produced the **same** ingredient list (copper plate + silver lining), regardless
of which metal lantern's page was open.

Root cause (confirmed against the shipped assets and the Tallybook mod's proven approach):
`ScribeCraftRecipeProbe.SignatureOf` identifies a recipe by its **bare** output code
(`CodeOf` = `Output.ResolvedItemStack.Collectible.Code`) — with no attributes. Every one of the 13
metal fan-outs of a given lantern pattern therefore collapses to **one** signature, and the
signature-dedup keeps whichever recipe is first in registry order (copper). The recipe *matching*
step (`Output.ResolvedItemStack.Satisfies(pageStack)`) is a separate fragility: it depends on the
subset direction of `Satisfies` over output attributes, which Tallybook — a shipping mod that solved
exactly this — deliberately avoids.

The proven fix is to stop identifying recipes by bare code and instead key variant identity on VS's
own `GuiHandbookItemStackPage.PageCodeForStack(stack)`, which folds a stack's meaningful attributes
(sorted, with `IgnoredStackAttributes` + `durability` removed) into the identity string. This is the
same primitive VS uses to give each attribute variant its own Handbook page — and the same one
Tallybook keys its recipe groups on. Matching then becomes an exact page-code equality rather than an
attribute-subset test.

## What Changes

- **Signature keys on the output's page code, not its bare code.** `SignatureOf` becomes
  `{PageCodeForStack(output)}|{ingredientPattern}|{WxH}`. Gold and copper lantern recipes now produce
  distinct signatures and never collapse. Attribute-less common items are unaffected — for them
  `PageCodeForStack` returns just the bare code, so their signatures are byte-identical to before.
- **Matching keys on page-code equality, not `Satisfies`.** `MatchingRecipes` filters to recipes
  whose `PageCodeForStack(Output.ResolvedItemStack)` equals `PageCodeForStack(pageStack)`, replacing
  the `outStack.Satisfies(stack)` test. This resolves the exact on-screen variant (the stack VS hands
  our postfix in `inSlot.Itemstack`) and is robust regardless of `Satisfies` subset direction.
- **Friendly family names for genuine wildcard ingredients.** A truly broad ingredient
  (`metalplate-*`) currently surfaces its raw wildcard code as a child Tracker's display name. Give
  it a readable family label (e.g. "Any metal plate") derived from the ingredient, instead of the raw
  `domain:code-*` string. Concrete (`{var}`-bound) ingredients keep their exact resolved name.
- **Liquid / container ingredient note correctness (10.9).** Verify and tighten the D7 liquid path so
  a liquid-portion ingredient (and its container) surfaces as a non-counting note rather than being
  mis-counted as a discrete item, matching the observed playtest gap.
- No change to the batch-scaling math (`ScribeCraftMath`), the persisted-signature self-heal contract
  (a signature still re-resolves one recipe on document open), or any Core code. The signature string
  format DOES change (the output segment goes from `domain:code` to the class-qualified page code), so
  any **already-persisted** Craft signature — of any item, not just lanterns — will no longer
  re-resolve after this change. That is acceptable: (a) Crafting Tasks are unreleased (in-progress
  `add-crafting-tasks`), so no shipped save depends on the old format, and (b) the existing D3
  graceful-degrade path covers it — an unresolvable signature leaves the parent as a plain output
  tracker with its child trackers intact (no crash, no data loss, no mis-bind); only auto-rescale of a
  stale-format task stops until it is re-created. See design.md for the full compatibility analysis.

## Capabilities

### New Capabilities
_(none)_

### Modified Capabilities
- `craft-task`: the recipe-variant a Crafting Task binds to is now identified by the output's
  attribute-qualified Handbook page code, so an attribute-encoded output (metal lanterns, and any
  future material-variant block) binds the correct variant and derives that variant's own ingredient
  list. Genuine wildcard ingredients render a readable family name; liquid/container ingredients
  render as notes rather than being counted.

## Impact

- **`src/Mod/ScribeCraftRecipeProbe.cs`**: `SignatureOf` and `MatchingRecipes` re-keyed onto
  `GuiHandbookItemStackPage.PageCodeForStack`; `IngredientCode`/`DeriveIngredients` gain the family-name
  and liquid/container handling. Mod-side only (already references `Vintagestory.GameContent` and the
  live recipe registry).
- **`src/Mod/ScribeItemRef.cs`** (possibly): a small helper to render a wildcard family's display name
  if that logic is cleaner there than in the probe.
- No Core change, no codec/persistence schema change. The signature string format changes; a persisted
  signature is a re-resolution key on the parent Craft row, so old-format keys stop re-resolving and
  degrade per the D3 path (analysis in design.md). No VS API surface change, no new dependency.
- Handbook "Add Crafting Task" links for common (attribute-less) items are byte-identical to today.
