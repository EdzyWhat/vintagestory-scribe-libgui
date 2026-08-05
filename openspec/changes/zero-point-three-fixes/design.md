## Context

Two independent defects from the 0.3 tablet playtest, bundled into one `zero-point-three-fixes` change.

**Schematic recipe.** `add-clockmaker-notebook-schematic` added a second recipe for the Clockmaker's
Notebook — the same ingredients as the trait-gated recipe plus a reusable `scribe:clockmakerschematic`
(`consume: false`) — so non-Tinkerer players can craft it with a purchased blueprint. The recipe was
authored as a 1×4 row (`ingredientPattern: "BGMS"`, `width: 4, height: 1`). The vanilla crafting grid is
3×3 (`GridRecipe.Width`/`Height` default 3; no shipped grid recipe is 4-wide, confirmed by scanning
`assets/survival/recipes/grid/`). A 4-wide recipe can't be laid out in the grid, so it never crafts —
and the survival handbook's `CollectibleBehaviorHandbookTextAndExtraInfo.addCreatedByInfo`, which
enumerates every grid recipe producing the page's stack, skips/hides it too. That single defect produces
both playtest symptoms: the schematic craft fails (`e093c2ad`) and the handbook shows only the trait
recipe with no schematic path and no trait asterisk.

**Handbook dual display.** The player wanted the Sling's presentation: multiple recipes on one entry,
with a `* Requires <trait> trait` asterisk on the gated one. That rendering is entirely automatic —
`addCreatedByInfo` draws one "Created by" grid per matching recipe and appends the `gridrecipe-requirestrait`
lang note (`"* Requires {0} trait"`) for any recipe carrying `requiresTrait`. So once the schematic recipe
is a valid grid recipe, both grids appear with the asterisk on the trait one, with no custom handbook code.

**Quench rehydration.** `ItemScribeTablet` already softens a hard tablet back to wet on passive water
exposure: `OnGroundIdle` (dropped item swimming) and `OnHeldIdle` (holder swimming) both call the private
`Soften(stack, world)`, which swaps the `clay-<c>-hard` variant to its `clay-<c>` sibling, carries the
document/history via `CarryStackData`, and omits `transitionstate` so the dry-out clock re-seeds. The
gesture players expect — crouch + right-click a water container, like quenching hot metal — does not
exist. Vanilla's quench is passive (per-tick `ICoolingMedium.CoolNow` when a hot block sits under liquid),
so there is no gesture to inherit; we author the trigger and reuse `Soften` unchanged.

## Goals / Non-Goals

**Goals:**
- Make the schematic recipe craftable by fitting it in the 3×3 grid.
- Get both recipes (schematic + trait-gated) onto the Clockmaker's Notebook handbook as separate grids,
  the trait one asterisked — via recipe validity alone, no handbook code.
- Add a deliberate crouch + right-click-a-water-container quench that softens a hard tablet, reusing the
  existing `Soften` machinery, additive to the two existing passive paths.

**Non-Goals:**
- No change to ingredients, output, trait gating, or trader availability of either recipe.
- No `recipegroup` grouping (recipes stay separate grids, not a cycling entry).
- No temperature/`ICoolingMedium` model on the tablet — the quench is a state swap, not a thermal sim.
- No change to the passive drop-in-water / swim-while-holding paths (kept as-is).
- No new water-consumption cost (dipping doesn't drain the container) unless a later fix revisits it.

## Decisions

**D1 — Reshape the schematic recipe to 2×2, not 3×2 or a 3×3 cross.**
`ingredientPattern: "BGMS"` becomes `width: 2, height: 2` (`"BG"` / `"MS"`, or any 2×2 arrangement).
2×2 is the tightest layout that fits all four ingredients in the grid and reads as a compact block.
*Alternatives:* 3×2 with two empty cells (wastes space, and empty cells need the pattern's `_` filler,
which is more error-prone); a 3×3 arrangement (overkill). 2×2 is the minimal correct fix.

**D2 — Keep the two recipes ungrouped (no shared `recipegroup`).**
Per the product decision, the handbook should show both paths as distinct "Created by" grids rather than
one cycling entry. `addCreatedByInfo` does this by default when recipes lack a shared `recipegroup`.
*Alternative:* Sling-style `recipegroup` collapses alternates into one cycling grid — tidier but hides
that there are two genuinely different acquisition paths (trait vs. blueprint), which is the point.

**D3 — Quench trigger lives in `OnHeldInteractStart`, gated on ShiftKey + a water-container `blockSel`.**
The tablet's `OnHeldInteractStart` already branches on `byEntity.Controls.ShiftKey` to pass through to
`GroundStorable` placement. The quench must run on the SAME shift+right-click, so it takes precedence
ONLY when the aimed-at block is a water-holding container; otherwise it falls through to the existing
shift-passthrough (so ground-storage placement is unaffected when not aiming at water). Detection:
`blockSel is not null` → `world.BlockAccessor.GetBlock(blockSel.Position)` is a `BlockLiquidContainerBase`
whose `GetContent(blockSel.Position)` is a water portion (check the content stack's
`WaterTightContainableProps`/code, or `Collectible.GetCollectibleInterface<ICoolingMedium>()?.CanCool(...)`).
*Alternative:* a separate non-shift right-click — rejected because plain right-click already opens the
dialog, and reusing the crouch gate matches the metal-quench muscle memory the player asked for.

**D4 — Server-authoritative swap, client-side splash/sizzle feedback.**
`OnHeldInteractStart` fires on both sides. Mirror the existing idle-soften convention: do the
`Soften`→`slot.Itemstack = softened`→`slot.MarkDirty()` on the server only; set
`handling = EnumHandHandling.PreventDefault` so the container's own fill/pour interaction doesn't also
fire; play a splash/sizzle sound (and optional particles) on the client for feedback. Only act when
`ReadHard(stack)` is true — a wet tablet (already editable) and a fired tablet (permanent) both no-op,
so the gesture is inert on anything but a hard tablet, exactly like `Soften`'s own guard.

**D5 — No `handleLiquidContainerInteract` attribute needed (verify in-game).**
Vanilla routes a held item's interaction to the container's `OnBlockInteractStart` first, and only back
to the held item's `OnHeldInteractStart` when the collectible sets `Attributes.handleLiquidContainerInteract:
true`. Since we crouch (ShiftKey), the container's fill/pour path is typically suppressed and the held
handler runs — but if playtest shows the container swallowing the crouch-right-click, add that attribute
to `scribetablet.json`. Left out initially to avoid over-configuring; called out as the first fallback.

## Risks / Trade-offs

- **[Container interaction precedence]** The bucket/barrel's own crouch-right-click behavior might win over
  the tablet's handler, so the quench appears to do nothing. → D5: fall back to the
  `handleLiquidContainerInteract` attribute; this is an in-game verification point, not a code unknown.
- **[Shift-passthrough collision]** Quench shares the crouch gesture with ground-storage placement. →
  D3 gates quench strictly on aiming at a water container; every other crouch-right-click still falls
  through to the existing `GroundStorable` branch unchanged.
- **[Archive-order header drift]** This change's deltas MODIFY requirements that
  `add-clockmaker-notebook-schematic` and `add-tablet-firing-mechanic`/`wire-tablet-clay-art-and-variants`
  introduce and haven't archived yet. If archived first, the delta headers won't locate their target. →
  Archive this change AFTER those; match the canon header wording they establish (documented lesson).
- **[Water detection breadth]** Different containers (bucket, barrel, tureen) expose contents slightly
  differently. → Detect via the shared `BlockLiquidContainerBase.GetContent` / `WaterTightContainableProps`
  base API rather than per-block casts, so any water-holding liquid container works uniformly.
- **[No water cost]** Dipping doesn't consume water, so a single bucket rehydrates infinitely. Accepted:
  matches the low-stakes reversibility the feature is for; a cost can be a later 0.3 fix if desired.
