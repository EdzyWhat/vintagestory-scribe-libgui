using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Scribe;

/// <summary>
/// Client-side Harmony postfix on <see cref="CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo"/> that
/// appends an "Add to Scribe" section (Link, Tracker, and Craft links) to every item/block Handbook page.
/// This is the only way to create a Tracker or Link task: both need the target item code the open page supplies.
/// Clicking a link calls <see cref="ScribeModSystem.AddFromHandbook"/>.
/// The postfix only appends to the returned component array, never mutating existing entries.
/// </summary>
[HarmonyPatch(typeof(CollectibleBehaviorHandbookTextAndExtraInfo),
    nameof(CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo))]
internal static class ScribeHandbookPatch
{
    /// <summary>Append the "Add to Scribe" section to <paramref name="__result"/>, the full vanilla page.</summary>
    // ReSharper disable once InconsistentNaming — Harmony injected parameter name is fixed.
    private static void Postfix(ItemSlot inSlot, ICoreClientAPI capi, ref RichTextComponentBase[] __result)
    {
        // Carry the page's own attributed stack, not a bare code: attributes (a lantern's material/glass/lining)
        // make the recipe probe match and the target resolve to the correct name.
        var stack = inSlot?.Itemstack;
        if (stack?.Collectible?.Code is null || capi is null) return;
        string itemCode = ScribeItemRef.Encode(stack);

        var modSystem = capi.ModLoader.GetModSystem<ScribeModSystem>();
        if (modSystem is null) return;

        var linkFont = CairoFont.WhiteSmallText();
        var headingFont = CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold);

        var appended = new List<RichTextComponentBase>
        {
            new ClearFloatTextComponent(capi, 14f),
            new RichTextComponent(capi, Lang.Get("scribe:scribe-gui-additem-heading") + "\n", headingFont),

            new LinkTextComponent(capi, Lang.Get("scribe:scribe-gui-handbook-addlink") + "\n", linkFont,
                _ => modSystem.AddFromHandbook(ScribeAddKinds.Link, itemCode)),
            new LinkTextComponent(capi, Lang.Get("scribe:scribe-gui-handbook-addtracker") + "\n", linkFont,
                _ => modSystem.AddFromHandbook(ScribeAddKinds.Tracker, itemCode)),
        };

        // One "Add Crafting Task" link per grid recipe producing this item (none if there is no grid recipe).
        // The signature lets the generator re-resolve the exact variant later.
        foreach (var variant in ScribeCraftRecipeProbe.ProbeVariants(capi, stack))
        {
            string signature = variant.Signature;
            // The recipe's resolved output, which may differ from the page item's own encoded target.
            string craftTarget = variant.OutputCode;
            appended.Add(new LinkTextComponent(capi, variant.Label + "\n", linkFont,
                _ => modSystem.AddCraftFromHandbook(craftTarget, signature)));
        }

        var combined = new RichTextComponentBase[__result.Length + appended.Count];
        __result.CopyTo(combined, 0);
        for (int i = 0; i < appended.Count; i++) combined[__result.Length + i] = appended[i];
        __result = combined;
    }
}
