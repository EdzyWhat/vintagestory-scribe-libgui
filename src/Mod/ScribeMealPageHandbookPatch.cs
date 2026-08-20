using HarmonyLib;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Scribe;

/// <summary>
/// Client-side Harmony postfix that appends a single "Add Link" clickable link to the bottom of every
/// cooked-meal (and pie) Handbook page (add-meal-page-scribe-link, playtest 6.4). Cooked meals render
/// through <see cref="GuiHandbookMealRecipePage"/>, a different page class than ordinary items — its text
/// is built by <c>GetPageText</c>, which NEVER calls
/// <c>CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo</c> (the method the item-page patch
/// <see cref="ScribeHandbookPatch"/> hangs off). So meals otherwise get no "Add to Scribe" section at all.
///
/// <para>A meal has no stable countable item (its bowl's contents are randomized per instance) and is not a
/// grid recipe, so — exactly like the guide/explainer patch (<see cref="ScribeGuidePageHandbookPatch"/>) — a
/// <b>Link</b> is the only sensible action (nothing to count for a Tracker; the Craft probe is grid-only).
/// The link stores the meal's <see cref="GuiHandbookMealRecipePage.PageCode"/>
/// (<c>handbook-mealrecipe-&lt;code&gt;</c>) as a <c>page:</c>-prefixed guide Link and its title as the label.</para>
///
/// <para><b>The one deliberate divergence from the guide patch</b> (and the reason meals need their own patch
/// rather than folding into it): the meal page's <see cref="GuiHandbookMealRecipePage.Title"/> is ALREADY
/// <c>Lang.Get</c>-resolved in its constructor, whereas <c>GuiHandbookTextPage.Title</c> is a raw lang key the
/// guide patch feeds through <c>Lang.Get</c>. So this patch stores <c>Title</c> VERBATIM — re-resolving an
/// already-resolved string would fail to find a key and blank/echo it. See VSAPI-NOTES.md.</para>
///
/// <para><c>GetPageText</c> is overloaded on this class (a 3-arg <see cref="RichTextComponentBase"/>[] builder
/// and a no-arg <c>PageText</c> summary); we target the 3-arg builder that produces the visible rich text, so
/// the patch attribute names its parameter types explicitly to disambiguate. The builder allocates a fresh
/// array each call, so appending in a postfix can never accumulate duplicates across repeated opens. Harmony +
/// the survival mod both ship with the base game (no new dependency); the file is auto-discovered by the shared
/// <c>PatchAll</c> in <see cref="ScribeModSystem.StartHandbookPatch"/>.</para>
/// </summary>
[HarmonyPatch(typeof(GuiHandbookMealRecipePage), "GetPageText",
    new[] { typeof(ICoreClientAPI), typeof(ItemStack[]), typeof(ActionConsumable<string>) })]
internal static class ScribeMealPageHandbookPatch
{
    /// <summary>Append the "Add Link" action to a meal page's rich-text component array. Runs after the meal
    /// page's <c>GetPageText</c> builds <paramref name="__result"/>. The injected <paramref name="capi"/> binds
    /// to the original method's <c>capi</c> argument by name. Guards mirror the sibling patches so a meal page
    /// with the mod half-initialized is byte-identical to vanilla.</summary>
    // ReSharper disable once InconsistentNaming — Harmony injected parameter names are fixed.
    private static void Postfix(GuiHandbookMealRecipePage __instance, ICoreClientAPI capi, ref RichTextComponentBase[] __result)
    {
        if (capi is null || __result is null) return;

        string? pageCode = __instance.PageCode;
        if (string.IsNullOrEmpty(pageCode)) return;

        var modSystem = capi.ModLoader.GetModSystem<ScribeModSystem>();
        if (modSystem is null) return;

        // Title is ALREADY Lang.Get-resolved on the meal page (unlike GuiHandbookTextPage.Title) — store it
        // verbatim; do NOT re-resolve. This is the whole reason for a separate patch (see class remarks).
        string title = __instance.Title;
        if (string.IsNullOrEmpty(title)) title = pageCode;

        var linkFont = CairoFont.WhiteSmallText();
        var headingFont = CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold);

        var appended = new RichTextComponentBase[]
        {
            // Same spacing idiom as the item/guide pages: a small gap, a bold heading, then the single action.
            new ClearFloatTextComponent(capi, 14f),
            new RichTextComponent(capi, Lang.Get("scribe:scribe-gui-additem-heading") + "\n", headingFont),
            new LinkTextComponent(capi, Lang.Get("scribe:scribe-gui-addlink") + "\n", linkFont,
                _ => modSystem.AddGuideLinkFromHandbook(pageCode, title)),
        };

        var combined = new RichTextComponentBase[__result.Length + appended.Length];
        __result.CopyTo(combined, 0);
        appended.CopyTo(combined, __result.Length);
        __result = combined;
    }
}
