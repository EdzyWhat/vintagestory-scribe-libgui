using System;

namespace Scribe.Core;

/// <summary>Which quest mod backs a Quest Link: a fixed short token in the <c>quest:</c> target string,
/// never inferred from the quest code's own domain.</summary>
public static class ScribeQuestSource
{
    public const string VsQuest = "vsquest";
    public const string ProgressionFramework = "progressionframework";
}

/// <summary>
/// Helpers for interpreting a <see cref="ScribeBlock.LinkTarget"/> string. A target is EITHER a plain
/// collectible code (an item/block Link, resolved live by the Mod layer), OR a Handbook guide-page
/// reference marked by <see cref="GuidePagePrefix"/> (no item, so its name lives in
/// <see cref="ScribeBlock.LinkLabel"/>), OR a quest reference <c>quest:{source}/{code}</c> scoped to the
/// backend mod that owns the quest. Pure string logic so every surface classifies a link identically.
/// </summary>
public static class ScribeLinkTarget
{
    /// <summary>Prefix marking a guide-page target; unambiguous because a collectible code's domain is never <c>"page"</c>.</summary>
    public const string GuidePagePrefix = "page:";
    public const string QuestPrefix = "quest:";

    /// <summary>Separator between a quest's backend source token and its quest code (a quest code is <c>domain:path</c> and never contains a slash).</summary>
    private const char SourceSeparator = '/';

    /// <summary>True when <paramref name="target"/> references a Handbook guide page.</summary>
    public static bool IsGuidePage(string? target)
        => target is not null && target.StartsWith(GuidePagePrefix, StringComparison.Ordinal);

    /// <summary>The bare page code for a guide-page target, or null when it isn't one.</summary>
    public static string? PageCode(string? target)
        => IsGuidePage(target) ? target![GuidePagePrefix.Length..] : null;

    /// <summary>Builds a guide-page target from a bare page code.</summary>
    public static string ForPage(string pageCode) => GuidePagePrefix + pageCode;

    public static bool IsQuest(string? target)
        => target is not null && target.StartsWith(QuestPrefix, StringComparison.Ordinal);

    /// <summary>The backend source token for a quest target, or null if it isn't one. A target with no
    /// <see cref="SourceSeparator"/> (saved before backend tags existed) falls back to
    /// <see cref="ScribeQuestSource.VsQuest"/>, the only backend that could have created it.</summary>
    public static string? QuestSource(string? target)
    {
        if (!IsQuest(target)) return null;
        string remainder = target![QuestPrefix.Length..];
        int slash = remainder.IndexOf(SourceSeparator);
        return slash > 0 ? remainder[..slash] : ScribeQuestSource.VsQuest;
    }

    /// <summary>The quest's domain-qualified id without prefix or source tag. A target with no
    /// <see cref="SourceSeparator"/> treats its whole remainder as the code (same legacy fallback).</summary>
    public static string? QuestCode(string? target)
    {
        if (!IsQuest(target)) return null;
        string remainder = target![QuestPrefix.Length..];
        int slash = remainder.IndexOf(SourceSeparator);
        return slash > 0 ? remainder[(slash + 1)..] : remainder;
    }

    public static string ForQuest(string source, string questCode) => QuestPrefix + source + SourceSeparator + questCode;
}
