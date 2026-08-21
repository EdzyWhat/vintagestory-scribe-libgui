## Context

Scribe identifies every item-task target by `Collectible.Code.ToString()`. That is lossless for
items whose identity is fully in the code (`game:ingot-copper`), but **lossy** for items that encode
identity in `ItemStack.Attributes`: lanterns store `material`/`glass`/`lining` there (the block code
is only `lantern-{size}-{position}`), and meals, tool-heads, etc. are the same shape.

Three call sites drop the attributes:
- `ScribeHandbookPatch.Postfix` (`:40`) reduces the handbook item to `inSlot.Itemstack.Collectible.Code.ToString()`.
- `ScribeCraftRecipeProbe.ProbeVariants` (`:58`) re-resolves that bare code to an attribute-less stack.
- `ScribeItemRef.ResolveStack` (`:20-33`) builds `new ItemStack(item/block)` with no attributes.

Consequences (both confirmed by decompiling the game): the recipe probe's
`recipe.Output.ResolvedItemStack.Satisfies(stack)` never matches a lantern recipe (the output carries
`material=copper`; our stack carries none) → **no "Add Crafting Task" link**; and
`BlockLantern.GetHeldItemName` (`:384-395`) builds the lang key from the `material` attribute, so an
attribute-less stack yields the unmatched key `game:block-lantern-small-up-` rendered verbatim → the
**raw fallback name**.

The Tallybook mod solves the identical problem by persisting `Code` + `IsBlock` +
`TreeAttribute.ToJsonToken()` and rebuilding the stack via `TreeAttribute.FromJson`
(`Pin`/`TallyService.Resolve`). Both API methods exist and round-trip
(`TreeAttribute.ToJsonToken():913`, `FromJson():918`). The handbook page the user is on is *already*
a per-variant page (`GuiHandbookItemStackPage.PageCodeForStack` folds non-ignored attributes into the
page code, so each metal lantern is its own page), and the postfix already receives the fully
attributed `inSlot.Itemstack` via the page's `DummySlot` — we simply stop discarding it.

## Goals / Non-Goals

**Goals:**
- An "Add Crafting Task" link appears for attribute-encoded items with grid recipes (lanterns).
- A Link/Tracker/Craft task for an attribute-encoded item shows the correct name, opens the correct
  Handbook page, and (Tracker) counts the exact variant.
- `Core` stays VS-API-free: the target remains one opaque plain string; all encode/decode is Mod-side.
- Fully backward-compatible: an existing bare code resolves exactly as today; no save migration.

**Non-Goals:**
- No liquid *counting* — liquid ingredients stay a non-counting note (crafting-task D7 stands).
- No change to how fully-code-identified items behave (their encoded form is byte-identical to today).
- No new dependency; no `Core` change; no change to the recipe signature format (D3).

## Decisions

**Decision: A Mod-layer codec packs `(code, isBlock, meaningful-attributes)` into one string; `Core`
keeps storing an opaque string.** The `Core` invariant forbids VS types in `Core`, not a richer
string — the target field stays `string`. `ScribeItemRef` gains `Encode(ItemStack)` /
`ResolveStack(world, encoded)`:
- **Encode:** clone `stack.Attributes`, remove every `GlobalConstants.IgnoredStackAttributes` key plus
  `durability`, `SortedCopy(true)` for determinism, and `ToJsonToken()` the result. If the remaining
  tree is empty → emit the **bare code** (`stack.Collectible.Code.ToString()`), identical to today, so
  common items are unchanged. Otherwise emit `stack@<code>|<b|i>|<base64(attrJson)>`.
- **Decode:** a `"page:"`-prefixed guide code stays the existing guide-page path; a `"stack@"`-prefixed
  code is split into its three fields (base64 and codes contain no `|`), the collectible is resolved
  from the item or block registry per the `b`/`i` flag, and `stack.Attributes =
  (ITreeAttribute)TreeAttribute.FromJson(attrJson)`; anything else is a **legacy bare code** resolved
  as today.
- *Why strip `IgnoredStackAttributes` + `durability` (divergence from Tallybook, which serializes the
  whole tree):* it keys identity on the meaningful variant attributes (material/glass/lining) and
  ignores durability/temperature/transient noise — matching your "exact variant, not noise-sensitive"
  intent, and mirroring exactly what `PageCodeForStack` strips internally.
- *Why `stack@` marker + base64:* the marker can't collide with a bare `domain:path` code or the
  `page:` guide prefix; base64 makes the attribute blob opaque and delimiter-safe through
  `ToTreeAttributes` persistence and the HUD.

**Decision: `ProbeVariants` takes the attributed `ItemStack`, not a code.** Change the signature to
`ProbeVariants(ICoreClientAPI capi, ItemStack stack)` and have `ScribeHandbookPatch` pass
`inSlot.Itemstack` (the page's attributed stack). `MatchingRecipes` then runs the unchanged
`Output.ResolvedItemStack.Satisfies(stack)` against a stack that actually carries the attributes, so
lantern recipes match — the same input vanilla's "Created by" uses. Label/signature/ingredient
derivation is untouched. This alone fixes Fix A and needs no codec.

**Decision: the craft link stores the recipe's resolved *output* stack, encoded.**
`AddCraftFromHandbook` already binds a signature that re-resolves the exact recipe; additionally encode
the recipe's `Output.ResolvedItemStack` via the codec as the parent's target, so a Craft parent for a
lantern names correctly too (same resolution path as Link/Tracker).

**Decision: exact-variant Tracker matching keys on the decoded target's attributes.** In
`ScribeTrackerCounter`, when the resolved target stack carries attributes, a carried stack counts only
when its collectible matches **and** it satisfies the target's stored (non-ignored) attributes; a bare
code keeps matching by collectible/wildcard as today. This makes a copper-lantern Tracker count copper
lanterns specifically (your decision), and leaves every existing Tracker's behavior unchanged.

**Decision: `OpenHandbookPage` prefers `IHandBookPageCodeProvider`.** When the resolved collectible
implements `IHandBookPageCodeProvider` (e.g. `BlockMeal`), call
`HandbookPageCodeForStack(world, stack)` for the navigation target; otherwise fall back to
`GuiHandbookItemStackPage.PageCodeForStack(stack)`. Guarded (interface probe + null check) so a
collectible that throws or returns null degrades to the existing behavior — a cheap correctness win
for meals, borrowed from Tallybook.

## Risks / Trade-offs

- **Encoded-string collision with the `page:` guide-link scheme** → the decoder checks guide-page
  first, then the `stack@` marker, then legacy; `stack@` cannot appear in a bare code or a `page:` code.
- **Non-deterministic attribute order would fragment identity and recipe signatures** → `SortedCopy(true)`
  before `ToJsonToken()`, matching `PageCodeForStack`'s determinism.
- **`Satisfies`/attribute-match direction for the exact-variant count is subtle** → verify in-game that a
  copper-lantern Tracker counts copper lanterns and ignores iron; a listed gate. If `Satisfies` proves
  wrong-directioned, fall back to an explicit "carried has every stored attribute key = value" check.
- **`IHandBookPageCodeProvider.HandbookPageCodeForStack` could throw or return an unopenable code for an
  odd collectible** → guarded probe with fallback to `PageCodeForStack`; never worse than today.
- **Craft parent already stored a bare output code in existing saved docs** → legacy decode path keeps
  them working (they just keep the old name until recreated); no migration, no crash.
- **Encoded strings are longer** → negligible for persistence/HUD; only attribute-encoded targets carry
  the blob, and only its meaningful keys.

## Open Questions

- None blocking. The precise exact-variant matcher (`Satisfies` vs. explicit attribute-subset check) is
  resolved during apply against the in-game count gate; the requirement is only that a copper-lantern
  Tracker counts copper lanterns and not other metals.
