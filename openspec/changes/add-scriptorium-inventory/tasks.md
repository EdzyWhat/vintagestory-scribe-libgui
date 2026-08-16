## 1. Block-entity inventory (server-authoritative substrate)

- [x] 1.1 Add a custom `ItemSlotScribeDocument : ItemSlot` whose `CanHold`/`CanTakeFrom` accept a
      stack only when `stack.Collectible is IScribeDocumentItem` (rejects all non-Scribe items).
      (Broadened after playtest 4.2 feedback to `is IScribeDocumentItem or BlockScribeWritingStation` so a
      picked-up Lectern/Scriptorium — which also carries a Scribe document on its stack — is storable too.
      See design.md D3.)
- [x] 1.2 Add an `InventoryGeneric` (2 slots, id `"scribescriptorium-" + Pos`) to
      `BlockEntityScriptorium`, populated with `ItemSlotScribeDocument` slots via the slot factory.
- [x] 1.3 In `Initialize`, call `Inventory.LateInitialize("scribescriptorium-" + Pos, api)` and set
      `Inventory.Pos = Pos` so `InvNetworkUtil` binds to the block-entity packet channel (the
      network-readiness gate — without it, slot clicks are silently dropped).
- [x] 1.4 Persist additively: call `Inventory.ToTreeAttributes(tree)` /
      `Inventory.FromTreeAttributes(tree)` in the BE's existing tree overrides, alongside the
      document keys. Confirm a pre-change Scriptorium (no inventory sub-tree) loads with empty slots.
      (Nested under a dedicated `"scriptoriumInventory"` sub-tree so the inventory's own
      `qslots`/`slots` keys never collide with the document/lock/guestbook keys.)
- [x] 1.5 Override `OnReceivedClientPacket(player, packetid, data)` mirroring
      `BlockEntityOpenableContainer`: forward `packetid < 1000` to
      `Inventory.InvNetworkUtil.HandleClientPacket(...)` + `MarkDirty(true)`; `1000` →
      `OpenInventory`, `1001` → `CloseInventory`.
- [x] 1.6 In `OnBlockBroken`, guarded by `Api is ICoreServerAPI`, call
      `Inventory.DropAll(Pos.ToVec3d().Add(0.5, 0.5, 0.5))` so stored Scribe items drop on break.

## 2. Dialog inventory tab (LibGUI slot widgets)

- [x] 2.1 Add `Inventory` to the private `ScribeLecternView` enum in `ScribeDialogBase` and an
      `IsInventoryView` flag (mirroring `IsVisitorsView`/`IsHistoryView`/`IsTimerView`).
- [x] 2.2 Change the Scriptorium dialog's base ctor call to the inventory-carrying
      `GuiDialogBlockEntityBase` ctor (pass `be.Inventory`) so `OpenInventory` /
      `CloseInventoryAndSync` fire automatically. Verify Lectern/Notebook/Tablet paths are
      unaffected (they never build slot widgets).
- [x] 2.3 Add `OnClickSwitchToInventory` in `ScribeDialogBase.ViewSwitching`, following the
      Guestbook/History teardown pattern (flush + release the editor lock before leaving if active).
      (Also added `RefreshInventoryView` + a base `BuildInventoryContent` placeholder, and wired the
      `BuildCentralRegion` dispatch case, mirroring the Timer/History tabs.)
- [x] 2.4 Add the inventory build method: create a `Gui.Widgets.Inventory.SlotController`,
      `WatchInventory(be.Inventory)`, and place a `SlotGrid`/two `FlatItemSlot`s for the two slots.
      (Overrode `BuildInventoryContent` in `GuiDialogScribeScriptorium`: a centered row over the
      inventory's slots, wired to a dialog-lifetime `SlotController` created lazily via `EnsureSlotController`.
      Later hand-built the row of `Stack`(`FlatItemSlot` + watermark) instead of a bare `SlotGrid` to carry
      the D7 empty-slot watermark — see 2.8.)
- [x] 2.5 Lifecycle: `UnwatchInventory` + `SlotController.Dispose()` in the view State's `Dispose`
      (per LibGUI's `InventoryPage` example) so the `SlotModified` handler doesn't leak. Do NOT
      manually `OpenInventory` (base + duplicate-open guard handle it).
      (Tied to `OnGuiClosed` rather than a per-view State, since the reconcile architecture re-runs the
      build every frame — a dialog-lifetime field disposed on close is the correct scope here.)
- [x] 2.6 Add the Scriptorium-only nav button in `GuiDialogScribeScriptorium.GetExtraNavButtons`
      (alongside the existing Guestbook button), with an active-color when `IsInventoryView`; pick an
      icon from the existing `TitleButton` set.
      (Registered a `scribeinventory` icon aliased to `book.svg`, mirroring `scribehistory`→`guestbook.svg`.)
- [x] 2.7 **Server-open fix (playtest 4.2/4.4 root cause).** Send `SendBlockEntityPacket(1000)` on dialog
      open when `Inventory != null`, so the server registers the inventory as OPEN for the player. Without
      it, LibGUI's base opens the inventory client-side only and never notifies the server, so
      `HandleActivateInventorySlot` can't resolve the target inventory id and silently drops every move —
      the item lived only in client optimism (gone on relog, nothing to `DropAll` on break). Added to
      `ScribeDialogBase.OnGuiOpened`, guarded by `Inventory != null` (no-op for document-only surfaces).
      See design.md D1.
- [x] 2.8 **Book watermark under each slot (D7 — feedback).** Each slot draws a full-opacity `scribebook`
      glyph (via `ScribeVsIconGlyph`) UNDERNEATH the real `FlatItemSlot` in a per-slot `Stack`; the slot's box
      fill is made transparent so the book shows through. Always present (not toggled on emptiness). Telegraphs
      "Scribe items only." Reworked from a first-cut over-layer that was invisible, blocked the slot centre,
      and flickered on top of a just-placed item — see design.md D7.

- [x] 2.9 **Creative-clone DocId collision fix (D8 — playtest).** Middle-click cloning a placed writing station
      carried the source's `DocId` onto the copy, colliding two live blocks in the DocId-keyed host/pin
      registries so the copy couldn't be opened. Fix: `OnBlockPlaced` mints a fresh id via
      `ScribeDocument.ReassignNewDocId()` when the restored id is still registered to a different live block
      (`ScribeModSystem.IsDocIdRegisteredToOtherBlock`); break→re-place is unaffected (source unregistered
      first). Shared base fix (also covers cloned Lecterns). See design.md D8.

## 3. Assets

- [x] 3.1 Add lang keys: the nav tab label/tooltip and any slot hint text.
      (`scribe-tab-inventory` tooltip + `scribe-gui-inventory-empty` slot-hint fallback.)
- [x] 3.2 Add a Handbook line to the Scriptorium entry describing the new inventory tab (update the
      "five tabs" wording in `handbook-scriptorium-views-text` to include it).
      (Now "six tabs"; added an "Item Storage" bullet between Guest Book and Settings.)

## 4. Verification

- [x] 4.1 `dotnet build` clean; `dotnet test` (Core suite) green — Core is untouched, so this is a
      regression guard, not new coverage. (Build: 0 warnings / 0 errors. Core.Tests: 375 passed.)
- [x] 4.2 Restage (`bash build/restage.sh Debug`), then manually test in-game: open a Scriptorium,
      switch to the inventory tab, and move a Notebook in and out — confirm the item actually moves
      (watch for any `[gui] Skipped slot activation … not network-ready` log; if seen, task 1.3 is
      wrong). (Confirmed: items move in/out and are held server-side — the diagnostic trace proved the
      server carries the stored items through save/unload. The root cause of the early "moves don't stick"
      failures was the missing server-open packet, fixed in 2.7.)
- [x] 4.3 Manually test the accept filter: confirm a non-Scribe item (e.g. a plank) is refused and a
      Tablet/Notebook/Clockmaker's Notebook is accepted. (Confirmed by playtester: non-Scribe items refused.)
- [x] 4.4 Manually test persistence: store an item, save+reload the world, confirm it's still in the
      slot; break the block, confirm the item drops with its document intact. (Relog retention confirmed —
      a stored Scriptorium reloaded `filled=2/2`. Break-drop confirmed by playtester 2026-08-16: breaking a
      Scriptorium with held items drops them as expected.)
- [ ] 4.5 Manually test open/close the tab repeatedly and re-open the dialog — confirm no leak /
      stale contents (WatchInventory rebuild works) and contents match across a second client viewing
      the same block.
- [x] 4.6 Confirm the tab appears ONLY on the Scriptorium (not Lectern/Notebook/Tablet). (Confirmed by
      playtester 2026-08-16: no regression — the Item Storage nav button shows only on the Scriptorium.
      Editor lock also confirmed unaffected.)
- [x] 4.7 **Creative-clone (2.9/D8).** Copy a Scriptorium that has stored items with middle-click, place
      the copy, then open BOTH the original and the copy — confirm both open normally and the original keeps
      its items. (Confirmed by playtester: clone no longer breaks interaction; watermark reads correctly as a
      faint parchment-veiled book.)
