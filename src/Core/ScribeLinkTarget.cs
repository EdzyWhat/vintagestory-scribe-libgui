using System;

namespace Scribe.Core;

/// <summary>
/// Helpers for interpreting a <see cref="ScribeBlock.LinkTarget"/> string. A Link's target is EITHER a plain
/// collectible code (an item/block Link — the Mod layer resolves it live to an <c>ItemStack</c> for its
/// icon + name) OR a Handbook <b>guide-page</b> reference marked by the <see cref="GuidePagePrefix"/> (a
/// non-item guide/explainer page, e.g. <c>"page:craftinginfo-knapping"</c> — it has no item, so its display
/// name is stored in <see cref="ScribeBlock.LinkLabel"/> and its icon is a generic book).
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
}
