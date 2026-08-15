## Why

The v1.2 roadmap tier (`docs/specs/v7-scriptorium-and-task-types.md`) is anchored by a new
placeable block — the **Scriptorium** — the third "placed" tier after the Lectern. It is the
in-world home for the tier's later capabilities (Tracker/Link tasks, an Assign & History view,
an Inbox), but the block itself is a discrete, shippable unit: a craftable piece of furniture
that hosts a Scribe document exactly like the Lectern does. Landing the block first gives those
later features a real surface to attach to, and gives players a second, cheaper, non-metal-gated
writing station in the meantime.

## What Changes

- Add a new placeable block, `scribe:scriptorium`, registered alongside the Lectern.
- It hosts a `ScribeDocument` (task checklist + notes) and opens the existing LibGUI Scribe
  dialog on right-click, reusing the shared document-host + dialog machinery (Read/Edit views,
  quick-add gesture, guestbook, title-in-tooltip) with no new GUI surfaces.
- Server-authoritative persistence/sync via the vanilla Sign pattern, matching the Lectern
  (`ToTreeAttributes`/`FromTreeAttributes`, `SendBlockEntityPacket`, `MarkDirty`), including
  break→re-place document carry-over and pin-store registration.
- Floor-only, face-the-player placement (same idiom as the Lectern), its own 3D Blockbench
  model + textures, a cheap grid recipe (planks + nails, no iron), lang entries, and a handbook
  entry.
- Out of scope (deferred to their own v1.2/v1.3 changes): the Scriptorium's unique **Assign &
  History** view and the **Inbox** nav-rail view (both part of the v1.3 assignment system);
  Tracker/Link/Crafting task types; copy-paste and import/export.

## Capabilities

### New Capabilities
- `scriptorium-block`: a craftable, placeable Scribe writing station (block + block entity)
  that hosts a Scribe document and opens the existing dialog, persisting and syncing on the
  vanilla Sign pattern — including placement orientation, break/pick document carry-over, and
  its recipe, model, and handbook entry.

### Modified Capabilities
<!-- None. The Scriptorium reuses the shared document-host interface and dialog shell as-is;
     no existing spec's requirements change. -->

## Impact

- **New code** (`src/Mod/`): `BlockScriptorium`, `BlockEntityScriptorium` (modeled on
  `BlockScribeLectern` / `BlockEntityScribeLectern`), and two `RegisterBlockClass` /
  `RegisterBlockEntityClass` lines in `ScribeModSystem`.
- **Reuses unchanged**: `IScribeDocumentHost`, `ScribeDocumentCodec`, the LibGUI dialog shell,
  the pin store, and the network channel — the Scriptorium is another host on the same seam.
- **New assets** (`src/Mod/assets/scribe/`): `blocktypes/scriptorium.json`, a Blockbench
  shape + block textures, a grid recipe, a GUI backdrop texture, `lang/en.json` entries
  (block name, interaction hints, default doc title), and a handbook/item entry.
- **`src/Core/`**: no changes — Core is document-model-only and already surface-agnostic.
- **No new dependencies**; vanilla `VintagestoryAPI` only.
- **Save compat**: additive only (a new block id); no change to the document codec or existing
  blocks.
