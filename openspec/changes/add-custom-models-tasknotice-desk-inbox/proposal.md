## Why

Three Scribe visuals are currently placeholders in a way that blocks independent art
iteration: the Assignment Desk and Inbox blocks both point their `shape.base` directly at the
Lectern's own shape file instead of owning a local copy, and the Task Notice item uses one
shape/texture for both its blank and sealed states, so a player can't tell them apart by
looking at the item — only by reading the tooltip. Before anyone can open Blockbench and start
sculpting real art for any of these four things, each needs its own dedicated, locally-owned
model file to edit without touching (or accidentally desyncing) another block's shared file.

## What Changes

- Give the Assignment Desk block its own local shape file — cloned from the Scriptorium's shape
  (`scriptorium.json` and its `.bbmodel` Blockbench source, kept as an editable starting point)
  rather than the Lectern it currently references — plus its own local textures, and repoint
  `assignmentdesk.json`'s `shape.base` and add a `textures` override block (mirroring the
  Scriptorium's own) at them. This does change the Desk's placeholder look, from a Lectern
  book-stand to the Scriptorium's desk+bookshelf+ink&quill look — still a placeholder, but one
  with an editable Blockbench source to work from directly instead of hand-editing raw shape
  JSON.
- Give the Inbox block the identical treatment: its own local shape + `.bbmodel` + textures
  cloned from the Scriptorium, `inbox.json` repointed at them with its own `textures` override
  block.
- Relocate the Task Notice item's existing shape/texture into a dedicated "blank" model
  folder (it already IS the blank appearance today, just not filed that way), and add a new
  placeholder "filled/sealed" model with a small structural difference (e.g. an added seal
  element) so the two are visually distinguishable, even before either gets real final art.
- Add a per-stack model swap to `ItemScribeTaskNotice` via `Item.OnBeforeRender`, selecting
  the blank or filled mesh based on the item's existing `IsSealed(ItemStack)` check. This is
  the first use of `OnBeforeRender` in this mod; the mesh for each state is tesselated once
  and cached (not rebuilt per frame), following the precedent in vanilla's
  `CollectibleBehaviorCustomTongedShape` (tesselate a named alternate shape, cache the
  resulting `MultiTextureMeshRef`, dispose on unload).
- Keeps the Task Notice as a single item code (`tasknotice`) — no item-code split, no changes
  to stacking, recipes, or the existing blank/sealed state-transition logic.

Explicitly out of scope: sculpting real final geometry or painting real final textures for the
Desk, Inbox, or the new filled Task Notice state. The filled Task Notice stays only
minimally/structurally distinct from blank; the Desk and Inbox swap from a Lectern-derived
placeholder to a Scriptorium-derived one (see above), but neither gets bespoke final geometry
or textures in this change — a separate art pass happens later in Blockbench, and in the
meantime the Desk and Inbox will render visually identical to the Scriptorium block.

## Capabilities

### New Capabilities
- `task-notice-item`: adds a requirement that blank and sealed Task Notices render visually
  distinguishable models (this capability doesn't exist yet under `openspec/specs/` — two other
  in-flight changes, `add-assignment-physical-delivery-mode` and `refine-task-notice-ux`, are
  already introducing it in parallel; this adds one more additive requirement alongside theirs)

### Modified Capabilities
- (none) — `assignment-desk-block` and `inbox-block` already state that each block "registers
  and renders its own model" without specifying how that model is sourced; swapping a
  placeholder-by-reference for a placeholder-by-local-file doesn't change that requirement's
  observable behavior, so no delta is needed for either.

## Impact

- `src/Mod/assets/scribe/blocktypes/assignmentdesk.json`, `inbox.json` — `shape.base` repointed
  to new local shape files, a new `textures` override block added (9 keys, mirroring
  `scriptorium.json`'s own), and PLACEHOLDER MODEL comments updated to reflect local ownership
  and the Scriptorium-derived starting point.
- New: `src/Mod/assets/scribe/shapes/block/assignmentdesk/assignmentdesk.json` +
  `assignmentdesk.bbmodel`, `src/Mod/assets/scribe/textures/block/assignmentdesk/*.png` (cloned
  from the Scriptorium's `scriptorium.json` + `scriptorium.bbmodel` + its 9 textures).
- New: `src/Mod/assets/scribe/shapes/block/inbox/inbox.json` + `inbox.bbmodel`,
  `src/Mod/assets/scribe/textures/block/inbox/*.png` (same cloning).
- `src/Mod/assets/scribe/itemtypes/tasknotice.json` — `shape.base` repointed at the relocated
  blank model; blank/filled shape selection moves into code.
- Relocate: `src/Mod/assets/scribe/shapes/item/tasknotice.json` →
  `src/Mod/assets/scribe/shapes/item/tasknotice/blank.json` (textures likewise relocated under
  `textures/item/tasknotice/`).
- New: `src/Mod/assets/scribe/shapes/item/tasknotice/filled.json` and its texture(s).
- `src/Mod/ItemScribeTaskNotice.cs` — new `OnBeforeRender` override, mesh caching, disposal on
  unload.
