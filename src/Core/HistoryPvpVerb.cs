namespace Scribe.Core;

/// <summary>Resolves the lang KEY of a weapon-aware PvP verb from stored facts. The existence
/// check is injected so this stays Core-pure (no <c>Lang</c>) while still refusing English-fallback
/// hits — pass a current-locale-only <c>HasTranslation</c>, never a <c>Lang.Get</c> key-echo.</summary>
public static class HistoryPvpVerb
{
    public const string ToolPrefix    = "scribe:scribe-pvp-verb-tool-";
    public const string DamagePrefix  = "scribe:scribe-pvp-verb-damage-";
    public const string GenericPrefix = "scribe:scribe-pvp-verb-generic-";
    public const string FallbackKey   = "scribe:scribe-pvp-verb-damage-bluntattack";

    /// <summary>Three-tier fall-through matching the live write path's stored signal: held-tool
    /// category, then damage type, then the generic pool indexed by <see cref="HistoryFlavor.Slot"/>.
    /// A missing key in <paramref name="hasTranslation"/> is a miss — it must not treat an English
    /// fallback as a hit.</summary>
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
