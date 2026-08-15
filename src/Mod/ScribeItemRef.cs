using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;   // GuiHandbookItemStackPage

namespace Scribe;

/// <summary>
/// Mod-layer helper for turning a Tracker/Link block's plain item-code string (e.g.
/// <c>"game:ingot-copper"</c>) into the game objects the GUI needs — an <see cref="ItemStack"/> for the row
/// icon + display name, and the Handbook page a Link opens (add-tracker-link-tasks Group 5). Core stores
/// only the string (it is VS-API-free by invariant); this is where the Mod layer parses it against the live
/// registries. Kept in one place so the read row, editor row, and pinned HUD all resolve identically.
/// </summary>
internal static class ScribeItemRef
{
    /// <summary>Build an <see cref="ItemStack"/> from a collectible code, probing the item registry first
    /// then blocks (the same order <see cref="ScribeTrackerCounter"/> uses). Returns null for a null/empty/
    /// malformed code or one that resolves to neither — callers then fall back to showing the raw code.</summary>
    public static ItemStack? ResolveStack(IWorldAccessor world, string? code)
    {
        if (string.IsNullOrEmpty(code)) return null;

        AssetLocation loc;
        try { loc = new AssetLocation(code); }
        catch { return null; }

        var item = world.GetItem(loc);
        if (item != null) return new ItemStack(item);
        var block = world.GetBlock(loc);
        if (block != null) return new ItemStack(block);
        return null;
    }

    /// <summary>The player-facing name for a resolved stack, falling back to the raw code when the stack
    /// can't be resolved (a mod that provided the item was removed, say) so a Tracker/Link row still reads
    /// as something rather than blank.</summary>
    public static string DisplayName(ItemStack? stack, string? fallbackCode)
        => stack?.GetName() ?? fallbackCode ?? "";

    /// <summary>Open the Handbook page for a collectible code via the survival mod's registered
    /// <c>"handbook"</c> link protocol (add-tracker-link-tasks 5.3/5.5). No-op when the code doesn't resolve
    /// or the survival mod (and thus the protocol) isn't loaded — never toggles any completion state.</summary>
    public static void OpenHandbookPage(ICoreClientAPI capi, string? code)
    {
        var stack = ResolveStack(capi.World, code);
        if (stack is null) return;
        if (capi.LinkProtocols.TryGetValue("handbook", out var open))
            open(new LinkTextComponent("handbook://" + GuiHandbookItemStackPage.PageCodeForStack(stack)));
    }
}
