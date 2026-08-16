# Design — add-scriptorium-inventory

## Context

The Scriptorium block ships today (`BlockScriptorium` / `BlockEntityScriptorium` /
`GuiDialogScribeScriptorium`, plus blocktype JSON, recipe, lang, handbook) with the five standard
tabs. Its block entity subclasses `BlockEntityScribeWritingStation`, which owns a Scribe *document*
but **no inventory**. This change adds a small Scribe-items-only inventory as a new dialog tab — the
storage substrate that the later copy/paste and import/export changes build on.

The load-bearing unknown was whether the mod's **LibGUI** dialog can host real VS item-slot
drag/drop. Ground-truth DLL + LibGUI-source research answered **yes**: LibGUI does not reuse
vanilla's Cairo `GuiElementItemSlotGrid`, but its `Gui.Widgets.Inventory` namespace
(`SlotController`, `SlotGrid`, `FlatItemSlot`) faithfully reimplements the identical vanilla slot
protocol — building an `ItemStackMoveOperation`, calling `inventory.ActivateSlot(...)`, and sending
the resulting packet via the block-entity packet channel. From the server's and the inventory's
perspective it is indistinguishable from a native slot grid. Critically, `ScribeDialogBase` already
extends LibGUI's `GuiDialogBlockEntityBase` — today via its inventory-less `(pos, capi)` ctor.

Constraints: `src/Core/` untouched (this is pure Mod/VS-API work); no new dependencies (vanilla
inventory types only); persistence/sync follows the vanilla Sign/container pattern; server-
authoritative.

## Goals / Non-Goals

**Goals:**
- A 2-slot inventory on `BlockEntityScriptorium` that accepts only `IScribeDocumentItem` stacks.
- Move items in/out through the Scriptorium dialog using LibGUI's native slot widgets, via the
  standard server-authoritative container packet flow (no custom "scribe"-channel messages for the
  moves).
- Persist + sync via the vanilla container pattern; survive save/reload; drop stored items on break.
- Surface it as a new Scriptorium-only nav-rail tab.

**Non-Goals:**
- Copy/paste *transfer* semantics (merging a stored item's document into the Scriptorium document,
  or item-to-item). This change stores and returns whole items only.
- JSON/CSV import/export.
- Any assignment / Inbox surface (v1.3).
- A dedicated Scriptorium backdrop/art (already a separate tracked follow-up).

## Decisions

### D1 — Use LibGUI's `Gui.Widgets.Inventory` slot widgets, wired to the standard container packet flow
Rather than invent a custom click-to-move interaction over the "scribe" network channel, use the
built-in path: LibGUI's `SlotController` + `SlotGrid`/`FlatItemSlot` on the client, and the vanilla
block-entity packet channel (packet ids `<1000` for slot ops, `1000`/`1001` for open/close) on the
server. This is the same mechanism every vanilla container uses; the client emit side
(`SlotController.ClickSlot → SendPacketClient`, and `GuiDialogBlockEntityBase.OnGuiClosed →
SendBlockEntityPacket(1001)`) is already provided by LibGUI.
- **Why over a custom "scribe"-channel move:** less code, no bespoke server validation, correct
  predictive-client + authoritative-server behavior for free, and it reuses the exact battle-tested
  vanilla protocol. The mod's `ResolveItemPacketSlot` "scribe"-channel path is for *document* edits,
  a separate concern — item movement should not duplicate it.
- **Server-open gap discovered in playtest (the load-bearing fix):** LibGUI's
  `GuiDialogBlockEntityBase` provides only *part* of the client emit side. Its `OnGuiClosed` sends the
  `1001` close (and `CloseInventoryAndSync`), and its `OnGuiOpened` opens the inventory
  **client-side only** (`InventoryManager.OpenInventory`, for move prediction) — but it **never sends
  the paired `1000` open packet** that vanilla `BlockEntityOpenableContainer.toggleInventoryDialogClient`
  sends. That packet is not optional: the server's `HandleActivateInventorySlot` (packet id `7`)
  resolves the move's `TargetInventoryId` **only** from the player's *opened* inventories
  (`player.InventoryManager.GetInventory(id, out …)`), and with no `1000` it logs
  "no such inventory currently opened?" and **silently drops every slot move**. The item then lives
  only in the client's optimism: absent server-side, it vanishes on relog and isn't there for
  `DropAll` to spill on break — exactly the two playtest failures (4.2 Lectern-in, 4.4 destroy-on-break,
  and the confirmed relog symptom). Fix: `ScribeDialogBase.OnGuiOpened` sends `SendBlockEntityPacket(1000)`
  whenever `Inventory != null` (a no-op for the document-only surfaces, which carry no inventory);
  `BlockEntityScriptorium.OnReceivedClientPacket` already handles `1000 → OpenInventory` server-side.
- **Alternative considered:** custom scribe-channel put/take packets. Rejected: reimplements
  `ActivateSlot`/`MouseItemSlot`/drag-distribute semantics we'd get for free, and risks divergence
  from vanilla cursor-stack behavior.

### D2 — `BlockEntityScriptorium` gains an `InventoryGeneric`, kept on the existing base class
Add `InventoryGeneric(2, "scribescriptorium-" + pos, api)` to `BlockEntityScriptorium` while keeping
its `BlockEntityScribeWritingStation` base (do **not** reparent to `BlockEntityOpenableContainer`).
- **Why keep the base:** `BlockEntityScribeWritingStation` carries all the document/lock/guestbook/
  placement machinery the Scriptorium shares with the Lectern; reparenting to a container base would
  lose that. We instead replicate the few container behaviors we need (they're small).
- **Network-readiness (the #1 failure mode):** in `Initialize`, call
  `Inventory.LateInitialize("scribescriptorium-" + Pos, api)` and set `Inventory.Pos = Pos`. This
  binds `InvNetworkUtil` to the block-entity packet channel; without it, LibGUI's
  `SlotController.CanActivate` silently drops every click (logs `[gui] Skipped slot activation … not
  network-ready`).
- **Server round-trip:** override `OnReceivedClientPacket(player, packetid, data)` mirroring
  `BlockEntityOpenableContainer`: forward `packetid < 1000` to
  `Inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data)` then `MarkDirty(true)`;
  `1000 → player.InventoryManager.OpenInventory(Inventory)`, `1001 → CloseInventory(Inventory)`.
- **Alternative considered:** reparent to `BlockEntityOpenableContainer`. Rejected — loses the
  writing-station base.

### D3 — Scribe-items-only accept filter via a custom `ItemSlot`
Populate the `InventoryGeneric` with a custom slot type (e.g. `ItemSlotScribeDocument : ItemSlot`)
whose `CanHold(sourceSlot)` / `CanTakeFrom(...)` return true only for a stack that carries a Scribe
document. `InventoryGeneric` supports a slot factory for exactly this.
- **Why the slot, not dialog-side validation:** the accept rule must hold on the server against
  hostile/programmatic moves, hopper/automation interactions, and shift-click — a slot-level
  `CanHold` is the single authoritative gate. Dialog-side-only filtering would be bypassable.
- **What counts as "a Scribe document stack" (broadened after playtest — feedback):** the accept test
  is `Collectible is IScribeDocumentItem or BlockScribeWritingStation`. `IScribeDocumentItem` marks the
  held document items (Notebook / Clockmaker's Notebook / Tablet, all states); `BlockScribeWritingStation`
  is the base of the placed writing stations (Lectern, Scriptorium), whose broken-back-into-item form
  carries a Scribe document on its stack all the same. The initial `IScribeDocumentItem`-only rule
  rejected a picked-up Lectern, which a playtester expected to be storable — both kinds carry a document,
  so both are accepted.

### D4 — Persistence via `Inventory.ToTreeAttributes` / `FromTreeAttributes`, additively
In the BE's existing `ToTreeAttributes`/`FromTreeAttributes` overrides, call
`Inventory.ToTreeAttributes(tree)` / `Inventory.FromTreeAttributes(tree)` alongside the existing
document keys. `InventoryGeneric` handles its own sub-tree, so this is additive: an older saved
Scriptorium simply has no inventory sub-tree and loads with empty slots. **No Scribe-document codec
bump** — this stores whole ItemStacks, not document bytes.

### D5 — Drop stored items on break
In `OnBlockBroken`, guarded by `Api is ICoreServerAPI`, call
`Inventory.DropAll(Pos.ToVec3d().Add(0.5, 0.5, 0.5))`, so no stored Scribe document is destroyed by
breaking the block. (This is what `BlockEntityContainer.OnBlockBroken` does; we replicate the single
call since we're not on the container base.) Existing document-survival behavior is unchanged.

### D6 — New `ScribeLecternView.Inventory` view + Scriptorium-only nav button
The inventory is **not** always visible: it lives behind a new nav-rail tab reached exactly like the
existing Guestbook / Pinned tabs, using the structures already built. This single tab is the anchor
**"page"** the later features extend — copy/paste and import/export add their controls *to this same
page* rather than each spawning another tab. This change lands the page with just the slots on it.

Add an `Inventory` value to the private `ScribeLecternView` enum in `ScribeDialogBase`, an
`OnClickSwitchToInventory` handler + `IsInventoryView` flag (mirroring the Visitors/History/Timer
pattern), and a build method that creates the `SlotController`, `WatchInventory(be.Inventory)`, and
places a `SlotGrid`/two `FlatItemSlot`s. The nav button is added in
`GuiDialogScribeScriptorium.GetExtraNavButtons` (like its existing Guestbook button), so it appears
only on the Scriptorium. Change the Scriptorium dialog's base ctor call to the inventory-carrying
`GuiDialogBlockEntityBase` ctor so `OpenInventory`/`CloseInventoryAndSync` fire automatically.
- **Lifecycle:** create the `SlotController` in the view's `InitState` and
  `UnwatchInventory` + `controller.Dispose()` in `Dispose`, or the `SlotModified` handler leaks
  (per LibGUI's `InventoryPage` example). Let the base handle open/close; do not manually
  `OpenInventory` (duplicate-open guard).

### D7 — Book watermark UNDER each slot ("Scribe items only" affordance — playtest feedback)
Each slot draws a `scribebook` glyph as a watermark telegraphing that only Scribe items belong here. This
means hand-building the slot row instead of using `SlotGrid`: each slot is a `Stack` of the glyph and the
real `FlatItemSlot` (which owns all gesture/hover/item rendering).

**The glyph goes UNDER the slot, always present, not over it** (the first-cut over-layering was reverted
after feedback). The `FlatItemSlot`'s box fill is made transparent (`BackgroundColor = (0,0,0,0)`, border
kept) so the fully-opaque book beneath becomes the visible interior. `RenderStack` sizes to the sole
non-positioned child (the `FlatItemSlot` at 48px) regardless of order and paints in list order, so the
`Positioned` book (listed first) paints under the slot (listed second). This one change fixes three problems
the over-layer version had at once:
- **Visibility:** the book is the sole visible fill at full opacity, so it actually reads. (The first cut
  layered a *faint* light-tinted glyph over the ~40%-black slot box; it was invisible against the parchment.)
- **Click-through:** the `FlatItemSlot` is on top and owns the whole-slot gesture region, so the glyph can
  never intercept the click that drops an item in. (The over-layer version blocked the slot centre even
  wrapped in `IgnorePointer` — under-layering removes the problem structurally, no `IgnorePointer` needed.)
- **Z-order / no flicker:** a placed item is drawn by the slot's own overlay, i.e. ON TOP of the book, so
  the watermark is simply always beneath — "a fun visual indicator that's always there," never toggled on
  `slot.Empty`. (The over-layer version, toggled by emptiness, briefly showed the watermark on top of a
  just-placed item during the client/server round-trip.)
- **Glyph rendering:** uses the mod's `ScribeVsIconGlyph` (icon-by-code, via the self-healing `CustomIcons`
  delegate) — NOT LibGUI's `Icon`/`SvgIconSource` widget, whose `LoadSvg`-by-path fails on our
  post-startup-unloaded assets (see `ScribeRowWidgets`/VSAPI-NOTES). Tint is full-opacity `Primary` (the
  terracotta/amber accent) for contrast on the parchment; exact scale/tint stay tunable polish knobs.

### D8 — Creative-clone must mint a fresh DocId (discovered in playtest)
Middle-click "pick block" in creative stamps the source block's serialized document — **including its
`DocId`** — onto the copy's stack (`BlockScribeWritingStation.OnPickBlock`, same as break→re-place), and
`BlockEntityScribeWritingStation.OnBlockPlaced` restores it. But `DocId` is the key of both the mod's host
registry (`ScribeModSystem._hostRegistry`) and the pin store's live index (`ScribePinStore._docPositions`).
A clone leaves the **source still alive and registered** under that id, so the copy collides: its dialog /
editor-lock / pin traffic resolves (via `TryResolveHost`) to the *original* block — the observed
"after copying a Scriptorium I can no longer open it" symptom (and the likely root of the earlier
"inventory looks destroyed" confusion — the copy's open routed to the wrong host).

- **Fix:** in `OnBlockPlaced` (server-only), if the restored `DocId` is already registered to a *different
  live block* (`ScribeModSystem.IsDocIdRegisteredToOtherBlock(docId, Pos)`), call the new
  `ScribeDocument.ReassignNewDocId()` to forge a unique identity for the copy before registering it. The copy
  keeps the source's title/task **content** (a real duplicate) but starts with its own identity and no pins.
- **Why the "registered to a different live block" test distinguishes clone from break→re-place:** a
  break→re-place path runs `OnBlockRemoved` first, which `UnregisterHost`s the source — so the id is free and
  we correctly KEEP it (pins reattach, as intended). Only a clone leaves the id occupied.
- **Scope note:** this is a latent bug in the *shared* writing-station clone path — it affects a cloned
  Lectern too, not just the Scriptorium — surfaced here because the inventory feature drove creative-copy
  testing. Fixed at the shared base so all tiers benefit. `src/Core` change is limited to the VS-API-free
  `ReassignNewDocId()` method.

## Risks / Trade-offs

- **[Inventory not network-ready → clicks silently dropped]** → Always `LateInitialize` + set
  `Inventory.Pos` in `Initialize`; add an in-game test that actually moves an item, and watch for
  the `[gui] Skipped slot activation` log.
- **[Leaked `SlotModified` handler if the view isn't disposed]** → Unwatch + dispose the
  `SlotController` in the view State's `Dispose`; verify by opening/closing the tab repeatedly.
- **[`ScribeDialogBase` ctor change touches all subclasses]** → The base already extends
  `GuiDialogBlockEntityBase`; only the Scriptorium needs the inventory-carrying ctor. Confirm the
  Lectern/Tablet/Notebook paths still pass a null/absent inventory and are unaffected (they don't
  add the tab, so they never build slot widgets).
- **[Two-client sync while both view the block]** → `WatchInventory` rebuilds on `SlotModified`, and
  the server broadcasts it; verify with the standard two-client Atlas/playtest.
- **[Auto-close on walk-away]** → Already governed by `ScribeDialogBase`'s pinned
  `InteractionRange` (`DefaultPickingRange + 0.5`); keep it — it also closes the open container
  dialog correctly.

## Migration Plan

Additive and backward-compatible. Deploy: ship the new BE inventory + dialog tab. Old placed
Scriptoria load with empty slots (no inventory sub-tree). Rollback: reverting the code leaves any
persisted inventory sub-tree as inert unread attributes; items that were inside would need to be
recovered by re-adding the feature or breaking the block on the new build first. No world migration
step is required.

## Open Questions

- **Slot rendering size / layout** in the tab — settled (D7): two slots centered in a hand-built row at
  the stock 48px size/4px spacing, each empty slot carrying a faint `scribebook` watermark. Remaining
  in-game tuning: the watermark's exact alpha/scale (`Surface` @ 0.28, 0.6× slot) — a polish knob, not a
  correctness issue.
- **Nav-button icon** for the inventory tab — settled: reused the open-book glyph (`scribeinventory`
  aliased to `book.svg`), mirroring `scribehistory`→`guestbook.svg`.
- **VSAPI-NOTES / libgui-reference staleness:** the research found the `reference/vslibgui/` clone
  referenced by VSAPI-NOTES is absent locally. Not blocking (findings are captured here), but worth
  a note in VSAPI-NOTES' LibGUI section for the next slot-related task.
