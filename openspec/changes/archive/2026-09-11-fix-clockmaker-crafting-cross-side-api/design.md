## Context

`ItemClockmakerNotebook.OnCreatedByCrafting` (`src/Mod/ItemClockmakerNotebook.cs:79-155`) does
its server-only copy-forward work behind `if (api.Side != EnumAppSide.Server) return;`, where
`api` is `this.api` — the field `CollectibleObject` assigns exactly once, non-virtually, via
the engine's `OnLoadedNative` when the item's prototype is registered. See proposal.md for how
eight rounds of live Harmony diagnostics this session proved that field can end up holding the
*client's* `ICoreAPI` even while the method is genuinely being invoked from the server's own
crafting flow, under ImprovedHandbookRecipesFork's static-caching bug. Every diagnostic capture
also showed a reliable alternative signal available at the exact same call site:
`(outputSlot.Inventory as InventoryBase)?.Api`, derived from the crafting grid's own inventory
rather than the Collectible prototype, which reported the correct invoking side in all 8 test
rounds regardless of what `this.api` reported.

`ConsumeCraftingIngredients` on the same class currently exists only as a TEMP DIAGNOSTIC
override with no production logic (memory: `ConsumeCraftingIngredients` was empirically
falsified as a workaround location — it failed to fire server-side identically to
`OnCreatedByCrafting` in the same captures, so it was never a viable alternate hook).

## Goals / Non-Goals

**Goals:**
- Make the server-only branch of `OnCreatedByCrafting` resilient to a `CollectibleObject`
  instance whose own `api` field does not reflect the side actually invoking the method.
- Remove all TEMP DIAGNOSTIC code added while root-causing this bug.

**Non-Goals:**
- Fixing ImprovedHandbookRecipesFork's underlying static-caching bug (out of Scribe's control;
  upstream reporting was explicitly deferred this session).
- Auditing every other `CollectibleObject` override in this codebase for the same `this.api`
  assumption. This change fixes the one confirmed, user-reported instance
  (`ItemClockmakerNotebook.OnCreatedByCrafting`). A broader audit is a separate, future concern
  if more instances turn up.
- Changing anything about `ConsumeCraftingIngredients` beyond deleting its diagnostic override
  — it carries no production logic today and this change doesn't add any.

## Decisions

**Derive the side from `outputSlot.Inventory`'s API, not `this.api`.** Replace
`if (api.Side != EnumAppSide.Server) return;` with a local `var craftApi =
(outputSlot.Inventory as InventoryBase)?.Api; if (craftApi is not ICoreServerAPI sapi) return;`
and use `sapi` (not `this.api`/a cast of it) for the rest of the method — `sapi.World`,
`sapi.World.Logger`, `sapi.World.PlayerByUid`, `sapi.World.Calendar`. This is a pure
call-site swap: every diagnostic round already proved this signal is correct, and
`outputSlot.Inventory` is guaranteed non-null and server-owned for a real server-side craft
(it's the actual `InventoryCraftingGrid` driving the call). No new dependency, no change to
what runs once the side check passes.

*Alternative considered*: keep `this.api` for the World/Logger calls and only use the
inventory-derived API for the boolean side check. Rejected — if `this.api` can be the wrong
`ICoreAPI` object, everything derived from it (its `World`, its `Logger`) is equally suspect
(this is exactly what test8 showed: logging through the wrong-side reference produced no
visible output). Once the side check needs a trustworthy source, the method should get *all*
its API access from that same trustworthy source, not mix the two.

*Alternative considered*: patch `InventoryCraftingGrid.FindMatchingRecipe`/`ConsumeIngredients`
directly (as the diagnostic patches did) instead of fixing the override. Rejected — the
existing `OnCreatedByCrafting` override is the vanilla, documented extension point and the fix
here is a one-line signal swap; reaching for an external Harmony patch to work around a
one-line problem would add permanent complexity for no benefit.

**Delete all TEMP DIAGNOSTIC code.** `ScribeClockmakerCarryoverDiagnosticPatch.cs` (the whole
file — all three diagnostic patch classes), the `OnCreatedByCrafting`/`ConsumeCraftingIngredients`
temp logging in `ItemClockmakerNotebook.cs`, and the diagnostic hookup/patch-owner-dump code in
`ScribeModSystem.cs` / `ScribeModSystem.ServerLifecycle.cs`. This diagnostic code already did
its job (root-causing the bug); keeping it around has no further value and each of those
Harmony patches is an extra, permanent point of fragility (per
[[harmony-attribute-patch-gotchas]]) that this project doesn't want to carry into a release.

## Risks / Trade-offs

- **[Risk]** `outputSlot.Inventory` could theoretically be null or not an `InventoryBase` in
  some exotic call path this session's diagnostics didn't cover, causing the method to
  silently no-op instead of running. → **Mitigation**: this mirrors exactly what the
  diagnostic patches already did successfully in every one of 8 test rounds, including the
  buggy modpack — no call path was observed where this signal was unavailable at this call
  site. If it's ever null on a legitimate server craft, the current behavior (skip the
  copy-forward) is the same "fails safe" outcome as today's bug, not a regression.
- **[Risk]** This fixes only the one confirmed call site; another `CollectibleObject` override
  elsewhere in Scribe could have the same latent assumption. → **Mitigation**: explicitly
  scoped out as a non-goal above; no other instance has been reported, and speculatively
  auditing the whole codebase for a hypothetical problem is out of scope for a bug-fix change.

## Migration Plan

No data migration. This is a same-process code change: build, restage, and manually verify
against the exact repro modpack (HoR Performance Optimizer + ImprovedHandbookRecipesFork
installed) that crafting a Clockmaker's Notebook from a titled Notebook with tasks and history
now carries all three forward, per `notebook-craft-carryover`'s existing spec scenarios. No
rollback concerns beyond reverting the commit — no persisted state changes shape.
