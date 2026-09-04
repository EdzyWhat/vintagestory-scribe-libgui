## Why

Playtest feedback (2026-09-03) on the standalone Inbox block: it currently only places on the
ground, with one model, and its 8-slot inventory (4 Scribe-only + 4 open) is already feeling
tight now that Task Notices are a first-class delivery path. Two related asks came out of the
same session: give the Inbox wall-mount placement (like a lantern) alongside its existing
ground placement, and grow its inventory so there's more open storage headroom without shrinking
the Scribe-only reserve.

## What Changes

- The Inbox block gains a second placement mode: wall-mounted, in addition to its existing
  ground placement, each with its own model/orientation (precedent: the vanilla wood torch's
  `orientation` variant group + `shapebytype`, which resolves a ground vs. wall shape from the
  face the player placed against).
- The Inbox's own inventory grows from 8 slots (4 restricted + 4 open, 2×4) to 12 slots (8
  restricted + 4 open, 3×4) — more Scribe-item storage, same 4 general-purpose slots.
- The Inbox Inventory tab's slot grid and the block's `IScribeDocumentHost` layout dimensions
  are updated for the new 3×4 grid.

## Capabilities

### New Capabilities
(none — both changes extend existing Inbox capabilities rather than introducing new ones)

### Modified Capabilities
- `inbox-block`: placement gains a wall-mounted mode alongside ground placement (each with its
  own model); the layout requirement's references to the Inbox Inventory tab's slot grid update
  from 8 slots to 12.
- `inbox-inventory`: slot count/layout changes from 8 slots (4 restricted + 4 open, 2 rows × 4
  columns) to 12 slots (8 restricted + 4 open, 3 rows × 4 columns); the restricted/open split and
  every requirement/scenario referencing the old counts or layout updates accordingly.

## Impact

- **Assets**: `src/Mod/assets/scribe/blocktypes/inbox.json` (new `orientation`/placement
  variant group, `shapebytype`), `src/Mod/assets/scribe/shapes/block/inbox/` (a new wall-mounted
  shape alongside the existing ground shape cloned from the Scriptorium in
  `add-custom-models-tasknotice-desk-inbox` — check whether that change is archived yet before
  assuming the current file layout).
- **Code**: the Inbox block's placement logic (likely a `BlockBehavior`/base-class override
  mirroring `BlockTorch`'s face-to-orientation resolution), the Inbox block entity's inventory
  slot count/restriction wiring, and the Inbox Inventory tab's slot-grid rendering + the block's
  `IScribeDocumentHost.GetLayout` dimensions.
- **Specs**: `inbox-block`, `inbox-inventory` (both already archived into `openspec/specs/` —
  this change's delta specs modify them in place, to be merged on archive as usual).
