using HarmonyLib;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Scribe;

/// <summary>
/// Client-side Harmony postfix that appends a single "Add Link" clickable link to the bottom of every
/// non-item Handbook <b>guide/explainer</b> page (add-tracker-link-tasks 7.6). The item-page patch
/// (<see cref="ScribeHandbookPatch"/>) offers Add Tracker + Add Link on pages with an item behind them;
/// a guide page has no item, so only a <b>Link</b> makes sense (there is nothing to count for a Tracker).
/// Clicking it calls <see cref="ScribeModSystem.AddGuideLinkFromHandbook"/> with the guide's page code and
/// resolved title, which resolves an open/openable Scribe surface and appends a <c>page:</c>-prefixed guide
/// Link through that dialog's existing save path.
///
/// <para>Patches <see cref="GuiHandbookTextPage.Init"/> (the survival mod's text-page builder). Init runs
/// <c>comps = VtmlUtil.Richtextify(...)</c>, freshly rebuilding the component array each call, so appending in
/// a postfix can never accumulate duplicates across repeated opens. The postfix only APPENDS — it never
/// mutates existing components — so a page is byte-identical to vanilla apart from the trailing link. As with
/// <see cref="ScribeHandbookPatch"/>, Harmony and the survival mod both ship with the base game, so this adds
/// no mod dependency.</para>
///
/// <para>Client-only: created/unpatched with the same <see cref="Harmony"/> instance as the item-page patch,
/// discovered by the shared <c>PatchAll</c> in <see cref="ScribeModSystem.StartHandbookPatch"/>.</para>
/// </summary>
[HarmonyPatch(typeof(GuiHandbookTextPage), nameof(GuiHandbookTextPage.Init))]
internal static class ScribeGuidePageHandbookPatch
{
    /// <summary>Append the "Add Link" action to a guide page's component array. Runs after
    /// <see cref="GuiHandbookTextPage.Init"/> rebuilds <c>comps</c> (accessed by field-ref as
    /// <paramref name="___comps"/>). Only guide-category pages get the link — item-stack pages are a different
    /// page class entirely (<c>GuiHandbookItemStackPage</c>) and are handled by <see cref="ScribeHandbookPatch"/>,
    /// so this simply skips any text page whose category isn't <c>"guide"</c>. The link reuses the item picker's
    /// <c>scribe-gui-addlink</c> label. The stored target is the guide's <see cref="GuiHandbookTextPage.PageCode"/>
    /// (marked as a guide page in Core via <see cref="ScribeLinkTarget"/>); the display label is the resolved
    /// title, captured now because a guide page has no item to derive a name from later.</summary>
    // ReSharper disable once InconsistentNaming — Harmony injected parameter names are fixed.
    private static void Postfix(GuiHandbookTextPage __instance, ICoreClientAPI capi, ref RichTextComponentBase[] ___comps)
    {
        if (capi is null || ___comps is null) return;
        if (__instance.CategoryCode != "guide") return;

        string? pageCode = __instance.PageCode;
        if (string.IsNullOrEmpty(pageCode)) return;

        var modSystem = capi.ModLoader.GetModSystem<ScribeModSystem>();
        if (modSystem is null) return;

        // Title is a lang KEY on the page (the survival mod renders it via Lang.Get(Title)); resolve it to the
        // player-facing string so the pinned/read-view Link row shows a real title rather than the raw key.
        string title = Lang.Get(__instance.Title);

        var linkFont = CairoFont.WhiteSmallText();
        var headingFont = CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold);

        var appended = new RichTextComponentBase[]
        {
            // Match the item page's spacing idiom: a small gap, a bold heading, then the single action.
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
