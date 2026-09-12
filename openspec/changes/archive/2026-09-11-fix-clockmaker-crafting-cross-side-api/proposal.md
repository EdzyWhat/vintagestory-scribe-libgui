## Why

Crafting a Clockmaker's Notebook from a Notebook silently loses the source document's title,
tasks, and History (including the "Crafted by" entry) when certain third-party mods are
installed (HoR Performance Optimizer + ImprovedHandbookRecipesFork). Eight rounds of live
Harmony diagnostics this session root-caused it: `ItemClockmakerNotebook.OnCreatedByCrafting`
guards its server-only copy-forward logic with `if (api.Side != EnumAppSide.Server) return;`,
reading `this.api` — the `CollectibleObject`'s own field. ImprovedHandbookRecipesFork has a
static-field caching bug (`Patch_InventoryCraftingGrid_FindMatchingRecipe._recipeSelector`,
cached once via `??=` with no side check) that can cross-wire a client-resolved `GridRecipe`/
`ItemStack.Collectible` into the server's authoritative crafting flow in integrated
singleplayer, so the server ends up invoking the hook on a Collectible instance whose own
`api` field is actually the client's `ICoreAPI`. The guard then reads `Client` and bails,
skipping the entire carryover. This already violates the `notebook-craft-carryover` spec's
existing requirements — it is a robustness bug, not a new behavior to specify.

## What Changes

- `ItemClockmakerNotebook.OnCreatedByCrafting` and `ConsumeCraftingIngredients` stop trusting
  `this.api`/`this.api.Side` to decide whether server-only logic should run. Both derive the
  side/API from the call's own context instead — `(outputSlot.Inventory as InventoryBase)?.Api`
  — which was proven correct (matching the actual invoking side) in every one of the 8 rounds
  of live diagnostic testing this session, regardless of what `this.api` reported.
- Remove all TEMP DIAGNOSTIC code added during root-causing: the whole
  `ScribeClockmakerCarryoverDiagnosticPatch.cs` file, and the diagnostic Harmony
  patch-owner dump / temp logging added to `ItemClockmakerNotebook.cs`,
  `ScribeModSystem.cs`, and `ScribeModSystem.ServerLifecycle.cs`.
- No behavior change on the golden path (no conflicting mods installed) — this only changes
  which signal decides "is this running server-side," not what happens once that's true.

## Capabilities

No capability's requirements are changing. `notebook-craft-carryover`
(`openspec/specs/notebook-craft-carryover/spec.md`) already specifies exactly this carryover
behavior; this change fixes an implementation bug that violates it under specific mods, it
does not add or alter any requirement. `skip_specs: true` is set in this change's
`.openspec.yaml` accordingly.

## Impact

- `src/Mod/ItemClockmakerNotebook.cs` — `OnCreatedByCrafting`, `ConsumeCraftingIngredients`.
- `src/Mod/ScribeClockmakerCarryoverDiagnosticPatch.cs` — deleted (diagnostic-only).
- `src/Mod/ScribeModSystem.cs`, `src/Mod/ScribeModSystem.ServerLifecycle.cs` — diagnostic
  additions reverted; no functional change to either file otherwise.
- No new dependencies, no network protocol changes, no persisted-data changes.
