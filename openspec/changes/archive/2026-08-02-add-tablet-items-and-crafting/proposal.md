## Why

Scribe's writing surfaces — the Lectern, Notebook, and Clockmaker's Notebook — are all
mid/late-game crafts. A player in the early tech tree has nowhere to jot goals or tasks. Two
early-game craftable **tablet** items (clay and wax) give the earliest players a writing surface
by reusing Scribe's existing document infrastructure, presented as a leaner, more limited tier.

## What Changes

- Add two tablet items — clay and wax — via **one** `ItemScribeTablet` class with a `material`
  variant axis `[clay, wax]`. Modeled on `ItemScribeNotebook` (MaxStackSize 1, right-click to
  open with shift pass-through to ground storage, title tooltip line, `Crafted` history entry).
- Both tablets are crafted by **simple grid recipes** (NOT clayforming): a clay + sticks style
  recipe and a beeswax + frame style recipe.
- Persist the tablet's document by **pure reuse** of `ScribeDocumentAttributes` (docId + document
  bytes on `ItemStack.Attributes`). No new persistence code, no new network packet — the existing
  `ScribeNotebookSaveMessage` and its frozen registration order are reused.
- Add `TabletHost` (`IScribeDocumentHost`), a thin variant of `NotebookHost`: layout aspect
  `1160/1024`, default title `"Tablet"`, and enforcement of the tablet policy at the mutation
  boundary.
- Add a Core `ScribeDocumentPolicy` value type (`int? MaxBlocks`, `int? MaxPins`, `bool ReadOnly`)
  with a `Tablet` preset (`MaxBlocks = 10` task blocks, `MaxPins = 1`) and `CanAdd`/`CanPin`
  predicates, applied at the host/editor boundary. `ScribeDocument` itself stays tier-agnostic and
  uncapped.
- Register `ItemScribeTablet` in `ScribeModSystem.Start()`.
- Add assets: `itemtypes/scribetablet.json` (variantgroups `material [clay, wax]`, class
  `ItemScribeTablet`, maxstacksize 1, GroundStorable, creative), placeholder art reusing a vanilla
  clutter tablet shape/textures, grid recipes `recipes/grid/scribetablet-clay.json` +
  `scribetablet-wax.json`, and lang keys.
- **Interim behavior**: a tablet crafted in this change opens the **existing** Scribe document
  editing dialog (`GuiDialogScribeNotebook`) so the item is testable before the bespoke tablet
  dialog exists. The bespoke dialog is Proposal C.

## Capabilities

### New Capabilities
- `clay-wax-tablet-item`: the two clay/wax tablet items (one class, `material` variant axis), grid
  crafting, docId persistence by reuse, creative access, the `TabletHost` adapter, and the interim
  reuse of the existing document dialog.
- `scribe-document-policy`: a reusable Core rule type that caps a document tier (max task blocks,
  max pins, read-only), applied at the host/editor mutation boundary — the `Tablet` preset caps at
  10 tasks and 1 pin. Split out from the item capability because the later tablet-dialog and
  pencil-toggle-row proposals also consume it.

### Modified Capabilities
- None. `IScribeDocumentHost` is implemented (not altered) by `TabletHost`; the caps live at the
  mutation boundary rather than in `ScribeDocument`, so `task-note-document` and `player-pins`
  requirements are unchanged; and the interim dialog reuse does not change `notebook-item`.

## Impact

- New code: `src/Mod/ItemScribeTablet.cs`, `src/Mod/TabletHost.cs`, `src/Core/ScribeDocumentPolicy.cs`.
- Edited code: `src/Mod/ScribeModSystem.cs` (one `RegisterItemClass` line in `Start()`).
- New assets: `itemtypes/scribetablet.json`, `recipes/grid/scribetablet-{clay,wax}.json`, lang keys,
  placeholder shape/texture references (vanilla clutter tablet).
- New tests: Core unit tests for `ScribeDocumentPolicy` in `tests/Core.Tests`.
- No new mod dependencies, no new network packet, no persistence-format change.
