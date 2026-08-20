## Why

A Crafting Task auto-generates a Tracker subtask for every counting ingredient of its recipe — but a
**liquid** ingredient (the black dye an ink-and-quill needs, the water a poultice needs) is currently
surfaced only as a non-counting **Text note** ("Also needs Black Dye"). The player gets no have/need
counter for it, so the one part of the recipe that is easiest to overlook — do I actually have enough
dye? — is the one part the task can't track. The recent `fix-recipe-variant-identity` change added
`ScribeCraftRecipeProbe.TryAddLiquidNote`, which correctly *identifies* the liquid from the recipe's
`liquidContainerProps.requiresContent`; this change upgrades that identification into a real, counting
Tracker.

## What Changes

- **Liquid ingredients become counting Tracker subtasks instead of notes.** A recipe that requires
  1 L of black dye generates a depth-1 Tracker child with target **1** (litres) for `game:blackdye`,
  exactly like the solid ingredient children — with a live have/need counter, completion state, and
  the player's tracker-completion setting applied on fill.
- **The counted unit is litres, rounded up.** The recipe's per-craft litre requirement
  (`requiresLitres`, a float) is scaled by the batch (`craftsNeeded`) and the **total** is rounded up
  to the nearest whole litre (`ceil`) to form the integer Tracker target. Ceiling the batch total —
  not each craft — keeps fractional-litre recipes from over-counting.
- **Liquid quantity is counted from carried containers, not loose stacks.** A liquid never exists as a
  loose carryable itemstack — it lives as `WaterTightContainable` content *inside* a bucket/bowl. The
  carried-inventory scan gains a liquid path: for each carried liquid container, read its content
  stack, and if it matches the tracked liquid, sum its litres (`content.StackSize / ItemsPerLitre`).
- **Graceful fallback to the old note.** When the liquid can't be resolved to a concrete item/block
  (a wildcard code, a missing `requiresLitres`, an unresolvable stack), the ingredient degrades to the
  existing non-counting Text note rather than producing a broken tracker.
- No new mod dependency; no change to the network/persistence contract (a liquid child is an ordinary
  `Tracker` block — its litre semantics are a Mod-side counting detail, not a new persisted kind).
- **Supersedes** `fix-recipe-variant-identity`'s "Liquid and container ingredients are noted, not
  mis-counted" behavior: the same liquids are now *tracked* rather than *noted* (the note becomes the
  fallback path only).

## Capabilities

### New Capabilities
_(none)_

### Modified Capabilities
- `craft-task`: a liquid ingredient of a Crafting Task's recipe is now generated as a counting Tracker
  subtask (litre-based, ceil-rounded target, counted from carried liquid containers) instead of a
  non-counting note; the note remains only as the unresolvable-liquid fallback.

## Impact

- **Core (`src/Core/`, no VS API):** `ScribeCraftIngredient` gains liquid metadata (an `IsLiquid` flag
  and a per-craft litre amount); `ScribeCraftMath` gains a pure ceil-litres-to-target helper;
  `ScribeDocument.ReconcileCraftIngredients` computes a liquid child's target via that helper instead
  of the integer per-craft multiply. All pure BCL — unit-tested in `tests/Core.Tests`.
- **Mod (`src/Mod/`):** `ScribeCraftRecipeProbe` — `TryAddLiquidNote` becomes a liquid *ingredient*
  emitter (with note fallback), reading `requiresContent.code`/`type` + the sibling `requiresLitres`.
  `ScribeTrackerCounter.CountCarried` gains a liquid-container litre-summing branch, so both the
  read-view count engine (`ScribeDialogBase.TrackerCount`) and the HUD count engine pick it up for free.
- No change to `ScribeBlockKind`, any codec, or any network message.
