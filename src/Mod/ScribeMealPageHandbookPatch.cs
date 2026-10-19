using HarmonyLib;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Scribe;

/// <summary>
/// Client-side Harmony postfix that appends a single "Add Link" link to every cooked-meal (and pie) Handbook
/// page. Meals render through <see cref="GuiHandbookMealRecipePage"/>, whose <c>GetPageText</c> never calls the
/// method <see cref="ScribeHandbookPatch"/> hooks, so they need their own patch.
///
/// <para>A meal has no stable countable item and is not a grid recipe, so a Link is the only sensible action,
/// as in <see cref="ScribeGuidePageHandbookPatch"/>. It stores the meal's
/// <see cref="GuiHandbookMealRecipePage.PageCode"/> as a <c>page:</c>-prefixed guide Link.</para>
///
/// <para>Unlike the guide patch, the meal page's <see cref="GuiHandbookMealRecipePage.Title"/> is already
/// <c>Lang.Get</c>-resolved, so it is stored verbatim; re-resolving would blank or echo it. See VSAPI-NOTES.md.</para>
///
/// <para><c>GetPageText</c> is overloaded, so the attribute names the 3-arg builder's parameter types. The builder
/// allocates a fresh array per call, so the postfix cannot accumulate duplicates across opens. Auto-discovered by
/// the <c>PatchAll</c> in <see cref="ScribeModSystem.StartHandbookPatch"/>.</para>
/// </summary>
[HarmonyPatch(typeof(GuiHandbookMealRecipePage), "GetPageText",
    new[] { typeof(ICoreClientAPI), typeof(ItemStack[]), typeof(ActionConsumable<string>) })]
internal static class ScribeMealPageHandbookPatch
{
    /// <summary>Append the "Add Link" action to <paramref name="__result"/>. <paramref name="capi"/> binds to the
    /// original method's argument by name. The guards leave the page vanilla if the mod is half-initialized.</summary>
    // ReSharper disable once InconsistentNaming — Harmony injected parameter names are fixed.
    private static void Postfix(GuiHandbookMealRecipePage __instance, ICoreClientAPI capi, ref RichTextComponentBase[] __result)
    {
        if (capi is null || __result is null) return;

        string? pageCode = __instance.PageCode;
        if (string.IsNullOrEmpty(pageCode)) return;

        var modSystem = capi.ModLoader.GetModSystem<ScribeModSystem>();
        if (modSystem is null) return;

        // Already Lang.Get-resolved; do not re-resolve (see class remarks).
        string title = __instance.Title;
        if (string.IsNullOrEmpty(title)) title = pageCode;

        var linkFont = CairoFont.WhiteSmallText();
        var headingFont = CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold);

        var appended = new RichTextComponentBase[]
        {
            new ClearFloatTextComponent(capi, 14f),
            new RichTextComponent(capi, Lang.Get("scribe:scribe-gui-additem-heading") + "\n", headingFont),
            new LinkTextComponent(capi, Lang.Get("scribe:scribe-gui-handbook-addlink") + "\n", linkFont,
                _ => modSystem.AddGuideLinkFromHandbook(pageCode, title)),
        };

        var combined = new RichTextComponentBase[__result.Length + appended.Length];
        __result.CopyTo(combined, 0);
        appended.CopyTo(combined, __result.Length);
        __result = combined;
    }
}
