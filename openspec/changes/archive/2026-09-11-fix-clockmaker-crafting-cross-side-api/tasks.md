## 1. Fix the side-detection bug

- [x] 1.1 In `src/Mod/ItemClockmakerNotebook.cs`, replace `OnCreatedByCrafting`'s
  `if (api.Side != EnumAppSide.Server) return;` with a local
  `var craftApi = (outputSlot.Inventory as InventoryBase)?.Api; if (craftApi is not
  ICoreServerAPI sapi) return;`, and route every subsequent API access in the method
  (`.World`, `.World.Logger`, `.World.PlayerByUid`, `.World.Calendar`) through that local
  `sapi` instead of `this.api`/a cast of it. Verify `dotnet build -c Release` succeeds with
  0 warnings/errors.

## 2. Remove diagnostic code

- [x] 2.1 Delete `src/Mod/ScribeClockmakerCarryoverDiagnosticPatch.cs` in full. Verify the
  project still builds after the file is removed and its `.csproj` include (if explicitly
  listed) is cleaned up.
- [x] 2.2 In `src/Mod/ItemClockmakerNotebook.cs`, remove all `TEMP DIAGNOSTIC
  (clockmaker-carryover-loss)` log calls and comments from `OnCreatedByCrafting`, and delete
  the diagnostic-only `ConsumeCraftingIngredients` override entirely (it carries no production
  logic). Verify by grepping the file for `TEMP DIAGNOSTIC` and confirming zero matches.
- [x] 2.3 In `src/Mod/ScribeModSystem.cs` and `src/Mod/ScribeModSystem.ServerLifecycle.cs`,
  remove the diagnostic patch hookup calls (`ScribeClockmakerCarryoverDiagnosticPatch.ApplyTo`,
  `ScribeGenerateOutputStackDiagnosticPatch.ApplyTo`, `ScribeOnCreatedByCraftingDiagnosticPatch
  .ApplyTo`) and any startup Harmony-patch-owner dump added for this investigation. Verify by
  grepping both files for `clockmaker-carryover-loss` and `DiagnosticPatch` and confirming zero
  matches.
- [x] 2.4 Run `dotnet build -c Release` and confirm 0 warnings/errors with all diagnostic code
  removed.

## 3. Verify the fix

- [x] 3.1 Restage the built mod (`build/restage.sh`, client not running) into a local install
  with HoR Performance Optimizer and ImprovedHandbookRecipesFork both installed — the exact
  modpack that reproduced the bug this session.
- [x] 3.2 Manually verify the `notebook-craft-carryover` spec's scenarios: craft a Clockmaker's
  Notebook from a Notebook that has a title, several tasks (some done), and existing History
  entries. Confirm the crafted Clockmaker's Notebook opens with the same title, the same tasks
  in the same order and done-state, the same `DocId`, and a History that shows all prior
  entries plus a new "Crafted" entry naming the crafting player.
- [x] 3.3 Manually verify the fresh-document scenario still holds: crafting/obtaining a
  Clockmaker's Notebook with no source document present yields an empty document with a fresh
  `DocId`, unchanged from today's behavior.
