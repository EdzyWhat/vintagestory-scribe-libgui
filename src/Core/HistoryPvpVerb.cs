namespace Scribe.Core;

/// <summary>Resolves the lang KEY of a weapon-aware PvP verb. The injected existence check keeps this
/// Core-pure; pass a current-locale-only <c>HasTranslation</c>, never a <c>Lang.Get</c> key-echo.</summary>
public static class HistoryPvpVerb
{
    public const string ToolPrefix    = "scribe:scribe-pvp-verb-tool-";
    public const string DamagePrefix  = "scribe:scribe-pvp-verb-damage-";
    public const string GenericPrefix = "scribe:scribe-pvp-verb-generic-";
    public const string FallbackKey   = "scribe:scribe-pvp-verb-damage-bluntattack";

    /// <summary>Fall-through: held-tool category, then damage type, then the generic pool indexed by
    /// <see cref="HistoryFlavor.Slot"/>. A key missing in <paramref name="hasTranslation"/> is a miss.</summary>
    public static string ResolveKey(
        string? tool,
        string? damageType,
        int seed,
        int genericPoolSize,
        Func<string, bool> hasTranslation)
    {
        if (!string.IsNullOrEmpty(tool))
        {
            string toolKey = ToolPrefix + tool;
            if (hasTranslation(toolKey)) return toolKey;
        }

        if (!string.IsNullOrEmpty(damageType))
        {
            string dmgKey = DamagePrefix + damageType;
            if (hasTranslation(dmgKey)) return dmgKey;
        }

        if (genericPoolSize > 0)
            return GenericPrefix + HistoryFlavor.Slot(seed, genericPoolSize);

        return FallbackKey;
    }
}
