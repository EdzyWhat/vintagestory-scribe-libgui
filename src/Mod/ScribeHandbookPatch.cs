using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Scribe;

/// <summary>
/// Client-side Harmony postfix that appends a small "Add to Scribe" section — an <b>Add Tracker</b> and an
/// <b>Add Link</b> clickable link — to the bottom of every item/block Handbook page
/// (add-tracker-link-tasks 3.1). This is the ONLY way to create a Tracker or Link task: both need a target
/// item code, which the Handbook page uniquely provides (the item whose page is open). Clicking a link calls
/// <see cref="ScribeModSystem.AddFromHandbook"/>, which resolves an open/openable Scribe surface and appends
/// the block through that dialog's existing save path.
///
/// <para>Patches <see cref="CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo"/> (the survival
/// mod's handbook-page builder) rather than reimplementing handbook rendering. Harmony/the survival mod both
/// ship with the base game, so this adds no mod dependency (see the "Harmony ships with the base game" and
/// VSSurvivalMod project notes). The postfix only APPENDS to the returned component array — it never mutates
/// existing entries — so a page with no Scribe items installed is byte-identical to vanilla.</para>
///
/// <para>Client-only: the Handbook is a client GUI, so the owning <see cref="Harmony"/> instance is created
/// in <see cref="ScribeModSystem.StartClientSide"/> and unpatched in <see cref="ScribeModSystem.Dispose"/>.
/// The postfix never runs server-side.</para>
/// </summary>
[HarmonyPatch(typeof(CollectibleBehaviorHandbookTextAndExtraInfo),
    nameof(CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo))]
internal static class ScribeHandbookPatch
{
    /// <summary>Append the "Add to Scribe" section to the handbook page's component array. Runs after the
    /// vanilla builder, so <paramref name="__result"/> is the full page; we concatenate our components onto
    /// it. The link labels reuse the editor picker's <c>scribe-gui-addtracker</c>/<c>scribe-gui-addlink</c>
    /// keys (one label set for both surfaces — fewer strings for translators).</summary>
    // ReSharper disable once InconsistentNaming — Harmony injected parameter name is fixed.
    private static void Postfix(ItemSlot inSlot, ICoreClientAPI capi, ref RichTextComponentBase[] __result)
    {
        // Need a resolvable collectible code to carry as the tracker/link target; bail on the odd empty page.
        string? itemCode = inSlot?.Itemstack?.Collectible?.Code?.ToString();
        if (itemCode is null || capi is null) return;

        var modSystem = capi.ModLoader.GetModSystem<ScribeModSystem>();
        if (modSystem is null) return;

        var linkFont = CairoFont.WhiteSmallText();
        var headingFont = CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold);

        var appended = new List<RichTextComponentBase>
        {
            // A little vertical gap, then a bold section heading, matching the vanilla page's own spacing idiom.
            new ClearFloatTextComponent(capi, 14f),
            new RichTextComponent(capi, Lang.Get("scribe:scribe-gui-additem-heading") + "\n", headingFont),

            // The two clickable actions. Each captures the item code and dispatches to the mod system, which
            // owns the "which Scribe surface?" resolution. A trailing newline separates the two links.
            new LinkTextComponent(capi, Lang.Get("scribe:scribe-gui-addtracker") + "\n", linkFont,
                _ => modSystem.AddFromHandbook(ScribeAddKinds.Tracker, itemCode)),
            new LinkTextComponent(capi, Lang.Get("scribe:scribe-gui-addlink") + "\n", linkFont,
                _ => modSystem.AddFromHandbook(ScribeAddKinds.Link, itemCode)),
        };

        var combined = new RichTextComponentBase[__result.Length + appended.Count];
        __result.CopyTo(combined, 0);
        for (int i = 0; i < appended.Count; i++) combined[__result.Length + i] = appended[i];
        __result = combined;
    }
}
