## Why

The Scribe Lectern block (`scribe:scribelectern`) is currently obtainable only from the creative
inventory — there is no crafting recipe anywhere in the mod. For a survival-facing v1, players must
be able to craft it. Its own blocktype comment already states the design intent ("meant to be
crafted rather than found"); this change delivers that recipe.

## What Changes

- Add a survival grid-crafting recipe that yields one Lectern from a "writing desk" set of
  ingredients: a wooden frame (4 planks + metal nails) plus writing implements (parchment, a
  feather quill, plain leather) and ink — 1 litre of black dye supplied in a fired bowl.
- The ink is consumed as a liquid-in-container ingredient (the vanilla `inkandquill` mechanism),
  not as a grid cell and not via a barrel recipe. The bowl is consumed by the craft.
- Ships as a single JSON asset under a new `assets/scribe/recipes/grid/` directory; no C# code
  changes (grid recipes auto-load from every mod domain via the vanilla `RecipeLoader`).

## Capabilities

### New Capabilities
- `lectern-crafting`: how the Lectern block is obtained in survival — the grid recipe's ingredient
  set, the liquid-ink-in-a-bowl requirement, and the resulting output. Distinct from the existing
  `lectern-block` capability, which governs the placed block's document/persistence behavior.

### Modified Capabilities
<!-- None. The recipe adds a new obtaining mechanism; it does not change any existing spec-level
     behavior of the placed block (document, break/replace, pins) owned by lectern-block. -->

## Impact

- **New asset**: `src/Mod/assets/scribe/recipes/grid/scribelectern.json` (new `recipes/grid/` dir).
- **No code change**: no `src/Core/` or `src/Mod/` C# is touched; recipes are data-only and
  auto-discovered at server startup.
- **No new dependencies**: references only vanilla `game:` items/blocks (planks, nails, parchment,
  leather, feather, dye, bowl) and the existing `scribe:scribelectern` block as output.
- **Vanilla version coupling**: relies on vanilla item codes and the `liquidContainerProps` grid
  mechanism as shipped in VS 1.22.x; a future vanilla rename of those codes would require an update.
