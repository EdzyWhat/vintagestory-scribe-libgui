## 1. Block-entity inventory (server-authoritative substrate)

- [ ] 1.1 Add a custom `ItemSlotScribeDocument : ItemSlot` whose `CanHold`/`CanTakeFrom` accept a
      stack only when `stack.Collectible is IScribeDocumentItem` (rejects all non-Scribe items).
- [ ] 1.2 Add an `InventoryGeneric` (2 slots, id `"scribescriptorium-" + Pos`) to
      `BlockEntityScriptorium`, populated with `ItemSlotScribeDocument` slots via the slot factory.
- [ ] 1.3 In `Initialize`, call `Inventory.LateInitialize("scribescriptorium-" + Pos, api)` and set
      `Inventory.Pos = Pos` so `InvNetworkUtil` binds to the block-entity packet channel (the
      network-readiness gate — without it, slot clicks are silently dropped).
- [ ] 1.4 Persist additively: call `Inventory.ToTreeAttributes(tree)` /
      `Inventory.FromTreeAttributes(tree)` in the BE's existing tree overrides, alongside the
      document keys. Confirm a pre-change Scriptorium (no inventory sub-tree) loads with empty slots.
- [ ] 1.5 Override `OnReceivedClientPacket(player, packetid, data)` mirroring
      `BlockEntityOpenableContainer`: forward `packetid < 1000` to
      `Inventory.InvNetworkUtil.HandleClientPacket(...)` + `MarkDirty(true)`; `1000` →
      `OpenInventory`, `1001` → `CloseInventory`.
- [ ] 1.6 In `OnBlockBroken`, guarded by `Api is ICoreServerAPI`, call
      `Inventory.DropAll(Pos.ToVec3d().Add(0.5, 0.5, 0.5))` so stored Scribe items drop on break.

## 2. Dialog inventory tab (LibGUI slot widgets)

- [ ] 2.1 Add `Inventory` to the private `ScribeLecternView` enum in `ScribeDialogBase` and an
      `IsInventoryView` flag (mirroring `IsVisitorsView`/`IsHistoryView`/`IsTimerView`).
- [ ] 2.2 Change the Scriptorium dialog's base ctor call to the inventory-carrying
      `GuiDialogBlockEntityBase` ctor (pass `be.Inventory`) so `OpenInventory` /
      `CloseInventoryAndSync` fire automatically. Verify Lectern/Notebook/Tablet paths are
      unaffected (they never build slot widgets).
- [ ] 2.3 Add `OnClickSwitchToInventory` in `ScribeDialogBase.ViewSwitching`, following the
      Guestbook/History teardown pattern (flush + release the editor lock before leaving if active).
- [ ] 2.4 Add the inventory build method: create a `Gui.Widgets.Inventory.SlotController`,
      `WatchInventory(be.Inventory)`, and place a `SlotGrid`/two `FlatItemSlot`s for the two slots.
- [ ] 2.5 Lifecycle: `UnwatchInventory` + `SlotController.Dispose()` in the view State's `Dispose`
      (per LibGUI's `InventoryPage` example) so the `SlotModified` handler doesn't leak. Do NOT
      manually `OpenInventory` (base + duplicate-open guard handle it).
- [ ] 2.6 Add the Scriptorium-only nav button in `GuiDialogScribeScriptorium.GetExtraNavButtons`
      (alongside the existing Guestbook button), with an active-color when `IsInventoryView`; pick an
      icon from the existing `TitleButton` set.

## 3. Assets

- [ ] 3.1 Add lang keys: the nav tab label/tooltip and any slot hint text.
- [ ] 3.2 Add a Handbook line to the Scriptorium entry describing the new inventory tab (update the
      "five tabs" wording in `handbook-scriptorium-views-text` to include it).

## 4. Verification

- [ ] 4.1 `dotnet build` clean; `dotnet test` (Core suite) green — Core is untouched, so this is a
      regression guard, not new coverage.
- [ ] 4.2 Restage (`bash build/restage.sh Debug`), then manually test in-game: open a Scriptorium,
      switch to the inventory tab, and move a Notebook in and out — confirm the item actually moves
      (watch for any `[gui] Skipped slot activation … not network-ready` log; if seen, task 1.3 is
      wrong).
- [ ] 4.3 Manually test the accept filter: confirm a non-Scribe item (e.g. a plank) is refused and a
      Tablet/Notebook/Clockmaker's Notebook is accepted.
- [ ] 4.4 Manually test persistence: store an item, save+reload the world, confirm it's still in the
      slot; break the block, confirm the item drops with its document intact.
- [ ] 4.5 Manually test open/close the tab repeatedly and re-open the dialog — confirm no leak /
      stale contents (WatchInventory rebuild works) and contents match across a second client viewing
      the same block.
- [ ] 4.6 Confirm the tab appears ONLY on the Scriptorium (not Lectern/Notebook/Tablet).
