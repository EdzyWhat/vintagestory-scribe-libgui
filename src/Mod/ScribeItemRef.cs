using System;                     // Convert, StringComparison
using System.Text;                // Encoding
using Scribe.Core;                // ScribeLinkTarget
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;    // GlobalConstants
using Vintagestory.API.Datastructures; // TreeAttribute, ITreeAttribute
using Vintagestory.GameContent;   // GuiHandbookItemStackPage, IHandBookPageCodeProvider

namespace Scribe;

/// <summary>
/// Mod-layer helper for turning a Tracker/Link/Craft block's target string into the game objects the GUI
/// needs — an <see cref="ItemStack"/> for the row icon + display name, and the Handbook page a Link opens
/// (add-tracker-link-tasks Group 5). Core stores only the string (it is VS-API-free by invariant); this is
/// where the Mod layer parses it against the live registries. Kept in one place so the read row, editor row,
/// and pinned HUD all resolve identically.
///
/// <para>Two target-string shapes are understood (support-attribute-encoded-items): a <b>bare collectible
/// code</b> (<c>"game:ingot-copper"</c>) resolves to an attribute-less stack exactly as before; an
/// <b>attribute-encoded</b> target (<c>"stack@&lt;code&gt;|&lt;b|i&gt;|&lt;base64(attrJson)&gt;"</c>, produced
/// by <see cref="Encode"/>) rebuilds the stack's meaningful attributes so items whose identity lives in
/// <see cref="ItemStack.Attributes"/> — lanterns (material/glass/lining), meals, tool-heads — resolve to the
/// correct name, Handbook page, and exact variant. The <c>stack@</c> marker cannot collide with a bare
/// <c>domain:path</c> code or the <see cref="ScribeLinkTarget.GuidePagePrefix"/> guide-page prefix.</para>
/// </summary>
internal static class ScribeItemRef
{
    /// <summary>Marker prefixing an attribute-encoded target so it can't be mistaken for a bare collectible
    /// code (a <c>domain:path</c> <c>AssetLocation</c>) or a <c>"page:"</c> guide-page reference.</summary>
    private const string StackPrefix = "stack@";

    /// <summary>Field separator for the <b>restricted-wildcard microformat</b> (fix-recipe-variant-identity
    /// D8, option B): <c>"&lt;wildcardCode&gt;|&lt;allowed,csv&gt;[|&lt;skip,csv&gt;]"</c>. A bare
    /// <c>domain:path</c> code never contains <c>'|'</c>, and the attribute-encoded <see cref="StackPrefix"/>
    /// form (which also uses <c>'|'</c>) is always distinguished by its marker first, so this separator only
    /// ever appears in a wildcard microformat.</summary>
    private const char WildcardSep = '|';

    /// <summary>True when <paramref name="code"/> is an <see cref="Encode"/>d attribute-encoded target (carries
    /// meaningful stack attributes) rather than a bare collectible code. Lets the Tracker counter pick an
    /// exact-variant matcher for these and keep the wildcard-friendly collectible match for bare codes.</summary>
    public static bool IsAttributeEncoded(string? code)
        => code is not null && code.StartsWith(StackPrefix, StringComparison.Ordinal);

    /// <summary>Build the stored target code for a (possibly restricted) wildcard ingredient
    /// (fix-recipe-variant-identity D8). An <b>unrestricted</b> wildcard — no <paramref name="allowedVariants"/>
    /// and no <paramref name="skipVariants"/>, e.g. a plain <c>game:metalplate-*</c> whose <c>*</c>-prefix
    /// already IS the family — stores the bare wildcard code, byte-identical to before (no regression). A
    /// <b>restricted</b> wildcard — one carrying an allowed/skip variant list, e.g. the Hunter's Backpack's
    /// <c>{ code: "*", allowedVariants: ["papyrustops","cattailtops"] }</c> — stores a Mod-side microformat
    /// <c>"&lt;code&gt;|&lt;allowed,csv&gt;[|&lt;skip,csv&gt;]"</c> so the counter can honor the variant filter.
    /// Without it a bare <c>game:*</c> would match every carried item and resolve its display to
    /// <c>game:item-air</c> (the 2026-08-20 playtest symptom). The stored value stays a plain string, so
    /// Core's document schema is untouched.</summary>
    public static string EncodeWildcard(AssetLocation code, string[]? allowedVariants, string[]? skipVariants)
    {
        string bare = code.ToString();
        bool hasAllowed = allowedVariants is { Length: > 0 };
        bool hasSkip = skipVariants is { Length: > 0 };
        if (!hasAllowed && !hasSkip) return bare;

        var sb = new StringBuilder(bare);
        sb.Append(WildcardSep).Append(hasAllowed ? string.Join(",", allowedVariants!) : "");
        if (hasSkip) sb.Append(WildcardSep).Append(string.Join(",", skipVariants!));
        return sb.ToString();
    }

    /// <summary>Parse an <see cref="EncodeWildcard"/> restricted-wildcard microformat into its base wildcard
    /// <paramref name="code"/> plus the allowed/skip variant arrays. Returns false — leaving all out-params
    /// null — for a bare code (no <see cref="WildcardSep"/>), an attribute-encoded <see cref="StackPrefix"/>
    /// target, or a malformed base location, so callers fall through to their existing bare-code handling.</summary>
    public static bool TryParseWildcard(string? code, out AssetLocation? baseCode,
        out string[]? allowedVariants, out string[]? skipVariants)
    {
        baseCode = null; allowedVariants = null; skipVariants = null;
        if (string.IsNullOrEmpty(code)) return false;
        if (code.StartsWith(StackPrefix, StringComparison.Ordinal)) return false;

        int sep = code.IndexOf(WildcardSep);
        if (sep < 0) return false;

        try { baseCode = new AssetLocation(code[..sep]); }
        catch { baseCode = null; return false; }

        string[] rest = code[(sep + 1)..].Split(WildcardSep);
        allowedVariants = SplitCsv(rest[0]);
        if (rest.Length > 1) skipVariants = SplitCsv(rest[1]);
        return true;
    }

    private static string[]? SplitCsv(string s)
        => string.IsNullOrEmpty(s) ? null : s.Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Encode an <see cref="ItemStack"/> into a target string that preserves its meaningful,
    /// identity-bearing attributes (support-attribute-encoded-items Fix B). Mirrors
    /// <see cref="GuiHandbookItemStackPage.PageCodeForStack"/>: clone the attributes, strip every
    /// <see cref="GlobalConstants.IgnoredStackAttributes"/> key plus <c>durability</c> (transient/noise), take
    /// a deterministic <c>SortedCopy(true)</c>, and JSON-serialize. When nothing meaningful remains the result
    /// is the <b>bare code</b> (<c>stack.Collectible.Code.ToString()</c>) — byte-identical to the pre-change
    /// target for common items, so their storage is unchanged. Otherwise the attributes are base64-wrapped
    /// (delimiter-safe through persistence and the HUD) into
    /// <c>"stack@&lt;code&gt;|&lt;b|i&gt;|&lt;base64(attrJson)&gt;"</c>.</summary>
    public static string Encode(ItemStack stack)
    {
        string bareCode = stack.Collectible.Code.ToString();

        var attributes = stack.Attributes;
        if (attributes is null || attributes.Count == 0) return bareCode;

        // Clone + strip the same noise PageCodeForStack strips, so identity keys on the meaningful variant
        // attributes (material/glass/lining) and ignores durability/temperature/tool-mode/etc.
        ITreeAttribute meaningful = attributes.Clone();
        foreach (string ignored in GlobalConstants.IgnoredStackAttributes)
            meaningful.RemoveAttribute(ignored);
        meaningful.RemoveAttribute("durability");
        if (meaningful.Count == 0) return bareCode;

        // SortedCopy(true) gives a deterministic ordering so the encoded string (and any signature derived
        // from it) is stable across sessions — the same determinism PageCodeForStack relies on.
        var sorted = meaningful.SortedCopy(true);
        string attrJson = TreeAttribute.ToJsonToken(sorted);
        string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(attrJson));
        string classFlag = stack.Class == EnumItemClass.Block ? "b" : "i";
        return StackPrefix + bareCode + "|" + classFlag + "|" + base64;
    }

    /// <summary>Build an <see cref="ItemStack"/> from a target string, probing the item registry first then
    /// blocks for a bare code (the same order <see cref="ScribeTrackerCounter"/> uses), or rebuilding the
    /// full attributed stack for an <see cref="Encode"/>d <c>"stack@"</c> target. Returns null for a null/
    /// empty/malformed code or one that resolves to neither — callers then fall back to showing the raw code.
    /// Every parse/registry miss degrades to null (never throws), so a legacy bare code, a target for a
    /// removed mod's item, or a corrupt blob all fail soft.</summary>
    public static ItemStack? ResolveStack(IWorldAccessor world, string? code)
    {
        if (string.IsNullOrEmpty(code)) return null;

        if (code.StartsWith(StackPrefix, StringComparison.Ordinal))
            return DecodeAttributed(world, code);

        AssetLocation loc;
        try { loc = new AssetLocation(code); }
        catch { return null; }

        var item = world.GetItem(loc);
        if (item != null) return new ItemStack(item);
        var block = world.GetBlock(loc);
        if (block != null) return new ItemStack(block);
        return null;
    }

    /// <summary>Rebuild an attributed <see cref="ItemStack"/> from an <see cref="Encode"/>d <c>"stack@"</c>
    /// target: split off the collectible code, the <c>b</c>/<c>i</c> class flag, and the base64 attribute
    /// blob (neither a <c>domain:path</c> code nor standard base64 contains <c>'|'</c>, so the three fields
    /// split cleanly), resolve the collectible from the block or item registry per the flag, and hydrate its
    /// attributes via <see cref="TreeAttribute.FromJson"/>. Any malformed field, registry miss, or bad blob
    /// returns null (the caller then shows the raw code) — never throws.</summary>
    private static ItemStack? DecodeAttributed(IWorldAccessor world, string code)
    {
        try
        {
            string body = code[StackPrefix.Length..];
            string[] parts = body.Split('|');
            if (parts.Length != 3) return null;

            string collectibleCode = parts[0];
            bool isBlock = parts[1] == "b";
            string attrJson = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]));

            var loc = new AssetLocation(collectibleCode);
            ItemStack? stack;
            if (isBlock)
            {
                var block = world.GetBlock(loc);
                if (block is null) return null;
                stack = new ItemStack(block);
            }
            else
            {
                var item = world.GetItem(loc);
                if (item is null) return null;
                stack = new ItemStack(item);
            }

            stack.Attributes = (ITreeAttribute)TreeAttribute.FromJson(attrJson);
            return stack;
        }
        catch { return null; }
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
        if (stack is null && code is not null && code.Contains('*'))
        {
            // A genuine wildcard/family ingredient code (e.g. "game:metalplate-*", stored by a Crafting Task's
            // child Tracker) resolves to no single stack. Show a representative family member's icon + a
            // readable "any variant" family label instead of the raw code (fix-recipe-variant-identity D3). The
            // STORED code stays the wildcard, so counting still matches every member.
            var member = ResolveWildcardMember(world, code);
            if (member is not null)
                return (member, Lang.Get("scribe:scribe-gui-craft-any-family", member.GetName()));
        }
        return (stack, DisplayName(stack, code));
    }

    /// <summary>Resolve a representative stack for a wildcard/family code — used both for a Tracker row's
    /// display icon+name and by <see cref="ScribeTrackerCounter"/> to pick the counting ingredient's item
    /// class. Two shapes (fix-recipe-variant-identity D8):
    /// <list type="bullet">
    /// <item>A <b>restricted-wildcard microformat</b> (<see cref="EncodeWildcard"/>) resolves a concrete member
    /// by substituting its first resolvable allowed variant into the wildcard's <c>*</c> (e.g.
    /// <c>game:*</c> + <c>"papyrustops"</c> → <c>game:papyrustops</c>, matching how
    /// <c>WildcardUtil.MatchesVariants</c> extracts the variant), so the representative is always a real
    /// family member — never <c>game:item-air</c>.</item>
    /// <item>A <b>bare wildcard</b> (e.g. <c>game:metalplate-*</c>) takes the first member the wildcard-aware
    /// registries return (<see cref="IWorldAccessor.SearchItems"/> then <see cref="IWorldAccessor.SearchBlocks"/>),
    /// skipping the <c>air</c> sentinel so a degenerate bare <c>*</c> can never surface "Item-Air".</item>
    /// </list>
    /// Null when the code is malformed or the family has no live member (caller falls back to the raw code —
    /// no regression).</summary>
    public static ItemStack? ResolveWildcardMember(IWorldAccessor world, string code)
    {
        // Restricted-wildcard microformat: prefer a concrete allowed-variant member (guaranteed non-air).
        if (TryParseWildcard(code, out var baseLoc, out var allowed, out _) && baseLoc is not null)
        {
            if (allowed is not null)
                foreach (string variant in allowed)
                {
                    var member = ResolveConcreteMember(world, baseLoc, variant);
                    if (member is not null) return member;
                }
            // No allowed variant resolved → fall through to a filtered search on the bare wildcard code.
            code = baseLoc.ToString();
        }

        AssetLocation loc;
        try { loc = new AssetLocation(code); }
        catch { return null; }

        // Air-exclusion guard (D8): SearchItems("*") returns game:item-air first — never present air (or any
        // air sentinel) as a family representative, for ANY wildcard shape.
        if (FirstNonAir(world.SearchItems(loc)) is { } item) return new ItemStack(item);
        if (FirstNonAir(world.SearchBlocks(loc)) is { } block) return new ItemStack(block);
        return null;
    }

    /// <summary>Build the concrete member of <paramref name="wildCard"/> for one allowed
    /// <paramref name="variant"/> by replacing the single <c>*</c> in its path (e.g. <c>metalplate-*</c> +
    /// <c>"copper"</c> → <c>metalplate-copper</c>; <c>*</c> + <c>"papyrustops"</c> → <c>papyrustops</c>), then
    /// resolving it from the item registry first, then blocks. Null when there is no <c>*</c> to fill or the
    /// resolved code is not registered.</summary>
    private static ItemStack? ResolveConcreteMember(IWorldAccessor world, AssetLocation wildCard, string variant)
    {
        if (!wildCard.Path.Contains('*')) return null;
        var loc = new AssetLocation(wildCard.Domain, wildCard.Path.Replace("*", variant));
        if (world.GetItem(loc) is { } item) return new ItemStack(item);
        if (world.GetBlock(loc) is { } block) return new ItemStack(block);
        return null;
    }

    /// <summary>The first non-<c>air</c> collectible in a wildcard search result. The <c>air</c> sentinel
    /// (item and block alike register code path <c>"air"</c>) sorts first in a bare-<c>*</c> search and would
    /// otherwise be shown as the family representative.</summary>
    private static T? FirstNonAir<T>(T[]? found) where T : CollectibleObject
    {
        if (found is null) return null;
        foreach (var c in found)
            if (c?.Code is { } loc && loc.Path != "air") return c;
        return null;
    }

    /// <summary>Open the Handbook page a Link/Tracker points at, via the survival mod's registered
    /// <c>"handbook"</c> link protocol (add-tracker-link-tasks 5.3/5.5/7.6). Two flavors of
    /// <paramref name="code"/>: a <c>"page:"</c>-prefixed <b>guide-page</b> code opens that raw Handbook page
    /// directly (no item to resolve); anything else is an item/attribute-encoded code, resolved to a stack
    /// whose Handbook page is opened. No-op when the code is empty/doesn't resolve, or the survival mod (and
    /// thus the protocol) isn't loaded — never toggles any completion state.
    ///
    /// <para>The page code prefers the collectible's own <see cref="IHandBookPageCodeProvider"/> when it
    /// implements one (e.g. <c>BlockMeal</c> maps every meal-with-ingredients to one shared page), falling
    /// back to <see cref="GuiHandbookItemStackPage.PageCodeForStack"/> when the interface is absent, returns
    /// null, or throws — a cheap correctness win for meals, borrowed from Tallybook, that never degrades the
    /// existing item-page behavior.</para></summary>
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

        string? pageCode = null;
        if (stack.Collectible is IHandBookPageCodeProvider provider)
        {
            // Guarded: a collectible's own page-code provider could throw or hand back an unopenable code, so
            // fall back to the generic item-stack page code rather than let it break navigation.
            try { pageCode = provider.HandbookPageCodeForStack(capi.World, stack); }
            catch { pageCode = null; }
        }
        if (string.IsNullOrEmpty(pageCode))
            pageCode = GuiHandbookItemStackPage.PageCodeForStack(stack);

        OpenHandbookByPageCode(capi, pageCode);
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
