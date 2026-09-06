## Context

The Inbox block (`BlockInbox`/`BlockEntityInbox`/`GuiDialogScribeInbox`) currently exists as a
single ground-placed variant. Its inventory is a fixed `InventoryGeneric(SlotCount, ...)` where
`BlockEntityInbox.SlotCount = 8` and `RestrictedSlotCount = 4`, with a slot factory
(`slotId < RestrictedSlotCount ? scribe-restricted : open`). The dialog (`GuiDialogScribeInbox.
BuildInboxInventoryContent`) renders this as two `Row`s (4 restricted, then 4 open) inside a
`Column`, centered. See proposal.md for the "why."

The block's shape/textures were just given a locally-owned, Scriptorium-cloned starting point in
`add-custom-models-tasknotice-desk-inbox` (check whether that change is archived yet — if not,
this change lands directly on its uncommitted/in-progress files, not the old Lectern-referencing
placeholder).

## Goals / Non-Goals

**Goals:**
- Add a wall-mounted placement mode alongside the existing ground placement, each with its own
  model, using the vanilla placement-variant convention rather than a bespoke mechanism.
- Grow the Inbox's inventory from 8 to 12 slots (8 restricted + 4 open) with existing saved
  Inbox blocks loading their old contents intact.

**Non-Goals:**
- No new art for either placement mode's model in this change (both may still be the
  Scriptorium-cloned placeholder, same as today) — this is plumbing, like
  `add-custom-models-tasknotice-desk-inbox` before it.
  **Update (2026-09-05, superseded)**: both `inbox.json` (ground) and `inbox-wall.json` have
  since received real, matching board/cubby geometry, replacing the Scriptorium-cloned
  placeholder this bullet describes — see `add-custom-models-tasknotice-desk-inbox`'s
  proposal.md for the equivalent Assignment Desk update. Both files currently share the same
  blocking bug (35 elements / 72 untextured faces each — see that change's tasks.md 2.4 and
  VSAPI-NOTES.md's Blockbench-export note), so neither variant renders in-game yet.
- No change to the Inbox tab (assignment row list) or to any assignment-state behavior.
- No change to the Assignment Desk or Scriptorium.

## Decisions

### Decision 1: Placement mode via a variant group + `shapebytype`, not a BlockEntity flag
Follow the vanilla wood torch's pattern (`assets/survival/blocktypes/wood/torch.json`): a
`variantgroups` entry whose states are `BlockFacing` codes (`up`/`north`/`east`/`south`/`west`),
resolved automatically by the engine's placement-variant convention from the face the player
clicked, with `shapebytype` mapping the `up` variant to the existing ground shape and the four
horizontal variants to a new wall shape (rotated per direction, same `rotateY` pattern the torch
uses). This reuses an established, boring mechanism instead of writing custom `DoPlaceBlock`
face-detection logic.
- **Alternative considered**: a single block class with `DoPlaceBlock` override choosing an
  `IAttachableToEntity`-style model swap based on `blockSel.Face` (the lantern's approach).
  Rejected — the lantern's complexity (material/lining/glass mesh caching) is solving a
  different problem (player-customizable materials); the torch's plain variant-group approach
  is the closer fit for two fixed models with no per-placement customization.
- Only ONE wall orientation state is visually needed to start (a single wall shape rotated 4
  ways), matching the torch's `*-north/east/south/west` sharing one wall shape.
- **Implementation note (confirmed during apply, tasks.md 3.3):** the torch's automatic
  face→variant resolution lives in its base class, `BlockGroundAndSideAttachable` (confirmed by
  reading both it and vanilla `Block.TryPlaceBlock`/`DoPlaceBlock`) — it is NOT a generic engine
  behavior every variant-grouped block gets for free. `BlockInbox` inherits `BlockScribeWritingStation
  : Block` instead (for the shared document/lock/tooltip machinery), so it needed its own
  `TryPlaceBlock` override mirroring `BlockGroundAndSideAttachable.TryAttachTo`. This also required
  generalizing `BlockScribeWritingStation.RequiresSolidGround` from a fixed per-class `bool` to a
  `bool RequiresSolidGround(BlockSelection)` method (Chalkboard's override updated to match), since
  the Inbox — unlike every ground-only or wall-only writing station — needs the floor check to vary
  per placement attempt, not per block class. Additionally added `BlockInbox.GetDrops`/`OnPickBlock`
  overrides (not anticipated by the original task text) to satisfy this change's own `inbox-block`
  spec delta — "Picking the block from either placement mode SHALL yield the same Inbox item" —
  since the base class's existing drop/pick logic would otherwise drop whichever variant is placed.

### Decision 2: Capacity change is two constants + a chunked-row layout, not a new inventory class
`BlockEntityInbox.SlotCount` (8→12) and `RestrictedSlotCount` (4→8) are the only Core-adjacent
numbers to change; `InventoryGeneric`'s existing tree-based persistence is index-based and
already additive (this is exactly how the original 0-slot→8-slot migration for pre-this-mod-
feature Inbox blocks was handled — same mechanism, no new code), so the "old 8-slot block loads
with items intact + 4 new empty slots" requirement falls out of the existing save/load path with
no special-case migration code.

`GuiDialogScribeInbox.BuildInboxInventoryContent` changes from two `Row`s (4 restricted, 4 open)
to three: the restricted range chunks into two `Row`s of 4 (`Enumerable.Range(0,4)` and
`Range(4,4)`, both against the restricted watermark) stacked in the same `Column` ahead of the
existing open-slot `Row`, using the same `SlotRowSpacing`/`SlotSpacing` constants — no new layout
primitive needed.

### Decision 3: `inbox-block`'s 1:1 square content region is unchanged
The Inbox block's overall dialog bounding box (`IScribeDocumentHost.GetLayout`, W × 1.2W,
matching the Assignment Desk) does not change — only what renders inside that same square
changes (12 slots instead of 8, in 3 rows instead of 2). If the resulting 3-row grid doesn't
visually fit the existing square as comfortably as the 2-row grid did, that's a sizing tune
within `ScribeInventorySlotStyle`/spacing constants, not a layout-contract change — no spec
change needed for it.

## Risks / Trade-offs

- **[Risk]** A single shared wall shape (torch-style) may look wrong on the Inbox's asymmetric
  book/writing-desk-derived geometry (the torch's stick shape is rotationally simple; the
  Scriptorium-derived shape is not). → Mitigation: this is exactly the kind of thing the
  Scriptorium-clone-first, art-later pattern already accepted for the ground model — ship a
  structurally-present but not-yet-art-polished wall shape (even a crude rotated clone of the
  ground shape), matching `add-custom-models-tasknotice-desk-inbox`'s own explicit scope
  boundary of plumbing-before-art.
- **[Risk]** Growing the restricted slot count from 4 to 8 doubles how much can be crammed into
  the Scribe-only rows relative to the Scriptorium's own 2-slot precedent, which may read as
  inconsistent in scale. → Mitigation: this was an explicit, deliberate ask (increase Scribe-item
  headroom specifically), not an oversight — no action needed, just noting the intentional
  asymmetry for future readers.

## Migration Plan

- No player-facing migration for the inventory: `InventoryGeneric`'s existing tree round-trip
  already handles a smaller saved slot count loading into a larger declared one (each slot's
  tree entry is keyed by index; missing higher indices just start empty), following the same
  additive pattern already used when the Inbox first gained this inventory at all.
- No migration for placement: existing placed Inbox blocks keep their current (ground) variant
  code unchanged; only newly-placed blocks can choose wall-mounted.
