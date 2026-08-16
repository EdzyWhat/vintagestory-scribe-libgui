## Why

The Scriptorium gained a two-slot, Scribe-items-only inventory (`add-scriptorium-inventory`) whose
sole purpose is a copy/paste gesture, but nothing yet copies a document from one note to another —
the slots just hold items. The v7 spec calls for duplicating a note's tasks item-to-item ("feels
like putting papers in a drawer") and, later, JSON/CSV import/export for round-tripping documents
out of a save. This change delivers the working copy/paste and lays out the import/export controls
structurally (unwired) so the interface is designed around them from the start rather than retrofitted.

## What Changes

- **Rename the Scriptorium's "Inventory" view to "Transcribe"** (modifies the `scriptorium-inventory`
  tab requirement). It was never storage — it is a place for copying documents and taking the copies
  back out. The nav-button tooltip and section heading become "Transcribe" (new lang key).
- **Document copy between the two slots.** With an Original note in the left slot and a target note in
  the right ("Duplicate") slot, a **"Stamp to copy"** action duplicates the Original's `ScribeDocument`
  onto the target via the existing `ScribeDocumentAttributes` byte-copy path. The Original is never
  consumed or modified.
- **Paste-over rule.** If the Duplicate slot's item has no contents, the copy proceeds silently. If it
  already has tasks, the first press swaps the button to a red **"Stamp again to overwrite N tasks"**
  inline confirm; a second press commits. No separate modal dialog.
- **Wax-seal stamp affordance + animation.** The copy button is dressed as a wax seal. Pressing it
  plays a short button-triggered 2D press animation (scale + slight tilt + fade) that leaves a brief
  imprint on the Duplicate slot, then the copied summary card populates it. Built as a **reusable
  animation component** (one 2D seal image asset + paint-transform code on the existing row-animation
  harness). The feature is fully functional if the animation is stripped — it is a flourish over a
  working button, never load-bearing.
- **Import/Export placeholder section.** A separate section below the copy pair with a **rendered
  placeholder slot** ("note to export from / import into") and disabled **Export JSON**, **Export CSV**,
  and **Import…** buttons carrying a "coming in a later update" tooltip. Structural only — no wiring, and
  crucially **no new persisted block-entity slot**: the placeholder slot becomes a real slot when
  import/export is actually wired, so the Scriptorium's inventory stays at its two real (copy) slots this
  change and no save-migration is needed.
- **Explicitly deferred:** the actual JSON/CSV import/export wiring **and** the functional import/export
  slot it needs (growing the block-entity inventory arrives with the wiring); and reusing the stamp
  animation for checkbox completion in read/edit/pinned views (the per-row-in-a-scroll-list placement is
  the hard part and warrants its own change).

## Capabilities

### New Capabilities

- `transcribe-copy-paste`: The Scriptorium's Transcribe view — a two-slot document-copy gesture with
  the empty/overwrite paste-over rule, a reusable wax-seal stamp affordance and press animation, and a
  structurally-present-but-unwired import/export section (a placeholder slot + disabled JSON/CSV/Import
  controls).

### Modified Capabilities

- `scriptorium-inventory`: the requirement that surfaces the inventory as its own dialog tab is modified
  so the tab is labeled **"Transcribe"** (was "Inventory"). No other `scriptorium-inventory` requirement
  changes — it stays exactly two real slots, Scribe-only, storage-only substrate.

## Impact

- **Builds directly on `scriptorium-inventory`** (just archived): reuses its `BlockEntityScriptorium`
  `InventoryGeneric` (two slots), `ItemSlotScribeDocument` filter, `ScribeDocumentSlot` widget, and
  `SlotController` wiring. No new persisted slot this change — the import/export slot is a rendered
  placeholder, so **no `InventoryGeneric` resize / save-migration**.
- **Code:** `GuiDialogScribeScriptorium.cs` (rename the view + rebuild `BuildInventoryContent` into the
  Transcribe layout), a new reusable stamp-animation widget under `src/Mod/`, and a copy operation that
  routes GUI → network channel → server (server-authoritative, mirroring the existing document-mutation
  flow). `ScribeDocumentAttributes` is the read/write path; the duplication itself clones with fresh ids.
- **Core:** the copy is a document-level clone with **fresh identity** (new `DocId` + `TaskId`s so the two
  items stay independent); this and the overwrite-prompt task-count are game-agnostic and live in
  `src/Core/`, API-free and unit-tested.
- **Assets:** one new 2D wax-seal image (AI-generated); new lang keys (`scribe-tab-transcribe`,
  stamp/overwrite/import-export strings).
- **No new mod dependencies.** Vanilla VintagestoryAPI only; persistence/sync follows the Sign pattern.
