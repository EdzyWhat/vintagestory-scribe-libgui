## 1. Pin current behavior

- [x] 1.1 Add Atlas scenarios that, for the Scriptorium, Assignment Desk and Inbox, assert the inventory sub-tree key in the saved tree and a stored-stack round trip; run `build/verify.sh` and confirm they pass before any code changes

## 2. Shared base

- [x] 2.1 Add `BlockEntityScribeInventoryStation` with the lazy inventory, `LateInitialize` binding, tree persistence, container packet flow and `DropAll` on break
- [x] 2.2 Move the Scriptorium, Assignment Desk and Inbox onto it and delete their duplicated members, keeping keys, prefixes, slot rules and the Inbox particle tick; verify build and the pinned scenarios

## 3. Verify

- [x] 3.1 Run `build/verify.sh`; confirm Core and Atlas pass
- [x] 3.2 Playtest: place each block, put items in, relog, confirm they persist, then break the block and confirm the items drop
