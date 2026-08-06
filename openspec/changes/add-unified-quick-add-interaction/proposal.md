## Why

The fastest way to capture a task should be one gesture on the writing tool already in your
hand — GTD's "capture speed beats organization," the roadmap's self-described *highest-leverage
UX investment*. Today there is no quick-capture: opening any Scribe surface lands you in a view,
never on a fresh empty task with the caret ready. This change adds **quick-add** as a single,
consistent gesture across all three writing surfaces (Lectern, Notebook, Tablet), replacing the
never-built standalone "backpack + quick-add hotkey" tier with an item-contextual interaction that
always acts on the surface you are already touching.

## What Changes

- **New unified gesture, one rule everywhere:** **Shift + Right-Click = quick-add** on the Lectern,
  the Notebook, and the Tablet. Quick-add opens the surface's editor, inserts a new empty task at the
  **top** of the document, and focuses the text caret on it, ready to type.
- **Lectern (BREAKING gesture change):** Shift+Right-Click currently opens the plain Editor view; it
  now performs quick-add. Opening the editor *without* adding a task moves to the existing Editor
  **nav tab** (reached via a plain right-click → Read view). Plain right-click still opens Read.
- **Held items — ground placement moves to Ctrl+Shift+Right-Click:** Notebook and Tablet ground
  storage (today gated on Shift) is remapped to **Ctrl+Shift+Right-Click**, freeing Shift+RC for
  quick-add. This follows the vanilla **spear** placement convention and is advertised through each
  item's held-interaction help text. *(Verified in `VSSurvivalMod.dll`: the ground-storable behavior
  gates placement on `ShiftKey` only and Scribe items already override `OnHeldInteractStart`, so
  Scribe owns its own modifier scheme — see design.md.)*
- **Tablet quench/soften disambiguation stays by aim:** Shift+Right-Click **aimed at a water block**
  continues to quench/soften a hard tablet; Shift+Right-Click **not** aimed at water performs
  quick-add. No new modifier — the existing water-aim branch is the discriminator.
- **Backpack item retired from the roadmap:** the portability tier's HUD already shipped; the
  backpack container is deferred/likely-cut (carrying the item is intended friction), and quick-add
  ships as this item-contextual interaction instead of a standalone hotkey.

## Capabilities

### New Capabilities
- `quick-add-interaction`: The unified Shift+Right-Click quick-add gesture — its trigger, its effect
  (open editor + insert empty top task + focus caret), and its consistency contract across all three
  Scribe writing surfaces.

### Modified Capabilities
- `lectern-block`: the block-interaction gesture map changes — plain right-click still opens Read,
  Shift+Right-Click is redefined to quick-add, and the plain-editor entry point becomes the Editor
  nav tab.
- `clay-wax-tablet-item`: held-interaction gesture map changes — ground placement moves to
  Ctrl+Shift+Right-Click, Shift+RC-not-on-water becomes quick-add, Shift+RC-on-water still quenches.
- `notebook-item`: held-interaction gesture map gains quick-add — ground placement moves to
  Ctrl+Shift+Right-Click, Shift+RC becomes quick-add (plain right-click still opens Read).

## Impact

- **Code:** `BlockScribeLectern.cs` / `BlockEntityScribeLectern.cs` (block interact branch),
  `ItemScribeTablet.cs` and `ItemScribeNotebook.cs` (`OnHeldInteractStart` modifier branches +
  `GetHeldInteractionHelp`), the shared editor entry (`ScribeDialogBase` editor-open + a
  "insert empty task at top and focus caret" seam, likely reusing the existing add-task path).
- **Lang:** new/updated held-interaction help strings and any interaction-hint tooltips in `en.json`.
- **No Core change expected:** quick-add is a Mod-side interaction that reuses the existing
  document add-task operation; `src/Core/` stays untouched.
- **No new dependencies.** Vanilla `VintagestoryAPI` only.
- **Player-facing behavior change:** existing muscle memory for Lectern Shift+RC (editor) and held-item
  Shift+RC (ground place) changes — call out prominently in the 1.0 CHANGELOG and handbook/wiki.
