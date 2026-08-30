## 1. Redesign carried-inventory scope in ScribeModSystem.History.cs

- [x] 1.1 Replace `CarriedInventoryClasses` with `DeniedInventoryClasses = { GlobalConstants.creativeInvClassName, GlobalConstants.groundInvClassName }` and change `FindCarriedNotebooks`'s filter to `inv is InventoryBasePlayer && !DeniedInventoryClasses.Contains(inv.ClassName)`
- [x] 1.2 Rewrite the doc-comments on the old allow-list and on `FindCarriedNotebooks` to describe the new type-check + denylist rationale (the old comment's craftinggrid-exclusion language is now stale)
- [x] 1.3 Manually verified in a local game session: put a Notebook in the crafting grid, trigger a death or storm, confirm a history entry is now recorded
- [x] 1.4 Manually verified in a local game session: open an unrelated chest containing a Notebook (dialog open, not carried), trigger a death or storm, confirm NO history entry is recorded on that chest's notebook (regression check against the transiently-opened-container risk)

## 2. Spike: confirm CarryOn's frozen-attribute shape before building the detection path

- [x] 2.1 Confirmed via decompilation/source-reading instead of a live session (no CarryOn install available headlessly): vanilla `InventoryBase.SlotsToTreeAttributes` nests slots as an individually-keyed `"slots"` sub-tree of `ItemstackAttribute` leaves (not a `TreeArrayAttribute`), itself nested one level deeper under an `"inventory"` key by `BEContainer`'s `InWorldContainer` wrapper — confirming a fully generic recursive walk (no hardcoded key names) is required and sufficient
- [x] 2.2 Confirmed via decompiled `AttachedCarriedBlock`: it delegates `BlockEntityData`/`ItemStack`/etc. straight to its own nested `CarriedBlock`, which can itself have further `AttachedBlocks` — recursion must be genuinely recursive, not one level

## 3. Build the CarryOn reflection bridge

- [x] 3.1 Added `src/Mod/CarryOnBridge.cs`: checks `IsModEnabled("carryon")`, resolves the manager via `GetModSystem("CarryOn.CarryOnLib.CarryOnLibSystem")` + reflected `CarryManager` property, exposes `FindCarriedNotebooks(Entity)`
- [x] 3.2 Every reflection call wrapped in try/catch; first failure logs once (`Disable(reason)`) and marks the bridge inactive for the rest of the session
- [x] 3.3 Generic recursive `ITreeAttribute` walker (`WalkTree`) — vanilla types only, no reflection; finds any nested `ItemstackAttribute` whose resolved `Collectible is IScribeDocumentItem`
- [x] 3.4 Wired via new `IHistoryRecordable` interface + `FindAllCarriedNotebookRecords` helper into the Death/PvpKill/TemporalStorm (and BossKill) recording paths in `ScribeModSystem.History.cs`
- [x] 3.5 Write-back implemented: `ITreeAttribute.SetItemstack` on the parent tree, then reflected `SetCarried(entity, rootCarriedBlock, null, true)` via `CarriedNotebookRef.FlushHistory()`

## 4. Verify end-to-end

- [x] 4.1 Manual/local verification: carry a chest with a Notebook inside, trigger a death and a storm, retrieve the notebook afterward and confirm both history entries appear
- [x] 4.2 Manual/local verification: with CarryOn NOT installed, confirm history recording behaves exactly as before this task group, with no error or delay
- [x] 4.3 Core test suite (559 tests) and local Atlas integration suite (25 tests, real VS 1.22.7 install) both green via `build/verify.sh Debug --no-restage`

## 5. Docs

- [x] 5.1 Added two entries to `VSAPI-NOTES.md`: `InventoryBasePlayer` as the engine's "on-player" boundary, and the `GetModSystem(string)` + small-reflected-surface pattern for soft mod integration
- [x] 5.2 Updated `CHANGELOG.md`'s `[Unreleased]` section with the bug fix and the new CarryOn capability

All 4 manual scenarios (1.3, 1.4, 4.1, 4.2) confirmed passing by the mod author, 2026-08-30.
