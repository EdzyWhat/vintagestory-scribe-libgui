## Why

The Scriptorium block already ships (block, model, recipe, and the five standard tabs), but it has
no place to *hold Scribe items*. The v1.2 phasing wants two features that both need a physical slot
substrate on this block: **copy/paste** ("move a task item-to-item, like putting papers in a
drawer") and **JSON/CSV import/export** (round-trip a document off a physical item). Rather than
build those interactions and their storage in one large change, this change lands the foundation
first — a small, Scribe-items-only inventory surfaced as its own tab — so copy/paste and
import/export can each split off cleanly on top of it.

## What Changes

- Add a small **server-authoritative inventory** (2 slots) to `BlockEntityScriptorium` that accepts
  **only Scribe document items** (`IScribeDocumentItem` — Notebook, Clockmaker's Notebook, Tablet).
  Non-Scribe items are rejected at the slot.
- Surface that inventory as a **new nav-rail tab** in `GuiDialogScribeScriptorium` only (a new
  `ScribeLecternView` value + switch handler + nav button + build method). The Lectern and the
  item-hosted surfaces do not gain it.
- Items **physically move** in and out via the normal VS slot interaction (the player's held/cursor
  stack), so a stored Notebook is the same ItemStack — document and all — that was placed. This is
  the substrate the later copy/paste gesture rides on.
- Persist and sync the inventory via the **vanilla Sign pattern** the block already uses
  (`ToTreeAttributes`/`FromTreeAttributes`, `MarkDirty`, server-authoritative), and **drop the
  stored items** when the block is broken (like a container), so no Scribe document is ever
  destroyed by breaking the block.
- Add lang + a Handbook line describing the new tab.

Explicitly **out of scope** (each a planned follow-on change on top of this substrate):
- The **copy/paste transfer semantics** — pasting a document from a stored item into the
  Scriptorium's own document, or item-to-item. This change only stores and returns whole items.
- **JSON/CSV import/export.**
- Any **assignment / Inbox** surfaces (v1.3).

## Capabilities

### New Capabilities
- `scriptorium-inventory`: the Scriptorium's Scribe-items-only slot inventory and its dialog tab —
  slot count and accept-filter, server-authoritative put/take, persistence + sync via the Sign
  pattern, drop-on-break, and the nav-rail tab that shows it. Behavior is storage only; it does not
  read or merge the stored items' documents.

### Modified Capabilities
<!-- None. The block itself (scriptorium-block), the dialog shell (scribe-dialog-base), and the
     document model (task-note-document) are unchanged at the requirement level — this change adds
     a new, self-contained capability rather than altering their contracts. -->

## Impact

- **Code:** `BlockEntityScriptorium` (add the inventory + persistence + drop-on-break),
  `BlockScriptorium` (container break behavior if not already inherited),
  `GuiDialogScribeScriptorium` (new nav button), `ScribeDialogBase` (new `ScribeLecternView` value,
  switch handler, and build method for the inventory view), plus lang/handbook assets.
- **Persistence/sync:** additive tree-attribute keys; no codec bump (the Scribe *document* format is
  untouched — this stores whole ItemStacks, not document bytes). Old placed Scriptoria load with an
  empty inventory.
- **Core:** none. `src/Core/` is not touched — this is entirely a Mod-layer (VS API) feature.
- **Dependencies:** none. Vanilla `VintagestoryAPI` inventory types only.
- **Open design risk (for design.md):** hosting real VS `ItemSlot`s and their drag/cursor
  interaction inside a **LibGUI** dialog (the vanilla slot-grid + `InventoryManager` linkage vs.
  LibGUI's own input handling). This is the load-bearing unknown and must be resolved in design by
  reading the shipped DLLs before implementation.
