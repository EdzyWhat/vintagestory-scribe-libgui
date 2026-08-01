## Why

Crafting a Notebook into a Clockmaker's Notebook currently destroys the very thing the
player was carrying: a vanilla grid recipe produces a fresh, blank output stack, so the
source Notebook's title, tasks, task state, and History chronicle are lost. The upgrade
should feel like the same notebook gaining a Timer tab — not like starting over — so the
crafted Clockmaker's Notebook must inherit the source document (preserving its `DocId`) and
its History.

## What Changes

- When a Clockmaker's Notebook is crafted from a Notebook via the grid recipe, the crafted
  output SHALL copy the source Notebook slot's `"scribeDocument"` attribute onto the output
  before any other processing, preserving the document's `DocId`, title, tasks, and task
  completion state.
- The crafted output SHALL also copy the source Notebook's `"scribeHistory"` attribute, so
  the History chronicle carries over; the existing "Crafted" History entry is then appended
  on top of the carried-over history rather than onto a blank chronicle.
- If the crafting inputs contain no source Notebook with a document (e.g. a future recipe or
  a `/giveitem` output), the output SHALL fall back to today's behavior: a fresh empty
  document with a new `DocId` and a history containing only the "Crafted" entry.
- The Notebook's in-game handbook entry SHALL note that upgrading it to the Clockmaker's
  Notebook keeps its tasks and History, so the documented behavior matches the new carryover.

## Capabilities

### New Capabilities
- `notebook-craft-carryover`: how a Notebook's document identity (`DocId`), contents
  (title/tasks/task-state), and History chronicle transfer onto a Clockmaker's Notebook when
  it is crafted from that Notebook.

### Modified Capabilities
<!-- None: existing specs (notebook-item, timer-lifecycle) describe per-item behavior and
     document persistence, but no existing requirement governs what happens to the document
     across a craft. This is net-new behavior. -->

## Impact

- **Code**: `src/Mod/ItemClockmakerNotebook.cs` — `OnCreatedByCrafting` (server-side only)
  gains a document + history copy step from the source Notebook input slot, before the
  existing "Crafted" history stamp. No change to `src/Core/` (document identity/codec already
  live there and are reused unchanged) and no change to the recipe JSON.
- **Docs/lang**: `src/Mod/assets/scribe/lang/en.json` — the existing
  `handbook-scribenotebook-craft-text` (which already mentions the upgrade path) gains a short
  clause that the upgrade keeps your tasks and History. Data-only text edit.
- **Data/persistence**: uses the existing `"scribeDocument"` / `"scribeHistory"` ItemStack
  attributes and the existing `ScribeDocumentAttributes` / `HistoryStore` codecs — no new
  attribute keys, no new network messages, server-authoritative as today.
- **Behavior**: the crafted Clockmaker's Notebook opens showing the source Notebook's
  document and history instead of a blank one. Not a breaking change (fresh-notebook and
  no-document-input paths are preserved).
