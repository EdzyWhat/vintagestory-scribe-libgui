## Why

Cooked-meal Handbook pages (e.g. Vegetable Stew) have **zero** "Add to Scribe" presence — no Link,
no Tracker, no Craft link — while ordinary item pages and even mixed/processed items (e.g. Soaked
Hide) do. The reason: cooked meals render through a different page class, `GuiHandbookMealRecipePage`,
whose `GetPageText` never calls `CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo` — the
method our item-page patch (`ScribeHandbookPatch`) and the resulting Add-to-Scribe section hang off.
So the section simply never renders on a meal page. (Playtest 6.4.)

The fix is small because the plumbing already exists. Scribe already models a **guide-page Link** — a
`page:`-prefixed reference to a raw Handbook page with a captured title, used today for craftinginfo
articles — with full Core storage (`ScribeDocument.AddGuideLink`, `ScribeLinkTarget.ForPage`),
display (a book glyph + title), open-by-page-code navigation, and a Handbook creation gesture
(`ScribeGuidePageHandbookPatch` on `GuiHandbookTextPage`). A cooked meal is exactly that shape: it has
no stable countable item and is not a grid recipe, but it has a stable Handbook page code
(`handbook-mealrecipe-<code>`). So the right presence for a meal page is a single **Add Link** that
creates a guide-page Link to the meal's recipe page.

## What Changes

- **Add a Harmony postfix on `GuiHandbookMealRecipePage.GetPageText`** that appends the same "Add to
  Scribe" heading + single "Add Link" action the guide-page patch uses, wired to
  `ScribeModSystem.AddGuideLinkFromHandbook(pageCode, title)`. This gives cooked meals (and pies —
  same page class) Scribe presence.
- The Link's target is the meal page's `PageCode` (`handbook-mealrecipe-<recipe.Code>`), stored as a
  guide-page Link; the label is the meal page's `Title`. **The meal page's `Title` is already
  `Lang.Get`-resolved in its constructor** (unlike `GuiHandbookTextPage`, whose `Title` is a raw lang
  key that the guide patch resolves), so the meal patch stores `Title` as-is and does NOT re-resolve.
- **No Tracker and no Craft link on meals** — deliberately. A meal bowl's contents are randomized per
  instance (no stable code to count), and meals are cooking recipes, not grid recipes (our Craft probe
  reads grid recipes only). This mirrors the guide-page patch's existing "nothing to count → Link
  only" rationale.
- No Core change (all creation/storage/display already exists), no new capability plumbing, no new
  dependency (Harmony + the survival mod ship with the base game, same posture as the two existing
  handbook patches).

## Capabilities

### New Capabilities
_(none)_

### Modified Capabilities
- `link-task`: cooked-meal (and pie) Handbook pages gain an "Add Link" action that creates a
  guide-page Link to the meal's recipe page, so meals have the same reference-into-Scribe presence
  that item and guide pages already have.

## Impact

- **New file `src/Mod/ScribeMealPageHandbookPatch.cs`**: a client-side postfix on
  `GuiHandbookMealRecipePage.GetPageText`, discovered by the existing shared `PatchAll` in
  `ScribeModSystem.StartHandbookPatch` (no registration change needed — `PatchAll` picks up new
  `[HarmonyPatch]` types automatically). Modeled on `ScribeGuidePageHandbookPatch`.
- Possibly one new lang key if the meal action needs a distinct label; expected to reuse the existing
  `scribe:scribe-gui-addlink` / `scribe:scribe-gui-additem-heading` keys (no new strings).
- No Core change, no codec/persistence change (guide-page Links already round-trip), no VS API surface
  change. Item and guide pages are untouched — only the meal page class gains the section.
