using Scribe.Core;                // ScribeLinkTarget
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

    /// <summary>Resolve the display icon-stack + name for a Tracker/Link row from its snapshot
    /// <paramref name="code"/> (add-tracker-link-tasks 5/7.6). A guide-page Link (a <c>"page:"</c>-prefixed
    /// code) has no item to draw, so it resolves to <c>(null, LinkLabel)</c> — the caller draws a book glyph
    /// in place of an <c>ItemStackDisplay</c> and shows the stored guide title (falling back to the bare page
    /// code if no label was captured). Every other code is an item/block resolved to a stack whose name is
    /// used. Shared by the read/editor rows, the Pin Tab, and the HUD so all three classify and label a Link
    /// identically. <paramref name="linkLabel"/> is ignored for item/Tracker codes (where it is null anyway).</summary>
    public static (ItemStack? Stack, string? Name) ResolveDisplay(IWorldAccessor world, string? code, string? linkLabel)
    {
        if (ScribeLinkTarget.IsGuidePage(code))
            return (null, linkLabel ?? ScribeLinkTarget.PageCode(code));
        var stack = ResolveStack(world, code);
        return (stack, DisplayName(stack, code));
    }

    /// <summary>Open the Handbook page a Link/Tracker points at, via the survival mod's registered
    /// <c>"handbook"</c> link protocol (add-tracker-link-tasks 5.3/5.5/7.6). Two flavors of
    /// <paramref name="code"/>: a <c>"page:"</c>-prefixed <b>guide-page</b> code opens that raw Handbook page
    /// directly (no item to resolve); anything else is an <b>item</b> code, resolved to a stack whose Handbook
    /// page is opened. No-op when the code is empty/doesn't resolve, or the survival mod (and thus the
    /// protocol) isn't loaded — never toggles any completion state.</summary>
    public static void OpenHandbookPage(ICoreClientAPI capi, string? code)
    {
        if (string.IsNullOrEmpty(code)) return;
        if (ScribeLinkTarget.IsGuidePage(code))
        {
            OpenHandbookByPageCode(capi, ScribeLinkTarget.PageCode(code));
            return;
        }
        var stack = ResolveStack(capi.World, code);
        if (stack is null) return;
        OpenHandbookByPageCode(capi, GuiHandbookItemStackPage.PageCodeForStack(stack));
    }

    /// <summary>Open a raw Handbook page by its page code (e.g. <c>"craftinginfo-knapping"</c> or an
    /// item-stack page code) through the registered <c>"handbook"</c> link protocol. No-op when the page
    /// code is empty or the protocol isn't registered.</summary>
    private static void OpenHandbookByPageCode(ICoreClientAPI capi, string? pageCode)
    {
        if (string.IsNullOrEmpty(pageCode)) return;
        if (capi.LinkProtocols.TryGetValue("handbook", out var open))
            open(new LinkTextComponent("handbook://" + pageCode));
    }
}
