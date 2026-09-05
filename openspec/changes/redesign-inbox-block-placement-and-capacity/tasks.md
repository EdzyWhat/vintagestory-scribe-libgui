## 1. Inbox capacity: 8 slots → 12 slots (8 restricted + 4 open)

- [x] 1.1 In `src/Mod/BlockEntityInbox.cs`, change `SlotCount` from `8` to `12` and
  `RestrictedSlotCount` from `4` to `8`; verify the project builds (`dotnet build -c Debug`)
  with no other change (the slot factory and tree persistence are already index-driven and need
  no edits).
- [x] 1.2 Add an Atlas integration test (`tests/Integration.Tests/`) that places an Inbox block,
  fills all 12 slots (8 restricted with a Scribe item, 4 open with an arbitrary item), confirms a
  non-Scribe item is rejected from a restricted slot (index < 8) and accepted into an open slot
  (index ≥ 8), and confirms the block's own tree round-trip (`ToTreeAttributes`/
  `FromTreeAttributes`) preserves all 12 slots' contents.
  - `InboxCapacityScenarios.Inbox_12_slots_enforce_restriction_and_survive_a_tree_roundtrip`.
    Went through the real `ItemSlot.TryPutInto` accept path (not direct `Itemstack` assignment)
    so the restriction itself is actually exercised. Made `BlockEntityInbox.SlotCount`/
    `RestrictedSlotCount` `public` (were `internal`) to reference them from the test assembly,
    matching `BlockEntityAssignmentDesk`'s own public slot-index constants.
- [x] 1.3 Add an Atlas integration test simulating a pre-this-change Inbox: construct/save a
  block entity's tree attributes as if only 8 slots were ever persisted (write items into
  indices 0-7 only, as the old `SlotCount` would have), then load it through the new 12-slot
  `BlockEntityInbox` and confirm the original 8 items load unchanged in their original slots and
  indices 8-11 are empty.
  - `InboxCapacityScenarios.A_pre_change_8_slot_inbox_loads_with_items_intact_and_4_new_empty_slots`.
    Hand-built the old-shaped tree (`qslots=8`, only indices 0-7 in the `slots` sub-tree) and fed
    it through `FromTreeAttributes` on the same live, placed block entity (not a bare
    `new BlockEntityInbox()` — its base chain resolves `Block` from `Api`/`Pos`, which only a
    placed, `Initialize`d entity has).

## 2. Inbox Inventory tab: 2-row grid → 3-row grid

- [x] 2.1 In `src/Mod/GuiDialogScribeInbox.cs`'s `BuildInboxInventoryContent`, chunk the
  restricted range into two `Row`s of 4 (indices 0-3, 4-7) instead of one `Row` of 4, and add
  both ahead of the existing open-slot `Row` in the `Column`, using the same
  `SlotRowSpacing`/`SlotSpacing` constants. Update the method's doc comment's "2 rows of 4"/"8
  slots" language to "3 rows of 4"/"12 slots." Verify by build only (visual confirmation is
  task 4.2 below).

## 3. Wall-mounted placement mode

- [x] 3.1 In `src/Mod/assets/scribe/blocktypes/inbox.json`, add a `variantgroups` entry whose
  `code` matches the engine's placement-orientation convention with states covering the ground
  case (`up`) and the four horizontal wall directions (`north`/`east`/`south`/`west`), following
  `assets/survival/blocktypes/wood/torch.json`'s exact `variantgroups`/`allowedVariants` shape
  (`loadFromProperties: "abstract/horizontalorientation"` for the orientation group). Verify the
  blocktype JSON parses and `dotnet build -c Debug` succeeds.
  - No `allowedVariants` needed: unlike the torch (3 crossed variant groups), the Inbox has only
    the one new group, so every combination is already meaningful — nothing to restrict.
  - Also updated: `creativeinventory` restricted to `*-up` (matching the torch), and fixed two
    now-dangling bare-code references that the added variant suffix would otherwise break —
    `lang/en.json`'s `block-scribeinbox` → `block-scribeinbox-*` and its handbook link
    `block-scribe:scribeinbox` → `block-scribe:scribeinbox-up`.
- [x] 3.2 Add a new wall-mounted shape file under `src/Mod/assets/scribe/shapes/block/inbox/`
  (a structurally-adapted clone of the existing ground shape rotated to sit flush against a
  wall — exact geometry is a first-pass placeholder per design.md's Risk note, not final art) and
  wire it into `inbox.json`'s `shapebytype` (`*-up` → the existing ground shape, `*-north` →
  the new wall shape with matching `rotateY` per direction, mirroring the torch's pattern).
  Verify both shape files parse and the block still loads without missing-texture/asset warnings
  in the client log for either variant.
  - `inbox-wall.json` is a verbatim clone of `inbox.json`'s shape with a single `rotationX: -90`
    added to its root element (tips the whole desk-derived hierarchy at once, per Blockbench's
    nested-rotation cascade — same mechanism the shape's own `lectern:origin`/`ink:origin` child
    rotations already rely on). No texture/UV changes, so no missing-texture risk; exact fit
    against a wall face is left for a real art pass, per design.md's accepted Risk.
  - Missing-texture/asset-warning confirmation in the client log is folded into manual playtest
    4.1 (requires opening the game).
- [x] 3.3 Confirm (reading `BlockInbox.cs`/base class behavior, no code change expected if the
  engine's variant-group + face-placement convention already resolves this generically the way
  it does for the vanilla torch) that placing against a floor yields the `up` variant and placing
  against a wall's side yields the matching horizontal variant with correct `rotateY`; if the
  engine does NOT resolve this automatically for this block's class hierarchy, add the minimal
  override needed (check `Block`'s default `TryPlaceBlock`/variant-resolution first before
  writing new placement code). Verify by build + a manual placement check (folded into 4.1).
  - Finding: it does NOT resolve automatically. Read `Block.TryPlaceBlock`/`DoPlaceBlock` (vsapi
    clone): the default always places `this` block's own id — variant-switching-by-face is
    specific to `BlockGroundAndSideAttachable` (the torch's own base class), which `BlockInbox`
    does not inherit (it inherits `BlockScribeWritingStation : Block`, for the shared
    document/lock/tooltip machinery every other writing station needs).
  - Added the minimal override: `BlockScribeWritingStation.RequiresSolidGround` generalized from
    a fixed per-class `bool` to a `bool RequiresSolidGround(BlockSelection)` method (Chalkboard's
    override updated to match) so the floor check can vary per placement attempt, not just per
    block class. `BlockInbox` overrides it (`true` only for a `BlockFacing.UP` click) and
    overrides `TryPlaceBlock` itself: a top-face click delegates unchanged to the base (ground
    mode, same floor-check + player-facing rotation as before); a horizontal-face click checks
    `CanAttachBlockAt` on the neighbor behind that face, then places the matching
    `-<direction>` variant directly, mirroring `BlockGroundAndSideAttachable.TryAttachTo`.
  - Also added `BlockInbox.GetDrops`/`OnPickBlock` overrides (not anticipated by this task's own
    text, but required by this change's `inbox-block` spec delta: "Picking the block from either
    placement mode SHALL yield the same Inbox item") — without them, breaking/picking a
    wall-mounted Inbox would drop/pick a `-<direction>` item instead of `-up`, since the base
    class's existing `GetDrops`/`OnPickBlock` drop whichever variant is placed. Both now always
    resolve to the `-up` block, mirroring `BlockGroundAndSideAttachable.GetDrops`'s identical
    fix-up for the torch.

## 4. Manual verification

- [ ] 4.1 Manual playtest: place an Inbox block against the ground — confirm it renders the
  existing (ground) model; place another against a wall — confirm it renders the new wall model,
  oriented correctly facing outward from the wall in all four horizontal directions; break and
  pick up each — confirm both yield the same Inbox item.
- [ ] 4.2 Manual playtest: open the Inbox Inventory tab and confirm all 12 slots render — two
  rows of 4 Scribe-item-restricted slots (with the watermark hint) followed by one row of 4 open
  slots — centered in the tab, matching the Assignment Desk's slot styling, with no change to the
  dialog's overall bounding box or the Inbox tab's own content.
- [ ] 4.3 Manual playtest: with an existing world containing an Inbox block placed before this
  change with items in its old 8 slots, load that world after this change — confirm the original
  items are intact in their original slots and the 4 new slots are empty (in addition to the
  automated coverage in task 1.3, to catch anything the synthetic tree-attribute test might miss
  about the real save/load path).
