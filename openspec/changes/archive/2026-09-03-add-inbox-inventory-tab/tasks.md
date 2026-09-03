## 1. Inbox block inventory container (design.md D1)

- [x] 1.1 Add an 8-slot `InventoryGeneric` to `BlockEntityInbox`, mirroring
  `BlockEntityScriptorium`'s lazy-construction pattern (`EnsureInventory`, a public `Inventory`
  getter, `Initialize` calling `LateInitialize` + setting `Pos`). Slot factory returns
  `new ItemSlotTaskNotice(self)` for indices 0-3 and `new ItemSlot(self)` for indices 4-7.
  Verify: `dotnet build src/Mod/Mod.csproj -c Debug` succeeds with 0 warnings/errors.
- [x] 1.2 Persist the inventory under its own sub-tree key (e.g. `"inboxInventory"`) in
  `ToTreeAttributes`/`FromTreeAttributes`, additive to the existing document/lock keys. Verify:
  place an Inbox block, put items in slots, save-and-reload the world (or restart the server),
  confirm the same items remain in the same slots.
- [x] 1.3 Confirm a pre-existing (pre-change) placed Inbox block still loads successfully with all
  8 inventory slots empty. Verify: load a world saved before this change (or a save with the
  sub-tree key stripped) and check the Inbox Inventory tab shows 8 empty slots, no error/crash.
- [x] 1.4 Override `OnBlockBroken` on `BlockEntityInbox` to drop the inventory's contents via
  `Inventory.DropAll(...)`, matching `BlockEntityScriptorium.OnBlockBroken` exactly. Verify: place
  items in the inventory, break the block, confirm the items drop as recoverable ItemStacks.
- [x] 1.5 Wire the block-entity packet channel handling (`OnReceivedClientPacket`-style dispatch,
  `InvNetworkUtil.HandleClientPacket`, `OpenInventory`/`CloseInventory` on dialog open/close) for
  the new inventory, matching `BlockEntityScriptorium`'s wiring. Verify: dragging an item into and
  out of a slot updates on both a hosting server and a connected client (or single-player,
  confirm no "not network-ready" log warning per `VSAPI-NOTES.md`/prior slot-wiring notes).

## 2. Shared slot-style helper (design.md D2)

- [x] 2.1 Extract the Assignment Desk's `BuildNoticeSlot` slot-styling values (`SlotSize`,
  `WatermarkScale`, veil-color formula, watermark-glyph `Stack` construction) into a shared
  static helper taking `ColorScheme`, an optional watermark icon name, and the underlying
  `ItemSlot`/`SlotController`, returning the styled `Widget`. Verify: `dotnet build` succeeds;
  the Assignment Desk's own Notice slots render visually unchanged in-game (manual check).
- [x] 2.2 Confirm the helper supports a no-watermark call path (open slots) that renders the same
  size/border/background with no glyph overlay. Verify: unit-level code review (no automated
  visual test available) confirms the helper's watermark parameter is genuinely optional and
  short-circuits the `Stack`/glyph construction when absent.

## 3. Inbox/Inbox Inventory tab switching (design.md D3)

- [x] 3.1 Add an `IsInboxInventoryView` flag and `OnClickSwitchToInboxInventory()` /
  updated `OnClickSwitchToInbox()` pair scoped to `GuiDialogScribeInbox`, following the existing
  `IsAssignmentView`/`OnClickSwitchToAssignment` pattern in `ScribeDialogBase.ViewSwitching.cs`.
  Verify: `dotnet build` succeeds; clicking each nav button switches `IsInboxInventoryView`/the
  Inbox view flag correctly (log or breakpoint check acceptable given no GUI test harness).
- [x] 3.2 Update `GuiDialogScribeInbox.BuildRightColNav()` to render three buttons — Inbox, Inbox
  Inventory, Settings — using the Assignment Desk's existing button spacing/sizing constants
  (`NavButtonSize`, `spacing: 16`). Verify: in-game, all three nav buttons render without crowding
  or overlap at the default window size.
- [x] 3.3 Update `GuiDialogScribeInbox`'s class doc-comment (currently states the dialog
  "permanently stays on" the Inbox tab and has "no other tab") to reflect the new second tab.
  Verify: doc-comment no longer contradicts the implemented behavior (read-through check).

## 4. Inbox Inventory tab content (specs/inbox-inventory, design.md D4)

- [x] 4.1 Build the Inbox Inventory tab's content widget: 8 slots via the shared slot-style
  helper (task 2.1), arranged 2 rows of 4, centered horizontally and vertically within the tab's
  content region, rendered when `IsInboxInventoryView` is active. Verify: in-game, opening the
  tab shows 8 slots in a centered 2×4 grid.
- [x] 4.2 Confirm the 4 restricted slots use the watermark icon (matching the Assignment Desk's
  Task Notice hint) and reject any non-Task-Notice item; confirm the 4 open slots show no
  watermark and accept any item. Verify: in-game, attempt to place a Notebook into a restricted
  slot (rejected) and into an open slot (accepted); place a Task Notice into a restricted slot
  (accepted) and into an open slot (accepted).
- [x] 4.3 Confirm the tab's slot grid renders within the Inbox block's existing W × 1.2W bounding
  box with no new `IScribeDocumentHost.GetLayout` dimension change. Verify: in-game, the dialog's
  overall size when the Inbox Inventory tab is active matches its size when the Inbox tab is
  active.

## 5. Localization

- [x] 5.1 Add lang keys for the tab label/tooltip ("Inbox Inventory") to
  `src/Mod/assets/scribe/lang/en.json`, following the existing `scribe-tab-inbox` naming
  convention (e.g. `scribe-tab-inbox-inventory`). Verify: the new key resolves in-game (tab label
  and nav-button tooltip both show "Inbox Inventory", not a missing-lang-key placeholder).

## 6. Spec sync and verification

- [x] 6.1 Run the Core test suite and confirm no regressions (`src/Core/` is untouched by this
  change, so this is a sanity check, not expected to need new tests). Verify:
  `dotnet test tests/Core.Tests` passes.
- [x] 6.2 Manually playtest the full flow: place an Inbox block, open it, switch to Inbox
  Inventory, store/retrieve items in both restricted and open slots, break the block and confirm
  drops, reload the world and confirm persistence. Verify: all sub-steps behave as described in
  `specs/inbox-inventory/spec.md`'s scenarios with no crashes or desync.
