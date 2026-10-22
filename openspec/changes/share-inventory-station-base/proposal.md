## Why

`BlockEntityScriptorium`, `BlockEntityAssignmentDesk` and `BlockEntityInbox` each carry the same ~70 lines for
their slot inventory: a lazily created `InventoryGeneric`, `LateInitialize` binding in `Initialize`, an
inventory sub-tree in `ToTreeAttributes`/`FromTreeAttributes`, the vanilla container packet flow in
`OnReceivedClientPacket`, and `DropAll` in `OnBlockBroken`. Each copy says it mirrors another. A fix to one
(for example the slot-click binding) has to be repeated in all three.

## What Changes

- Add an abstract `BlockEntityScribeInventoryStation` deriving from `BlockEntityScribeWritingStation`. It owns
  the shared mechanics and asks the subclass for four things: slot count, the tree sub-key, the channel name
  prefix, and the slot factory.
- Make the three block entities derive from it and delete their duplicated members.
- Persisted tree keys (`scriptoriumInventory`, `assignmentDeskInventory`, `inboxInventory`) and channel ids
  (`scribescriptorium-`, `scribeassignmentdesk-`, `scribeinbox-`) stay byte-identical, so saved worlds load
  unchanged.
- Add Atlas tests that pin each block's tree key and a save/load round trip first, then run them again after
  the refactor.

## Capabilities

### New Capabilities
None.

### Modified Capabilities
None. No player-visible behavior changes (`skip_specs: true`).

## Impact

`src/Mod/BlockEntityScriptorium.cs`, `BlockEntityAssignmentDesk.cs`, `BlockEntityInbox.cs` and a new base file;
about 150 lines removed. Needs an in-game check that each block still stores, shows and drops its items.
