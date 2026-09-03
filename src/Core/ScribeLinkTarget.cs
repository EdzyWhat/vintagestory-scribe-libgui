using System;

namespace Scribe.Core;

/// <summary>Which quest mod backs a Quest Link (add-progression-framework-quest-support Decision 1) —
/// a fixed short token embedded in the <c>quest:</c> target string, never inferred from the quest code's
/// own domain (two backends could coincidentally share a domain string).</summary>
public static class ScribeQuestSource
{
    public const string VsQuest = "vsquest";
    public const string ProgressionFramework = "progressionframework";
}

/// <summary>
/// Helpers for interpreting a <see cref="ScribeBlock.LinkTarget"/> string. A Link's target is EITHER a plain
/// collectible code (an item/block Link — the Mod layer resolves it live to an <c>ItemStack</c> for its
/// icon + name) OR a Handbook <b>guide-page</b> reference marked by the <see cref="GuidePagePrefix"/> (a
/// non-item guide/explainer page, e.g. <c>"page:craftinginfo-knapping"</c> — it has no item, so its display
/// name is stored in <see cref="ScribeBlock.LinkLabel"/> and its icon is a generic book). Quest links use
/// a <c>quest:</c> prefix, scoped to the backend mod that owns the quest (<c>quest:{source}/{code}</c> —
/// add-progression-framework-quest-support Decision 1), and retain their captured label/description in the
/// block.
///
/// <para>Kept in Core as pure string logic (no VS API) so every surface — read view, editor, Pin Tab, HUD —
/// classifies a link identically and can't drift (add-tracker-link-tasks 7.6).</para>
/// </summary>
public static class ScribeLinkTarget
{
    /// <summary>Marker prefixing a guide-page target so it can't be mistaken for a bare collectible code.
    /// A collectible code is an <c>AssetLocation</c> (<c>domain:path</c>) whose domain is never <c>"page"</c>,
    /// so this scheme-like prefix is unambiguous.</summary>
    public const string GuidePagePrefix = "page:";
    public const string QuestPrefix = "quest:";

    /// <summary>Separator between a Quest Link's backend source token and its quest code. Unambiguous
    /// because a quest code is always <c>domain:path</c> (colon-delimited) and never contains a slash.</summary>
    private const char SourceSeparator = '/';

    /// <summary>True when <paramref name="target"/> references a Handbook guide page rather than an item.</summary>
    public static bool IsGuidePage(string? target)
        => target is not null && target.StartsWith(GuidePagePrefix, StringComparison.Ordinal);

    /// <summary>The bare Handbook page code for a guide-page target (strips the <see cref="GuidePagePrefix"/>),
    /// or null when the target is null or not a guide page.</summary>
    public static string? PageCode(string? target)
        => IsGuidePage(target) ? target![GuidePagePrefix.Length..] : null;

    /// <summary>Build a guide-page target string from a bare Handbook page code (e.g.
    /// <c>"craftinginfo-knapping"</c> → <c>"page:craftinginfo-knapping"</c>).</summary>
    public static string ForPage(string pageCode) => GuidePagePrefix + pageCode;

    public static bool IsQuest(string? target)
        => target is not null && target.StartsWith(QuestPrefix, StringComparison.Ordinal);

    /// <summary>The backend source token for a quest target (e.g. <see cref="ScribeQuestSource.VsQuest"/>),
    /// or null when <paramref name="target"/> isn't a quest target. A pre-existing target with no
    /// <see cref="SourceSeparator"/> (saved before this backend tag existed) defensively falls back to
    /// <see cref="ScribeQuestSource.VsQuest"/> — the only backend that could have created it — so older
    /// saves keep resolving correctly rather than losing their recorded backend.</summary>
    public static string? QuestSource(string? target)
    {
        if (!IsQuest(target)) return null;
        string remainder = target![QuestPrefix.Length..];
        int slash = remainder.IndexOf(SourceSeparator);
        return slash > 0 ? remainder[..slash] : ScribeQuestSource.VsQuest;
    }

    /// <summary>The quest's own (already domain-qualified) id, stripped of both the <see cref="QuestPrefix"/>
    /// and the backend source tag. Mirrors <see cref="QuestSource"/>'s legacy fallback: a pre-existing target
    /// with no <see cref="SourceSeparator"/> treats its entire remainder as the code.</summary>
    public static string? QuestCode(string? target)
    {
        if (!IsQuest(target)) return null;
        string remainder = target![QuestPrefix.Length..];
        int slash = remainder.IndexOf(SourceSeparator);
        return slash > 0 ? remainder[(slash + 1)..] : remainder;
    }

    public static string ForQuest(string source, string questCode) => QuestPrefix + source + SourceSeparator + questCode;
}
