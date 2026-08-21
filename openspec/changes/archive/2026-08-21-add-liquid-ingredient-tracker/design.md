## Context

Crafting Tasks (add-crafting-tasks, still in progress) turn a grid recipe into a parent "craft N of X"
Tracker plus one depth-1 Tracker child per counting ingredient. `ScribeCraftRecipeProbe.DeriveIngredients`
collapses the recipe's grid cells into a list of `ScribeCraftIngredient(ItemCode, PerCraftQuantity)` plus a
list of non-counting `Notes` (strings). `ScribeDocument.ReconcileCraftIngredients` then materializes each
ingredient as a child Tracker (target = `PerCraftQuantity × craftsNeeded`) and each note as a depth-1 Text
row, self-healing on every document open without persisting the ingredient list (only the parent's
`RecipeSignature` is persisted).

Liquids don't fit that pipeline today. `fix-recipe-variant-identity` added
`ScribeCraftRecipeProbe.TryAddLiquidNote`: it reads the recipe's containerized-liquid requirement — declared
on the recipe as `attributes.liquidContainerProps.requiresContent` (with a per-ingredient
`RecipeAttributes.requiresContent` fallback), NOT on a grid cell (the cell is the solid bowl/bucket) — and
emits a note naming the liquid. This change turns that note into a counting Tracker.

**The counting-model mismatch (the core problem).** A Tracker counts by summing `ItemStack.StackSize` of
carried stacks that satisfy its target (`ScribeTrackerCounter.CountCarried`, the "Tallybook" pattern). But a
liquid is never a loose carried stack — it is `WaterTightContainable` content stored *inside* a container
itemstack, measured in **litres** = `content.StackSize / ItemsPerLitre`. So a liquid Tracker must (a) resolve
the liquid's own item/block code, (b) sum litres across every carried container that holds it, and (c) define
what its integer target means. The user has agreed the target is **litres, rounded up (`ceil`)**.

### Decompiled ground truth (VSSurvivalMod.dll, `Vintagestory.GameContent`)

Confirmed by decompiling `/Applications/Vintage Story.app/Mods/VSSurvivalMod.dll`:

- **Recipe liquid requirement** (`BlockLiquidContainerBase.OnHandbookRecipeRender`, the authoritative
  reference): reads `RecipeAttributes["requiresContent"]` first, else recipe-level
  `Attributes["liquidContainerProps"]`, then off that object: `requiresContent.code` (liquid code, e.g.
  `game:blackdye`), `requiresContent.type` (`"item"` | `"block"`), and the **sibling** field
  `requiresLitres` (a `float`, NOT nested inside `requiresContent`, NOT named `quantity`/`litres`). It
  converts litres→stacksize via `StackSize = (int)(ItemsPerLitre * litres)`.
- **Read a carried container's content:** `BlockLiquidContainerBase.GetContent(ItemStack containerStack)` →
  the single content `ItemStack` (a normal stack whose `Collectible.Code` is the liquid). Backed by
  `BlockContainer.GetContents(world, itemstack)`, which reads the container itemstack's `"contents"` tree
  attribute. The content is a plain resolved `ItemStack`.
- **Litres of a content stack, statically:** `BlockLiquidContainerBase.GetContainableProps(ItemStack)` is
  **static** and returns the content collectible's `WaterTightContainableProps` off its own
  `waterTightContainerProps` attribute — no container instance needed. The field is
  `ItemsPerLitre` (`float`, default `1f`). `GetCurrentLitres` computes
  `(float)GetContent(stack).StackSize / contentProps.ItemsPerLitre`. Vanilla's own fallback when props are
  absent is `ItemsPerLitre = 1f`.

## Goals / Non-Goals

**Goals:**
- A liquid ingredient of a Crafting Task becomes a counting Tracker child with a litre-based, ceil-rounded
  target, counted from the viewer's carried liquid containers.
- Reuse the existing tracker recompute + HUD count engines unchanged (branch inside the shared counter).
- Keep all liquid-specific VS API in `src/Mod/`; keep `src/Core/` API-free and unit-testable.
- Degrade gracefully to the existing note when a liquid can't be resolved.

**Non-Goals:**
- No standalone (non-Craft) liquid Tracker: liquid trackers only ever arise as Crafting-Task children
  (that is the only place `requiresContent` is read). The New-Task item picker is unchanged.
- No new `ScribeBlockKind`, codec, or network message.
- No tracking of block-stored or placed-container liquids (carried inventory only, matching every Tracker).
- No sub-litre display precision: the have/need readout is in whole litres.

## Decisions

### D1 — Reuse `ScribeBlockKind.Tracker`; litres are a Mod-side counting detail
A liquid child is an ordinary `Tracker` block: `TargetItemCode` = the liquid's code (`game:blackdye`),
`TargetQuantity` = whole litres. **No new kind.** The "this target is a liquid, count litres not stacks"
decision is made at count time in the Mod layer by inspecting the resolved collectible's `MatterState`.

- *Why:* keeps Core, all codecs, persistence, and the network contract byte-identical. The unit "litres"
  needs no new field — the generic `TargetQuantity`/`CurrentQuantity` and the `N / M` row readout already
  read naturally as litres.
- *Alternative rejected:* a `ScribeBlockKind.LiquidTracker`. It would touch the enum (append-only, but still
  ripples into every codec + the reconcile/HUD/read paths) for no behavioral gain, since the count source is
  already discoverable from the resolved collectible.

### D2 — Resolve the liquid + litres from `requiresContent` + `requiresLitres`
`ScribeCraftRecipeProbe` reads `requiresContent.code`/`type` (already done by `TryAddLiquidNote`) and, new
here, the sibling `requiresLitres` float. It resolves the code to a concrete `ItemStack` (item vs block per
`type`) to confirm identity and get the display name. On success it emits a liquid
`ScribeCraftIngredient` carrying the liquid code + the per-craft litre amount; on failure (wildcard code,
`requiresLitres <= 0`, unresolvable stack) it appends the existing note instead (D6).

### D3 — Target = `ceil(litresPerCraft × craftsNeeded)`, computed in Core
Ceiling is applied to the **batch total**, not per craft. Ceiling per craft then multiplying would turn a
0.1 L × 10-craft recipe into `ceil(0.1) × 10 = 10` L instead of `ceil(1.0) = 1` L. Because the existing
`ReconcileCraftIngredients` scales children as `PerCraftQuantity × craftsNeeded` (integers), the litre amount
must bypass that integer multiply:

- `ScribeCraftIngredient` gains `bool IsLiquid` and `double LitresPerCraft` (defaulted, so existing
  solid-ingredient construction is unchanged).
- `ScribeCraftMath` gains a pure helper `LitreTarget(double litresPerCraft, int craftsNeeded)` =
  `max(1, ceil(litresPerCraft × craftsNeeded))`.
- `ReconcileCraftIngredients`: for a liquid ingredient, the child target = `LitreTarget(...)`; for a solid,
  the unchanged `PerCraftQuantity × craftsNeeded`. Child matching stays keyed on `TargetItemCode`.

*Core stays API-free:* `IsLiquid`/`LitresPerCraft`/the ceil helper are plain BCL (`double`, `Math.Ceiling`),
exactly like the existing `ScribeBrightnessCurve` math. **The litre value is never persisted** — only the
parent's `RecipeSignature` is; ingredients (and thus litres) re-derive from the live recipe on every open —
so there is no float-in-savegame concern, and the derived integer target is deterministic.

### D4 — Litre-summing carried scan lives in `ScribeTrackerCounter.CountCarried`
`CountCarried(player, ingredient)` keeps its `int` return and its two call sites (read-view engine + HUD)
unchanged. It branches on the resolved liquid:

```
if ingredient.ResolvedItemStack?.Collectible.MatterState == EnumMatterState.Liquid:
    litres = 0
    for each carried slot:
        if slot.Itemstack?.Collectible is BlockLiquidContainerBase container:
            content = container.GetContent(slot.Itemstack)
            if content != null && ingredient.SatisfiesAsIngredient(content, checkStackSize:false):
                ipl = BlockLiquidContainerBase.GetContainableProps(content)?.ItemsPerLitre ?? 1f
                litres += content.StackSize / ipl
    return (int)Math.Floor(litres + epsilon)     // epsilon ~1e-3 absorbs float error on exact multiples
else:
    <existing stacksize sum, byte-identical>
```

- Reusing `SatisfiesAsIngredient` on the **content** stack inherits the same exact/wildcard matching the
  solid path uses, so an attribute- or family-liquid still matches correctly.
- **Rounding is safe-pessimistic:** target is `ceil(required)`, have is `floor(carried)`. A player holding
  exactly the required whole litres reads satisfied (`floor(3.0)=3 ≥ ceil(3.0)=3`); a hair under never
  falsely completes. `CurrentQuantity` is uncapped, so overflow (`5 / 3`) still shows (feedback 7.14).
- *Why here:* both count engines already funnel through `CountCarried`, so the HUD gets liquid counting for
  free with zero change to `RecomputeTrackers`, `HudScribePins`, or any message.

### D5 — Resolution routing in `TryResolveIngredient`
The liquid code is concrete (`game:blackdye`), so it takes the existing concrete branch of
`TryResolveIngredient`, which calls `Resolve(...)` and sets `ResolvedItemStack` — making `MatterState`
available for D4's branch. No change to the wildcard/attribute-encoded branches. The per-code
ingredient cache is unchanged (a liquid resolves and caches like any other concrete code).

### D6 — Fallback to the note (graceful degrade)
`TryAddLiquidNote` is refactored into `TryAddLiquid(recipe, ingredients, notes, ...)`:
- Fully resolvable (concrete code, `requiresLitres > 0`, stack resolves) → add a liquid
  `ScribeCraftIngredient`.
- Otherwise → append the existing `scribe:scribe-gui-craft-liquid-note` Text note (today's behavior).

This preserves the "never crash, never mis-count" contract: an unrecognized liquid shows as an
informational note exactly as it does now.

## Risks / Trade-offs

- **[Fractional-litre display]** A recipe needing 0.1 L (ceil → target 1) shows `0 / 1` until the player
  carries ≥ 1 L (floor). → Acceptable and intended: the user chose whole-litre ceil rounding; sub-litre
  precision is a non-goal. Documented in the row's meaning.
- **[Multiple liquids in one recipe]** Vanilla recipe-level `liquidContainerProps` carries a single
  `requiresContent`, but a per-ingredient `RecipeAttributes.requiresContent` could appear more than once. →
  `TryAddLiquid` emits one ingredient per distinct liquid code and **merges** duplicate liquid codes by
  summing `requiresLitres` before handing off (ReconcileCraftIngredients expects distinct codes).
- **[Missing `waterTightContainerProps`]** A content stack with no props falls back to `ItemsPerLitre = 1f`
  (vanilla's own fallback), so litres degrade to a stacksize count rather than throwing. → Matches game
  behavior; harmless.
- **[Superseding a sibling requirement]** This reverses `fix-recipe-variant-identity`'s "liquids are noted"
  requirement while both changes are unarchived. → The delta is authored as ADDED under `craft-task` (the
  base spec is not yet archived), and the proposal states the supersession explicitly. If archived in the
  wrong order, reconcile per the known OpenSpec archive-order header-drift guidance (keep the superset body).
- **[Loose liquid items never count]** If a liquid also existed as a loose portion item in inventory, the
  D4 scan (containers only) would ignore it. → Correct: VS liquids are container-bound; a loose portion
  isn't the tracked resource.

## Migration Plan

Crafting Tasks are **unreleased** (in-progress `add-crafting-tasks`), so no shipped save carries a
liquid-note row — no data migration is required. `ReconcileCraftIngredients` never deletes rows, so any
stale liquid *note* row in a developer's in-progress test document persists harmlessly beside the new liquid
Tracker until manually removed; a fresh Craft task generates only the Tracker. No rollback concern beyond
reverting the code (the persisted model is unchanged either way).

## Open Questions

- **Does any survival recipe declare more than one `requiresContent` liquid, or a fractional
  `requiresLitres`?** The design handles both (merge-by-code; ceil-at-total), but the exact in-game
  distribution is unverified — worth a `.scribeprobe` sweep during the playtest task to confirm real recipes
  behave as expected (especially any sub-1-litre requirement's `ceil` and any block-`type` liquid).
