## 1. Shared formatter + lang

- [x] 1.1 Add two `scribe:`-prefixed keys to `src/Mod/assets/scribe/lang/en.json`: a title-line
      label (e.g. `tooltip-title` = `Title: "{0}"`) and an untitled placeholder (e.g.
      `tooltip-title-untitled` = `(untitled)`).
- [x] 1.2 Add a small static helper (e.g. `ScribeTooltip.FormatTitleLine(string? rawTitle)`) that
      returns the localized `Title: "<title>"` line, substituting the `(untitled)` placeholder when
      `rawTitle` is null/whitespace OR equals `ScribeDocument.DefaultTitle`. Single source of truth
      for quoting + placeholder so all three call sites match.

## 2. Lectern placed-block tooltip + burn removal

- [x] 2.1 In `src/Mod/BlockScribeLectern.cs`, override
      `GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)`: call `base`, then
      append the formatted title line, reading the title from
      `GetBlockEntity(pos) as BlockEntityScribeLectern`'s `Document.Title` (fall back to the
      placeholder if the BE is missing).
- [x] 2.2 Remove the `combustibleProps` block (burnTemperature/burnDuration) from
      `src/Mod/assets/scribe/blocktypes/lectern.json`.
- [x] 2.3 Also override `GetHeldItemInfo` on `BlockScribeLectern` so the Lectern's held/inventory
      form shows the same title line (reading the ItemStack document via
      `ScribeDocumentAttributes.TryReadFrom`) — playtest follow-up (2026-08-01T16-54-36 general note).

## 3. Notebook item tooltips

- [x] 3.1 In `src/Mod/ItemScribeNotebook.cs`, override
      `GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)`:
      call `base`, then append the formatted title line, reading via
      `ScribeDocumentAttributes.TryReadFrom(inSlot.Itemstack, out var doc)` (null/false → placeholder).
- [x] 3.2 Apply the same `GetHeldItemInfo` override in `src/Mod/ItemClockmakerNotebook.cs` (its
      upgrade copies the document payload, so the carried-over title renders automatically).

## 4. Verification

- [x] 4.1 `dotnet build src/Mod/Mod.csproj -c Debug` — zero new warnings/errors.
- [x] 4.2 `bash build/restage.sh Debug`, then fully quit + relaunch the game (assets/lang load at boot).
- [x] 4.3 Manual in-game: place a Lectern, give it a title; look at it — tooltip shows
      `Title: "<title>"` and NO Burn temperature/duration lines. Look at a fresh (untitled) lectern —
      tooltip shows `Title: "(untitled)"`.
- [x] 4.4 Manual in-game: hover a titled Notebook and a Clockmaker's Notebook in the inventory —
      each shows `Title: "<title>"`; a never-opened Notebook shows `Title: "(untitled)"`.
- [x] 4.5 Update `TESTING.md` with the new in-game tooltip items.
- [ ] 4.6 Manual in-game (2.3 follow-up): break/pick-up a titled Lectern and hover the resulting
      block item in the inventory — confirm it shows `Title: "<title>"`, matching the Notebook.
