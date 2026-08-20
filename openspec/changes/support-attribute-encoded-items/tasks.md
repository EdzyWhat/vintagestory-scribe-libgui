## 1. Attribute-preserving codec in `ScribeItemRef`

- [ ] 1.1 In `src/Mod/ScribeItemRef.cs`, add `Encode(ItemStack stack)`: clone `stack.Attributes`,
      remove every `GlobalConstants.IgnoredStackAttributes` key and `durability`, take `SortedCopy(true)`
      for determinism, and `ToJsonToken()` it. If the resulting tree is empty, return the bare
      `stack.Collectible.Code.ToString()` (byte-identical to today). Otherwise return
      `"stack@" + code + "|" + (isBlock ? "b" : "i") + "|" + Base64(attrJson)`.
- [ ] 1.2 Extend `ResolveStack(IWorldAccessor world, string? code)` to decode: `"page:"` guide codes
      keep the existing guide path; a `"stack@"` code splits into `code | b/i | base64(attrJson)`,
      resolves the collectible from the item or block registry per the flag, and sets
      `stack.Attributes = (ITreeAttribute)TreeAttribute.FromJson(attrJson)`; anything else is a legacy
      bare code resolved exactly as now. Guard every parse/registry miss to a null return (never throw).
- [ ] 1.3 In `OpenHandbookPage`, after resolving the stack, prefer the collectible's own page code when
      it implements `IHandBookPageCodeProvider` (e.g. `BlockMeal`) —
      `HandbookPageCodeForStack(capi.World, stack)` — falling back to
      `GuiHandbookItemStackPage.PageCodeForStack(stack)` when the interface is absent, returns null, or
      throws. Behavior for non-attribute items is unchanged.
- [ ] 1.4 Confirm `ResolveDisplay`/`DisplayName` are unchanged in shape — they now get correct names for
      attribute-encoded items purely because `ResolveStack` returns an attributed stack.

## 2. Fix A — craft links appear (recipe probe takes the attributed stack)

- [ ] 2.1 In `src/Mod/ScribeCraftRecipeProbe.cs`, change `ProbeVariants` to accept
      `(ICoreClientAPI capi, ItemStack stack)` instead of a code string; drop the internal
      `ScribeItemRef.ResolveStack(... outputItemCode)` call and match against the passed stack. Keep
      `MatchingRecipes`, `SignatureOf`, `DeriveIngredients`, and labeling unchanged. Use
      `stack.Collectible.Code.ToString()` where `outputItemCode` was used as a fallback (line ~85).
- [ ] 2.2 In `src/Mod/ScribeHandbookPatch.cs`, capture `inSlot.Itemstack` (the page's attributed
      stack). Pass it to `ProbeVariants(capi, stack)`; encode the target string once via
      `ScribeItemRef.Encode(stack)` and use that encoded string for the Tracker and Link
      `AddFromHandbook` calls (replacing the bare `itemCode`). Keep the empty-page bail.
- [ ] 2.3 For the craft links, store the recipe's resolved **output** stack encoded as the parent
      target: encode `variant`'s output stack (resolve it from `CodeOf`/the recipe output) via the codec
      so a Craft parent for an attribute-encoded item names correctly, and pass it through
      `AddCraftFromHandbook` alongside the existing signature.

## 3. Thread the encoded target through task creation

- [ ] 3.1 In `src/Mod/ScribeModSystem.cs`, confirm `AddFromHandbook(kind, target)` and
      `AddCraftFromHandbook(target, signature)` treat `target` as the opaque encoded string end-to-end
      (stored into `LinkTarget` / `TargetItemCode` unmodified). No `Core` signature change — `Core`
      still stores a plain string.
- [ ] 3.2 Verify no other caller re-parses the target as a bare `AssetLocation` in a way that would choke
      on the `stack@` form (grep `TargetItemCode` / `LinkTarget` usages); every read path must go through
      `ScribeItemRef.ResolveStack` / `ResolveDisplay` / `OpenHandbookPage`.

## 4. Exact-variant Tracker matching

- [ ] 4.1 In `src/Mod/ScribeTrackerCounter.cs`, when the resolved target stack carries attributes, count
      a carried stack only when its collectible matches **and** it satisfies the target's stored
      attributes; when the target is a bare code, keep the current collectible/wildcard match. Pick the
      matcher (`targetStack.Satisfies(carried)` vs. an explicit "carried has every stored key = value"
      check) and note the choice; the in-game gate (6.5) is the arbiter.
- [ ] 4.2 Confirm liquid/quantity semantics are untouched — this change only narrows *which* carried
      stacks match, never how quantities are summed.

## 5. Build

- [ ] 5.1 `dotnet build src/Mod/Mod.csproj` — 0 errors, 0 warnings. Qualify `System.Func`/`System.Action`
      if any new lambda needs them (the VS API also defines those).
- [ ] 5.2 `bash build/restage.sh Debug` (only while the client is NOT running).

## 6. In-game verification gates

- [ ] 6.1 In-game: open a lantern's Handbook page → an "Add Crafting Task" link is present (was absent).
      Click it → a Craft parent + ingredient subtasks appear.
- [ ] 6.2 In-game: create a Link and a Tracker from the "Copper Lantern" Handbook page → both rows read
      "Copper Lantern" (not `Game:Block-Lantern-Small-up`) and show the correct icon.
- [ ] 6.3 In-game: click the Link's/Tracker's label → the Handbook opens on the copper-lantern variant
      page (not a generic/empty page).
- [ ] 6.4 In-game: create tasks for a meal (an `IHandBookPageCodeProvider` item) → name resolves and the
      label opens the correct meal page.
- [ ] 6.5 In-game: with a copper-lantern Tracker, carry both copper and iron lanterns → only the copper
      lanterns count toward progress.
- [ ] 6.6 In-game: open a document saved before this change whose Tracker/Link targets are bare codes →
      every target still resolves, renders, and opens its page (backward-compat / no migration).
- [ ] 6.7 In-game: create a Tracker/Link for a plain code item (e.g. `game:ingot-copper`) → behavior and
      stored target are unchanged from before (bare code, not `stack@`).
- [ ] 6.8 Record verdicts in `TESTING.md` via the what-to-test skill.
