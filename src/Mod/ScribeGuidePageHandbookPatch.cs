using HarmonyLib;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Scribe;

/// <summary>
/// Client-side Harmony postfix that appends a single "Add Link" clickable link to the bottom of every non-item
/// Handbook <b>guide/explainer</b> page. The item-page patch (<see cref="ScribeHandbookPatch"/>) offers Add
/// Tracker + Add Link; a guide page has no item, so only a Link makes sense. Clicking calls
/// <see cref="ScribeModSystem.AddGuideLinkFromHandbook"/> with the page code and resolved title, which appends a
/// <c>page:</c>-prefixed guide Link through the open Scribe dialog's save path.
///
/// <para>Patches <see cref="GuiHandbookTextPage.Init"/>, which rebuilds <c>comps</c> on each call, so appending in
/// a postfix never accumulates duplicates. The postfix only APPENDS, so a page matches vanilla apart from the
/// trailing link. Harmony and the survival mod ship with the base game, so this adds no mod dependency.</para>
///
/// <para>Client-only; discovered by the shared <c>PatchAll</c> in <see cref="ScribeModSystem.StartHandbookPatch"/>.</para>
/// </summary>
[HarmonyPatch(typeof(GuiHandbookTextPage), nameof(GuiHandbookTextPage.Init))]
internal static class ScribeGuidePageHandbookPatch
{
    /// <summary>Append the "Add Link" action to a guide page's component array after
    /// <see cref="GuiHandbookTextPage.Init"/> rebuilds <c>comps</c> (field-ref <paramref name="___comps"/>). Text
    /// pages whose category isn't <c>"guide"</c> are skipped; item-stack pages are a different class handled by
    /// <see cref="ScribeHandbookPatch"/>. The stored target is the guide's
    /// <see cref="GuiHandbookTextPage.PageCode"/> (marked as a guide page via <see cref="ScribeLinkTarget"/>); the
    /// display label is the resolved title, captured now because a guide page has no item to derive a name from
    /// later.</summary>
    // ReSharper disable once InconsistentNaming — Harmony injected parameter names are fixed.
    private static void Postfix(GuiHandbookTextPage __instance, ICoreClientAPI capi, ref RichTextComponentBase[] ___comps)
    {
        if (capi is null || ___comps is null) return;
        if (__instance.CategoryCode != "guide") return;

        string? pageCode = __instance.PageCode;
        if (string.IsNullOrEmpty(pageCode)) return;

        var modSystem = capi.ModLoader.GetModSystem<ScribeModSystem>();
        if (modSystem is null) return;

        // Title is a lang KEY (the survival mod renders it via Lang.Get(Title)); resolve it to the player-facing string.
        string title = Lang.Get(__instance.Title);

        var linkFont = CairoFont.WhiteSmallText();
        var headingFont = CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold);

        var appended = new RichTextComponentBase[]
        {
            // Same spacing idiom as the item page: small gap, bold heading, single action.
            new ClearFloatTextComponent(capi, 14f),
            new RichTextComponent(capi, Lang.Get("scribe:scribe-gui-additem-heading") + "\n", headingFont),
            new LinkTextComponent(capi, Lang.Get("scribe:scribe-gui-handbook-addlink") + "\n", linkFont,
                _ => modSystem.AddGuideLinkFromHandbook(pageCode, title)),
        };

        var combined = new RichTextComponentBase[___comps.Length + appended.Length];
        ___comps.CopyTo(combined, 0);
        appended.CopyTo(combined, ___comps.Length);
        ___comps = combined;
    }
}
