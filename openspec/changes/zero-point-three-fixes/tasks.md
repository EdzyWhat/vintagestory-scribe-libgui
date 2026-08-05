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
