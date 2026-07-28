## Context

The Lectern block `scribe:scribelectern` is registered and placeable but creative-only — the mod
has no `recipes/` directory at all. Vintage Story grid recipes are pure JSON assets that the vanilla
`RecipeLoader` (in VSSurvivalMod) discovers at server startup by scanning `recipes/grid` across
*every* loaded mod domain (it calls `GetMany(..., "recipes/grid", null)` with a null domain filter),
so a file dropped under `assets/scribe/recipes/grid/` is picked up with no C# registration. This
keeps the change data-only and consistent with the mod's "vanilla API only, thin adapter" discipline.

The only non-obvious part is the ink: "1 L of black dye in a bowl." Black dye is a liquid portion
(`game:dye-black`, `ItemLiquidPortion`, 100 items/L), not a countable grid item.

## Goals / Non-Goals

**Goals:**
- One survival grid recipe that outputs a single Lectern from planks + nails + parchment + feather +
  plain leather + a bowl of black dye.
- Consume the ink as a liquid-in-container ingredient, consuming the bowl.
- Ship as a single JSON asset, no code.

**Non-Goals:**
- No "any dye color" ink (see Decisions — deliberately black-only).
- No barrel/multi-step crafting path.
- No change to the placed block's document, persistence, or pin behavior (owned by `lectern-block`).
- No new handbook copy beyond what the auto-generated recipe entry provides.

## Decisions

### Liquid ink via `attributes.liquidContainerProps`, not a grid cell or barrel recipe
A grid recipe cannot consume a liquid by litres in a cell — verified that zero `recipes/grid/*`
files use `litres`; that field is barrel-only. The vanilla mechanism for "a filled container is an
ingredient" is a recipe-level `attributes.liquidContainerProps` block, exactly as
`assets/survival/recipes/grid/inkandquill.json` does. A `bowl-*-fired` occupies a normal grid slot,
and the block declares:
```
attributes: { liquidContainerProps: {
  requiresContent: { type: "item", code: "game:dye-black" },
  requiresLitres: 1,
  consumeContainer: true
}}
```
*Alternative considered:* a two-step craft (grid-assemble an undyed lectern, then dye it in a
barrel). Rejected — barrel-dyeing a furniture block is unusual in vanilla, doubles the assets, and
complicates the handbook flow for no benefit.

### Ink is black-only (single recipe entry), not "any ink"
"Any ink" was requested but is not expressible as a wildcard here. `requiresContent` is matched by
`JsonItemStack.Matches` → `Resolve()` + `.Equals` (decompiled `BlockLiquidContainerBase.
MatchesForCrafting`, VSSurvivalMod.dll). A wildcard `dye-*` there fails to resolve, and the code
falls back to matching that ignores the liquid entirely — so *any* bowl (even empty) would craft the
block, a silent correctness bug. True "any ink" would require ~11 near-identical entries (one per dye
color). Decision: keep it black-only, matching the `inkandquill` precedent; revisit as a multi-entry
array later if desired.

### Shaped 3×3 with a fixed "writing desk" layout
```
F R L        F = feather (game:feather)
P P P        R = parchment (game:paper-parchment)
B P N        L = plain leather (game:leather-normal-plain)
             P = plank, any wood (game:plank-*)  -> 4 planks
             B = bowl of ink (game:bowl-*-fired + liquidContainerProps)
             N = nails, any metal (game:metalnailsandstrips-*)
```
Shaped (not shapeless) gives a recognizable, deliberate arrangement for a furniture block.

### Variant tolerance: any wood / any metal / plain leather
`plank-*` and `metalnailsandstrips-*` accept any wood/metal (as vanilla furniture like `chest` does),
lowering the material barrier. Leather is pinned to `leather-normal-plain` to keep it unambiguous
(dyed leathers read as a different, more finished material). Output pins to the single-variant block,
so no `{name}` substitution is needed.

### Reference vanilla codes with explicit `game:` prefix
Unqualified codes in a `scribe`-domain recipe resolve against `scribe`; vanilla ingredients must be
`game:`-prefixed (matching how `inkandquill` references vanilla items). Output uses `scribe:scribelectern`.

## Risks / Trade-offs

- **Vanilla code drift** → If a future VS version renames `dye-black`, `plank`, `metalnailsandstrips`,
  `paper-parchment`, `leather-normal-plain`, or the `liquidContainerProps` schema, the recipe breaks
  silently (recipe just won't resolve). Mitigation: codes verified against the installed 1.22.x assets;
  in-game craft test is the acceptance gate.
- **Silent liquid-match fallback** → If `requiresContent` were mis-authored (typo/wildcard), the
  recipe would match any bowl. Mitigation: pin the exact `game:dye-black` code; the spec's
  empty/wrong-bowl scenario is an explicit test.
- **Recipe not auto-loading** → If the new `recipes/grid/` path or JSON is malformed, the recipe
  silently won't register. Mitigation: watch the server log for recipe-load warnings on first launch;
  verify the recipe appears in the handbook.

## Migration Plan

Additive and data-only. Deploy by shipping the new asset in the mod zip; no data migration. Rollback
is deleting the single JSON file (the block reverts to creative-only, its prior state).

## Open Questions

- None blocking. Future option: expand to multi-color ink via an array of per-dye entries if players
  want "any ink."
