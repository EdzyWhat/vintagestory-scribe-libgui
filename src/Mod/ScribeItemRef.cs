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
/// Resolves a Tracker/Link/Craft block's target string (Core stores only the string) against the live
/// registries into an <see cref="ItemStack"/> for the row icon and name, and the Handbook page a Link opens.
/// Shared so the read row, editor row, and pinned HUD resolve identically.
///
/// <para>Two target shapes: a <b>bare collectible code</b> (<c>"game:ingot-copper"</c>) resolves to an
/// attribute-less stack; an <b>attribute-encoded</b> target
/// (<c>"stack@&lt;code&gt;|&lt;b|i&gt;|&lt;base64(attrJson)&gt;"</c>, from <see cref="Encode"/>) rebuilds the
/// stack's meaningful attributes for items whose identity lives in <see cref="ItemStack.Attributes"/>
/// (lanterns, meals, tool-heads). The <c>stack@</c> marker cannot collide with a bare <c>domain:path</c> code
/// or the <see cref="ScribeLinkTarget.GuidePagePrefix"/> prefix.</para>
/// </summary>
internal static class ScribeItemRef
{
    /// <summary>Marker prefixing an attribute-encoded target.</summary>
    private const string StackPrefix = "stack@";

    /// <summary>Field separator for the restricted-wildcard microformat
    /// <c>"&lt;wildcardCode&gt;|&lt;allowed,csv&gt;[|&lt;skip,csv&gt;]"</c>. The <see cref="StackPrefix"/> form
    /// also uses <c>'|'</c> but is always distinguished by its marker first.</summary>
    private const char WildcardSep = '|';

    /// <summary>True when <paramref name="code"/> is an <see cref="Encode"/>d attribute-encoded target, so the
    /// Tracker counter uses an exact-variant matcher instead of the wildcard-friendly collectible match.</summary>
    public static bool IsAttributeEncoded(string? code)
        => code is not null && code.StartsWith(StackPrefix, StringComparison.Ordinal);

    /// <summary>Build the stored target code for a (possibly restricted) wildcard ingredient. An unrestricted
    /// wildcard (e.g. <c>game:metalplate-*</c>) stores the bare code. One with an allowed/skip variant list
    /// (e.g. <c>{ code: "*", allowedVariants: [...] }</c>) stores the
    /// <c>"&lt;code&gt;|&lt;allowed,csv&gt;[|&lt;skip,csv&gt;]"</c> microformat so the counter honors the filter;
    /// a bare <c>game:*</c> would match every carried item and display as <c>game:item-air</c>.</summary>
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

    /// <summary>Parse an <see cref="EncodeWildcard"/> microformat into its base wildcard and allowed/skip
    /// variant arrays. Returns false (out-params null) for a bare code, a <see cref="StackPrefix"/> target, or a
    /// malformed base location.</summary>
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

    /// <summary>Encode a stack's identity-bearing attributes into a target string, mirroring
    /// <see cref="GuiHandbookItemStackPage.PageCodeForStack"/> (strip <see cref="GlobalConstants.IgnoredStackAttributes"/>
    /// plus <c>durability</c>, sort, JSON-serialize). With nothing meaningful left the result is the bare code;
    /// otherwise <c>"stack@&lt;code&gt;|&lt;b|i&gt;|&lt;base64(attrJson)&gt;"</c> (base64 is delimiter-safe).</summary>
    public static string Encode(ItemStack stack)
    {
        string bareCode = stack.Collectible.Code.ToString();

        var attributes = stack.Attributes;
        if (attributes is null || attributes.Count == 0) return bareCode;

        ITreeAttribute meaningful = attributes.Clone();
        foreach (string ignored in GlobalConstants.IgnoredStackAttributes)
            meaningful.RemoveAttribute(ignored);
        meaningful.RemoveAttribute("durability");
        if (meaningful.Count == 0) return bareCode;

        // Sorted so the encoded string (and any signature derived from it) is stable across sessions.
        var sorted = meaningful.SortedCopy(true);
        string attrJson = TreeAttribute.ToJsonToken(sorted);
        string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(attrJson));
        string classFlag = stack.Class == EnumItemClass.Block ? "b" : "i";
        return StackPrefix + bareCode + "|" + classFlag + "|" + base64;
    }

    /// <summary>Build an <see cref="ItemStack"/> from a target string: items then blocks for a bare code (the
    /// order <see cref="ScribeTrackerCounter"/> uses), or the full attributed stack for a <c>"stack@"</c>
    /// target. Any empty, malformed, unregistered, or corrupt input returns null (never throws); callers show
    /// the raw code.</summary>
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

    /// <summary>Rebuild an attributed stack from an <see cref="Encode"/>d target. Neither a <c>domain:path</c>
    /// code nor base64 contains <c>'|'</c>, so the three fields split cleanly. Any malformed field, registry
    /// miss, or bad blob returns null.</summary>
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

    /// <summary>The stack's name, falling back to the raw code (e.g. a removed mod's item) so the row is never blank.</summary>
    public static string DisplayName(ItemStack? stack, string? fallbackCode)
        => stack?.GetName() ?? fallbackCode ?? "";

    /// <summary>Resolve the icon stack and name for a Tracker/Link row. A <c>"page:"</c> or <c>"quest:"</c> Link
    /// has no item, so it resolves to <c>(null, linkLabel)</c> (falling back to the bare page/quest code) and
    /// the caller draws a book glyph. Every other code resolves to a stack and its name.
    /// <paramref name="linkLabel"/> is ignored for item/Tracker codes. Shared by the rows, Pin Tab, and HUD.</summary>
    public static (ItemStack? Stack, string? Name) ResolveDisplay(IWorldAccessor world, string? code, string? linkLabel)
    {
        if (ScribeLinkTarget.IsGuidePage(code))
            return (null, linkLabel ?? ScribeLinkTarget.PageCode(code));
        if (ScribeLinkTarget.IsQuest(code))
            return (null, linkLabel ?? ScribeLinkTarget.QuestCode(code));
        var stack = ResolveStack(world, code);
        if (stack is null && code is not null && code.Contains('*'))
        {
            // A wildcard family code (e.g. "game:metalplate-*") resolves to no single stack: show a representative
            // member's icon and an "any variant" label. The stored code stays the wildcard so counting matches all.
            var member = ResolveWildcardMember(world, code);
            if (member is not null)
                return (member, Lang.Get("scribe:scribe-gui-craft-any-family", member.GetName()));
        }
        return (stack, DisplayName(stack, code));
    }

    /// <summary>Resolve a representative stack for a wildcard code, for a Tracker row's icon and name and for
    /// <see cref="ScribeTrackerCounter"/>'s item class. A restricted microformat substitutes its first resolvable
    /// allowed variant into the <c>*</c> (e.g. <c>game:*</c> + <c>"papyrustops"</c> → <c>game:papyrustops</c>); a
    /// bare wildcard takes the first <see cref="IWorldAccessor.SearchItems"/> then
    /// <see cref="IWorldAccessor.SearchBlocks"/> match, skipping the <c>air</c> sentinel. Null when malformed or
    /// the family has no live member.</summary>
    public static ItemStack? ResolveWildcardMember(IWorldAccessor world, string code)
    {
        if (TryParseWildcard(code, out var baseLoc, out var allowed, out _) && baseLoc is not null)
        {
            if (allowed is not null)
                foreach (string variant in allowed)
                {
                    var member = ResolveConcreteMember(world, baseLoc, variant);
                    if (member is not null) return member;
                }
            // No allowed variant resolved: search on the bare wildcard code.
            code = baseLoc.ToString();
        }

        AssetLocation loc;
        try { loc = new AssetLocation(code); }
        catch { return null; }

        // SearchItems("*") returns game:item-air first; never present air as a family representative.
        if (FirstNonAir(world.SearchItems(loc)) is { } item) return new ItemStack(item);
        if (FirstNonAir(world.SearchBlocks(loc)) is { } block) return new ItemStack(block);
        return null;
    }

    /// <summary>Substitute <paramref name="variant"/> for the <c>*</c> in the path (<c>metalplate-*</c> +
    /// <c>"copper"</c> → <c>metalplate-copper</c>) and resolve it as item then block. Null if there is no
    /// <c>*</c> or the code is unregistered.</summary>
    private static ItemStack? ResolveConcreteMember(IWorldAccessor world, AssetLocation wildCard, string variant)
    {
        if (!wildCard.Path.Contains('*')) return null;
        var loc = new AssetLocation(wildCard.Domain, wildCard.Path.Replace("*", variant));
        if (world.GetItem(loc) is { } item) return new ItemStack(item);
        if (world.GetBlock(loc) is { } block) return new ItemStack(block);
        return null;
    }

    /// <summary>The first non-<c>air</c> collectible; the <c>air</c> sentinel (path <c>"air"</c>) sorts first in
    /// a bare-<c>*</c> search.</summary>
    private static T? FirstNonAir<T>(T[]? found) where T : CollectibleObject
    {
        if (found is null) return null;
        foreach (var c in found)
            if (c?.Code is { } loc && loc.Path != "air") return c;
        return null;
    }

    /// <summary>Open the Handbook page a Link/Tracker points at. A <c>"page:"</c> code opens that raw page; a
    /// Progression Framework <c>"quest:"</c> code opens that backend's Quest Log via
    /// <see cref="ScribeProgressionFrameworkQuestCatalog.TryOpenQuestLog"/>; a VS Quest <c>"quest:"</c> code
    /// does nothing (no page or navigation target exists); anything else resolves to a stack and opens its
    /// page. No-op when the code is empty or unresolved. Never toggles completion state.
    ///
    /// <para>The page code prefers the collectible's <see cref="IHandBookPageCodeProvider"/> (e.g.
    /// <c>BlockMeal</c> maps every meal to one shared page), falling back to
    /// <see cref="GuiHandbookItemStackPage.PageCodeForStack"/> if absent, null, or throwing.</para></summary>
    public static void OpenHandbookPage(ICoreClientAPI capi, string? code)
    {
        if (string.IsNullOrEmpty(code)) return;
        if (ScribeLinkTarget.IsGuidePage(code))
        {
            OpenHandbookByPageCode(capi, ScribeLinkTarget.PageCode(code));
            return;
        }
        if (ScribeLinkTarget.IsQuest(code))
        {
            if (ScribeLinkTarget.QuestSource(code) == ScribeQuestSource.ProgressionFramework)
                ScribeProgressionFrameworkQuestCatalog.TryOpenQuestLog(capi);
            return;
        }
        var stack = ResolveStack(capi.World, code);
        if (stack is null) return;

        string? pageCode = null;
        if (stack.Collectible is IHandBookPageCodeProvider provider)
        {
            // A provider may throw or return an unopenable code; fall back rather than break navigation.
            try { pageCode = provider.HandbookPageCodeForStack(capi.World, stack); }
            catch { pageCode = null; }
        }
        if (string.IsNullOrEmpty(pageCode))
            pageCode = GuiHandbookItemStackPage.PageCodeForStack(stack);

        OpenHandbookByPageCode(capi, pageCode);
    }

    /// <summary>Open a Handbook page by code through the <c>"handbook"</c> link protocol; no-op if empty or unregistered.</summary>
    private static void OpenHandbookByPageCode(ICoreClientAPI capi, string? pageCode)
    {
        if (string.IsNullOrEmpty(pageCode)) return;
        if (capi.LinkProtocols.TryGetValue("handbook", out var open))
            open(new LinkTextComponent("handbook://" + pageCode));
    }
}
