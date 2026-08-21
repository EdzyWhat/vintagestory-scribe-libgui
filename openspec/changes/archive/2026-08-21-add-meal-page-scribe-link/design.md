## Context

Three Handbook page classes exist for our purposes:
- `GuiHandbookItemStackPage` — ordinary item/block pages. Its text is built by
  `CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo`, which `ScribeHandbookPatch` postfixes
  to add Add Tracker / Add Link / Add Crafting Task.
- `GuiHandbookTextPage` — guide/explainer (craftinginfo) pages. No item behind them; its `Init`
  rebuilds `comps`, which `ScribeGuidePageHandbookPatch` postfixes to add a single Add Link
  (guide-page Link).
- `GuiHandbookMealRecipePage` — cooked-meal and pie pages. **Neither patched method runs for it.** Its
  text comes from its own `protected virtual RichTextComponentBase[] GetPageText(...)`, invoked by
  `ComposePage`. So meals currently get no Scribe section at all (playtest 6.4).

Verified against the decompiled `GuiHandbookMealRecipePage` (`VSSurvivalMod.dll`, `GameContent`):
- `public CookingRecipe Recipe` (has `.Code`, e.g. `vegetablestew`).
- `pageCode = "handbook-mealrecipe-" + recipe.Code + (isPie ? "-pie" : "")`, exposed as
  `public override string PageCode => pageCode`.
- `Title = Lang.Get(...)` — **already resolved to a display string in the constructor** (contrast
  `GuiHandbookTextPage.Title`, which is a raw lang key the guide patch feeds through `Lang.Get`).
- `dummySlot.Itemstack` is a `BlockMeal.RandomMealBowl` whose contents are randomized per render — not
  a stable countable target.
- `GetPageText` builds a fresh array via a local `List` every call, so appending in a postfix cannot
  accumulate duplicates across repeated opens (same dup-safety property the other two patches rely on).

The guide-page Link is already fully supported end to end: `ScribeDocument.AddGuideLink(pageCode,
label)` → `ScribeLinkTarget.ForPage` (`page:` prefix) in Core; `ScribeModSystem.AddGuideLinkFromHandbook`
→ `ScribeDialogBase.TryAddGuideLinkFromHandbook`; display via `ScribeItemRef.ResolveDisplay` (book
glyph + label) and navigation via `ScribeItemRef.OpenHandbookPage` → `OpenHandbookByPageCode`.

## Goals / Non-Goals

**Goals:**
- A cooked-meal (and pie) Handbook page shows an "Add to Scribe" section with a single Add Link action,
  matching the guide-page page's look and spacing.
- Clicking it creates a guide-page Link to `handbook-mealrecipe-<code>` labeled with the meal's title;
  the resulting Link row displays the title and, when opened, navigates back to that meal recipe page.

**Non-Goals:**
- Any Tracker on meals (no stable per-instance countable item).
- Any Craft link on meals (meals are cooking recipes; the Craft probe is grid-only). A future
  cooking-recipe Craft is out of scope.
- Touching the item-page or guide-page patches, or any Core code.

## Decisions

**D1 — Patch `GuiHandbookMealRecipePage.GetPageText` via a postfix that edits `__result`.** Prefer
`GetPageText` (returns the component array) over `ComposePage` so we append to `ref
RichTextComponentBase[] __result`, exactly as `ScribeHandbookPatch` does on `GetHandbookInfo` — cleaner
than the guide patch's `___comps` field-ref because the array is the return value here. `GetPageText`
is `protected virtual`; Harmony patches it by `[HarmonyPatch(typeof(GuiHandbookMealRecipePage),
"GetPageText")]` (string name resolves non-public methods). The new file is auto-discovered by the
existing `handbookHarmony.PatchAll(assembly)` — no registration change.

**D2 — Store `PageCode` as the guide target; store `Title` verbatim as the label.** `pageCode =
__instance.PageCode`; bail if empty. `title = __instance.Title` — used AS-IS, because the meal page
already resolved it (re-`Lang.Get`-ing an already-resolved string would fail to find a key and echo the
string back at best, or blank it). This is the one deliberate divergence from
`ScribeGuidePageHandbookPatch`, and it is the whole reason for a separate patch rather than folding the
meal type into the guide patch.

**D3 — Link only; reuse existing labels.** Append the same trio the guide patch uses: a
`ClearFloatTextComponent(14f)` gap, a bold `scribe:scribe-gui-additem-heading`, and one
`LinkTextComponent(scribe:scribe-gui-addlink)` calling `modSystem.AddGuideLinkFromHandbook(pageCode,
title)`. No new lang keys. No Tracker/Craft (D-Non-Goals).

**D4 — Guard defensively.** No-op on null `capi`/`__result`, empty `pageCode`, or a missing
`ScribeModSystem` — identical guard posture to the two sibling patches, so a meal page with the mod
half-initialized is byte-identical to vanilla.

## Risks / Trade-offs

- **`GetPageText` is protected/virtual and internal-ish API.** It ships with the base game (same
  posture as our existing `GetHandbookInfo`/`Init` patches) and is stable page-builder shape; if it
  changed, the Atlas/handbook smoke would surface it. Acceptable and precedented.
- **Pie pages share the class.** The postfix will also add the link to pie pages (`isPie`), whose
  `PageCode` carries the `-pie` suffix and whose `Title` is likewise pre-resolved — this is correct and
  desirable (pies are meals too), not a bug.
- **Title already-resolved divergence.** If a future VS version made `GuiHandbookMealRecipePage.Title`
  a raw key like the text page, our label would then be a raw key. Low likelihood; caught immediately
  in the §3 in-game check (the Link row would show a `mealrecipe-name-…` key instead of a name). Noted
  in a `VSAPI-NOTES.md` line so the divergence is discoverable.
