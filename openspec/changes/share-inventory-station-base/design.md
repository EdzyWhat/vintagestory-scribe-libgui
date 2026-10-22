## Context

Each of the three blocks has a Scribe-items inventory with the same plumbing. They differ only in slot count,
slot types (Scriptorium: all document slots; Desk: document + task-notice slots; Inbox: 8 document + 4 open),
tree key and channel prefix. Inbox additionally registers a particle tick in `Initialize`.

## Goals / Non-Goals

**Goals:** one copy of the inventory plumbing; unchanged save format and packet behavior.

**Non-Goals:** switching to `BlockEntityOpenableContainer` (the writing-station base is kept on purpose);
changing slot rules, slot counts or any key.

## Decisions

- **Abstract intermediate class, not a helper.** The mechanics override `Initialize`, the tree methods,
  `OnReceivedClientPacket` and `OnBlockBroken`, so they belong in a base class. Subclasses supply
  `InventorySlotCount`, `InventoryTreeKey`, `InventoryChannelPrefix` and `CreateSlot(slotId, inventory)`.
- **Order of operations preserved.** The base calls `EnsureInventory` before `base.Initialize`/`FromTreeAttributes`
  exactly as today (chunk-load reads the tree before `Initialize`). The Inbox keeps its particle registration by
  overriding `Initialize` and calling the base.
- **Constants stay public where referenced.** `SlotCount`/`RestrictedSlotCount` on the Inbox and the Desk slot
  indices are used by dialogs and tests, so they remain on the subclasses.
- **Test first.** Round-trip scenarios assert each block's tree sub-key and that stored stacks survive
  `ToTreeAttributes`/`FromTreeAttributes`, passing before and after the move.

## Risks / Trade-offs

- Virtual calls from a base-class `Initialize` into subclass members must not read uninitialized fields;
  the factory and constants are stateless, so this is safe.
- A changed key would orphan stored items in existing worlds. Mitigated by the key-pinning test.
