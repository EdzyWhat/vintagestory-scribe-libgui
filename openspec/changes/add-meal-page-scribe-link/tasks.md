## 1. Add the meal-page handbook patch

- [x] 1.1 Create `src/Mod/ScribeMealPageHandbookPatch.cs`: a client-side Harmony postfix
      `[HarmonyPatch(typeof(GuiHandbookMealRecipePage), "GetPageText")]` with signature
      `Postfix(GuiHandbookMealRecipePage __instance, ICoreClientAPI capi, ref RichTextComponentBase[] __result)`.
      Model it on `ScribeGuidePageHandbookPatch`, including the file-level doc comment explaining why
      meals need their own patch (different page class; pre-resolved `Title`) and the Link-only rationale.
      — DONE. IMPORTANT DEVIATION: `GetPageText` is OVERLOADED on the class (also a no-arg
      `PageText GetPageText()`), so the string-name-only attribute would be ambiguous — the
      `[HarmonyPatch]` names the 3-arg parameter types
      `new[] { typeof(ICoreClientAPI), typeof(ItemStack[]), typeof(ActionConsumable<string>) }`. Verified
      against the decompiled `GuiHandbookMealRecipePage`. Full doc comment written.
- [x] 1.2 Guard: no-op on null `capi`/`__result`, empty `PageCode`, or missing `ScribeModSystem`
      (match the sibling patches' guard posture — vanilla-identical when half-initialized).
      — DONE: all four guards present, mirroring the guide patch.
- [x] 1.3 Read `pageCode = __instance.PageCode` and `title = __instance.Title` (store the title
      VERBATIM — the meal page already `Lang.Get`-resolves it in its constructor; do NOT re-resolve).
      — DONE: `title = __instance.Title` used as-is (falls back to `pageCode` only if the title is
      empty). Confirmed against the decompile: `Title = Lang.Get("mealrecipe-name-" + recipe.Code)` in
      the ctor.
- [x] 1.4 Append the guide-patch trio: `ClearFloatTextComponent(capi, 14f)`, a bold
      `scribe:scribe-gui-additem-heading`, and one `LinkTextComponent(scribe:scribe-gui-addlink)` whose
      handler calls `modSystem.AddGuideLinkFromHandbook(pageCode, title)`. Reuse existing lang keys (no
      new strings). Copy `__result` + appended into a combined array and assign back to `__result`.
      — DONE: identical trio + combined-array reassignment; reuses existing lang keys (no new strings).

## 2. Build + tests

- [x] 2.1 `dotnet build src/Mod/Mod.csproj -c Debug` clean (0 warnings/errors); confirm the new patch is
      picked up by the existing `handbookHarmony.PatchAll(assembly)` (no registration edit needed).
      — DONE: Build succeeded, 0/0. New file is auto-discovered by the shared `PatchAll` (no
      registration edit).
- [x] 2.2 `dotnet test tests/Core.Tests/Core.Tests.csproj` green (no Core change; guide-page Link
      round-trip already covered — confirm nothing regressed).
      — DONE: no Core change; guide-page Link suite unchanged. (Same 6 pre-existing unrelated
      illumination-curve failures noted in the sibling change; Core untouched here.)

## 3. In-game verification (playtest gate)

- [ ] 3.1 In-game: open a cooked meal's Handbook page (e.g. Vegetable Stew) → an "Add to Scribe" section
      with a single Add Link appears; no Tracker/Craft action shows.
- [ ] 3.2 In-game: click Add Link with a Scribe surface open → a Link row is added showing the meal's
      title (e.g. "Vegetable Stew"), NOT a raw `mealrecipe-name-…` key or bare page code.
- [ ] 3.3 In-game: open that Link (read view / Pinned / HUD) → it navigates back to the meal recipe
      Handbook page.
- [ ] 3.4 In-game: confirm a pie page (same page class) also gets the Add Link and its title/label
      resolve correctly.
- [ ] 3.5 In-game: confirm ordinary item pages and guide/article pages are unchanged (their existing
      Add Tracker/Link/Craft sections still render exactly as before — no duplication, no loss).

## 4. Docs

- [x] 4.1 Add a `VSAPI-NOTES.md` line: `GuiHandbookMealRecipePage` is a distinct handbook page class
      whose text comes from `GetPageText` (not `GetHandbookInfo`), and whose `Title` is ALREADY
      `Lang.Get`-resolved in its ctor (unlike `GuiHandbookTextPage.Title`, a raw key) — so a meal-link
      patch stores the title verbatim. This is the trip-wire if the Link row ever shows a raw key.
      — DONE: covered in the shared "Handbook variant identity + the three page classes" section added by
      the sibling `fix-recipe-variant-identity` change (documents all three page classes, the overload
      disambiguation gotcha, and the pre-resolved-Title trip-wire).
