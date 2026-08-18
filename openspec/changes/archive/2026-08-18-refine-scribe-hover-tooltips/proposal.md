## Why

Two hover-related rough edges surfaced once the Scriptorium's item-storage tab shipped:

1. **Hover tooltips ignore the local-illumination pass.** The `respect-local-illumination` change
   shades the whole dialog body by the real light reaching the player, but LibGUI tooltips render in
   the Overlay layer — *outside* the tinted body subtree — so in medium/low light they render at full
   brightness and visibly "stick out" of the surface they belong to. This breaks the canonical-lighting
   illusion the illumination pass exists to create.
2. **The item-slot hover is a massive, generic panel.** Hovering an item in the Scriptorium inventory
   pops LibGUI's stock `ItemTooltipContent` (hard-coded `MaxWidth = 350f`, full vanilla name +
   description + durability + quantity). It's oversized for a two-slot Scribe shelf and tells you
   nothing Scribe-specific — it can't even show what document the item carries.

Fixing both now (a) restores lighting consistency across every Scribe hover surface and (b) turns the
item hover into a **document summary card** that previews exactly what a copy/import/export gesture
would act on — directly de-risking the next change (Scriptorium copy/paste).

## What Changes

- **Route every Scribe-owned hover tooltip through the illumination pass**, at a slightly reduced
  "hover strength" (90% of the body's darkening — 10% less darkening, for legibility). Covers the
  nav-button hovers, title-bar (title-edit / drag-grip / close) hovers, the editor info & gear hovers,
  and the Pinned-tab policy caption. Each wraps its tooltip content in `ScribeGlobalTint` built from the
  live shade, mirroring the existing drop-up-menu overlay precedent (`ScribeAddKindPicker`).
- **Explicitly exclude Scribe Settings.** Its dialog is un-shaded by construction (bare global theme,
  no `ScribeGlobalTint` wrap), so its two help tooltips stay at canonical lighting — no change there.
- **Replace the Scriptorium inventory item-slot hover** with a compact, Scribe-specific summary card:
  the item/block name, the document **Title**, a per-type count of the document's contents (tasks /
  notes / trackers / links), and a distinct **"crafted but never opened"** state when the stack carries
  no document yet. Because LibGUI bakes its tooltip into `ItemSlotGestureLayer` with no injection point,
  this means composing a small custom slot (item render via `ItemSlotOverlay`, gestures forwarded to the
  existing `SlotController`, our own tooltip) rather than the stock `FlatItemSlot`.
- The custom card's content set is chosen to be the **preview surface for copy/import/export**, so the
  copy/paste change extends this same card instead of inventing another.

## Capabilities

### New Capabilities
- `scribe-item-hover-summary`: the compact document-summary hover card for Scribe item stacks (name,
  title, per-type content counts, never-opened state), replacing the stock LibGUI item tooltip in the
  Scriptorium inventory slots; and the custom slot composition that hosts it.

### Modified Capabilities
- `gui-ambient-illumination`: the illumination pass now also shades Scribe **hover/overlay surfaces**
  (previously only the dialog body), at a reduced hover strength, with Scribe Settings excluded.

## Impact

- **New Mod code:** a custom slot widget + a tooltip-content builder that reads document metadata from a
  stack (`ScribeDocumentAttributes.TryReadFrom` → `ScribeDocument.Blocks` grouped by `ScribeBlockKind`).
- **Touched Mod code:** `ScribeDialogBase.Layout.cs` (`WithTooltip` gains a shade wrap), the inline
  `Tooltip` sites in `ScribeEditorContent`/`ScribePinnedContent`, `GuiDialogScribeScriptorium` (swap
  `FlatItemSlot` → custom slot), and a small shade-strength helper on `ScribeGlobalTint`/the shade type.
- **No `src/Core` change** (counts are read from the existing `ScribeDocument` model; no VS API added).
- **No new dependencies.** Reuses LibGUI's public `SlotController`/`ItemSlotOverlay`/`Tooltip` API.
- **Persistence/sync:** none — this is presentation only.
- **Excluded:** Scribe Settings hovers; the actual copy/paste transfer semantics (next change).
