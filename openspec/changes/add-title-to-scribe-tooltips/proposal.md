## Why

A Scribe document's title is only visible once you open its GUI — the placed Lectern and the
Notebook items give no hint of what they contain while sitting in the world or a hotbar/inventory
slot. Vanilla collectibles surface their salient stat on hover (a weapon shows damage/durability);
a Scribe object's salient "stat" is its title. Meanwhile the Lectern's tooltip currently shows
`Burn temperature` / `Burn duration` lines — combustion stats that are irrelevant to how the block
is actually used and only add noise.

## What Changes

- **Title on the Lectern's placed-block tooltip:** hovering a placed Lectern SHALL add a
  `Title: "<title>"` line (title wrapped in double quotes) sourced from the block entity's live
  document title.
- **Title on the Notebook items' held/inventory tooltip:** hovering the plain Notebook or the
  Clockmaker's Notebook in a hotbar/inventory slot SHALL add the same `Title: "<title>"` line,
  sourced from the document stored in the ItemStack.
- **Untitled placeholder:** when an object has no meaningful title (a never-titled document, whose
  stored title is the model default `"Untitled"`, or an item that has never been opened and carries
  no document yet), the line SHALL still appear with a placeholder — `Title: "(untitled)"` — so the
  field is consistently present.
- **Remove combustion noise from the Lectern:** drop `combustibleProps` (burnTemperature/
  burnDuration) from the lectern blocktype so those two lines no longer render on its tooltip.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `lectern-block`: add a requirement that the placed Lectern's hover tooltip shows the document
  title (quoted, with an untitled placeholder), and a requirement that the Lectern no longer
  advertises combustion (burn) stats on its tooltip.
- `notebook-item`: add a requirement that the plain Notebook and Clockmaker's Notebook show the
  document title (quoted, with an untitled placeholder) on their held/inventory tooltip.

## Impact

- **Lectern block** (`src/Mod/BlockScribeLectern.cs`): override `GetPlacedBlockInfo` to append the
  title line, reading `Document.Title` off `BlockEntityScribeLectern` (`.Document`, non-null).
- **Lectern blocktype JSON** (`src/Mod/assets/scribe/blocktypes/lectern.json`): remove the
  `combustibleProps` block (burnTemperature 600 / burnDuration 10).
- **Notebook items** (`src/Mod/ItemScribeNotebook.cs`, `src/Mod/ItemClockmakerNotebook.cs`):
  override `GetHeldItemInfo` to append the title line, reading via
  `ScribeDocumentAttributes.TryReadFrom` (guarding the never-opened, no-attribute case).
- **Lang** (`src/Mod/assets/scribe/lang/en.json`): add a `Title:` label key and an `(untitled)`
  placeholder key, following the existing `scribe:`-prefixed lang convention.
- **Shared formatting:** a single helper formats the quoted title line so the block and both items
  render it identically (avoid three divergent copies).
- No new dependencies; no persistence/codec/wire changes; no `Core` changes (title access is
  read-only through existing accessors).
