## Why

Some Vintage Story items encode their identity in **ItemStack attributes**, not in the collectible
code. A lantern's block code is only `lantern-{size}-{position}` (e.g. `lantern-small-up`); its
material, glass, and lining live in `stack.Attributes` (`BlockLantern.GetHeldItemName` builds its
name from the `material` attribute). Meals, tool-heads, and other stackable-attribute items are the
same shape.

Scribe identifies every item-task target by `Collectible.Code.ToString()` alone — the Handbook
patch (`ScribeHandbookPatch.cs:40`), the Tracker/Link target field, and the recipe probe all reduce
the item to a bare code and throw the attributes away. That one reduction causes two visible bugs on
attribute-encoded items:

1. **No "Add Crafting Task" link.** `ScribeCraftRecipeProbe.ProbeVariants` re-resolves the bare code
   to an attribute-less stack, and every lantern recipe's output carries `material`/`glass`/`lining`,
   so `recipe.Output.ResolvedItemStack.Satisfies(stack)` matches nothing → no craft link appears.
   (Vanilla's "Created by" uses the *identical* `Satisfies` test and works only because it passes the
   real attributed handbook stack — which our postfix already holds as `inSlot.Itemstack` and then
   discards.)
2. **Raw fallback name.** A Tracker/Link (and a Craft parent) for a lantern shows
   `Game:Block-Lantern-Small-up` instead of "Copper Lantern", because `ScribeItemRef.ResolveStack`
   rebuilds an attribute-less stack whose `GetName()` returns the unmatched lang key verbatim.

The Tallybook mod solved the same class of problem by persisting `Code` + `IsBlock` +
`TreeAttribute.ToJsonToken()` and rebuilding the stack with `TreeAttribute.FromJson` — confirming
the round-trip approach. We adopt that, with one deliberate divergence (strip
`GlobalConstants.IgnoredStackAttributes` + `durability` so identity keys on the meaningful variant
attributes and ignores durability/temperature noise).

## What Changes

- **Fix A — craft links appear (bug fix, no spec change to craft-task).** Feed the attributed
  `inSlot.Itemstack` into the recipe probe instead of a bare code, so `Satisfies` matches lantern (and
  other attribute-encoded) recipes and the "Add Crafting Task" link appears. Purely Mod-internal.
- **Fix B — attribute-preserving target identity.** Introduce a Mod-layer codec so a Tracker/Link/Craft
  target string can carry the item's meaningful attributes (material/glass/lining, etc.), rebuilt into
  the full stack on resolve. Attribute-encoded items then resolve to the correct **name**, the correct
  **Handbook page**, and (for Trackers) an **exact-variant** carried-inventory match. A bare legacy
  code with no attribute segment resolves exactly as today (backward compatible; no migration).
- **Exact-variant matching (your decision).** A task created from the "Copper Lantern" Handbook page
  targets copper lanterns specifically — it names correctly and a Tracker counts only copper lanterns.
- **Handbook navigation correctness (small win from Tallybook).** `OpenHandbookPage` prefers
  `IHandBookPageCodeProvider.HandbookPageCodeForStack` when the collectible provides it (meals and some
  items override their own page code), falling back to `PageCodeForStack`.

Non-goals: no liquid *counting* (liquids stay a non-counting note, per the crafting-task D7 decision);
no change to how non-attribute items behave; no new dependency; no `Core` VS-API reference (the codec
lives entirely in the Mod layer — `Core` still stores an opaque plain string).

## Capabilities

### New Capabilities
- `attribute-encoded-item-identity`: An item-task target (Link, Tracker, or Craft) preserves the
  identifying attributes of an attribute-encoded item across save/load and resolution, so it resolves
  to the correct display name, Handbook page, recipe match, and (for Trackers) exact-variant inventory
  count; a bare legacy code continues to resolve unchanged.

### Modified Capabilities
- `link-task`: Widen `LinkTarget` from "an item/block asset code" to a target identity that MAY be
  attribute-encoded (backward-compatible with a bare code), so a Link to an attribute-encoded item
  opens the correct Handbook page and shows the correct name.
- `tracker-task`: Widen `TargetItemCode` similarly, and specify that carried-inventory matching is
  **exact-variant** for an attribute-encoded target (a copper-lantern Tracker counts copper lanterns,
  not every metal), while a bare code keeps matching by collectible as before.

## Impact

- `src/Mod/ScribeHandbookPatch.cs` — capture the attributed `inSlot.Itemstack`; encode the target via
  the new codec; pass the stack (not the bare code) into the probe.
- `src/Mod/ScribeCraftRecipeProbe.cs` — `ProbeVariants` accepts an `ItemStack` (attributed) instead of
  re-resolving a bare code; output/label/signature derivation unchanged.
- `src/Mod/ScribeItemRef.cs` — new attribute-preserving encode/decode helpers; `ResolveStack` rebuilds
  attributes from the encoded string via `TreeAttribute.FromJson`; `OpenHandbookPage` prefers
  `IHandBookPageCodeProvider`.
- `src/Mod/ScribeTrackerCounter.cs` — exact-variant match when the target carries attributes; bare-code
  behavior unchanged.
- `src/Mod/ScribeModSystem.cs` (`AddFromHandbook`/`AddCraftFromHandbook`) — thread the attribute-encoded
  target string through task creation.
- No `src/Core/` change (target stays an opaque plain string), no new dependency, no persistence-format
  migration (legacy bare codes resolve as-is).
