using Scribe.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Scribe;

/// <summary>Formats live-schema History rows in the viewing client's locale. Baked / Manual /
/// Crafted / PickedUp rows are not formatted here — the GUI keeps today's string concat for those.
/// Pool and PvP-verb existence checks use <see cref="Lang.HasTranslation"/> (current locale only),
/// never <see cref="Lang.Get"/>, so an English fallback cannot count as a hit in a short locale.</summary>
internal static class HistoryDisplay
{
    private static readonly Dictionary<string, int> PoolSizeCache = new();

    internal static bool IsLiveSystemRow(HistoryEntry entry)
        => entry.Schema == HistorySchema.Live && entry.Kind is
            HistoryEventKind.Death or HistoryEventKind.PvpKill
            or HistoryEventKind.BossKill or HistoryEventKind.TemporalStorm;

    internal static string Date(HistoryEntry entry, IWorldAccessor world)
        => IsLiveSystemRow(entry)
            ? NotebookHost.FormatDateFromTimestamp(world, entry.InGameTimestamp)
            : entry.InGameDate;

    internal static string Body(HistoryEntry entry)
    {
        if (IsLiveSystemRow(entry)) return Sentence(entry);
        return entry.ActorName.Length > 0
            ? $"{entry.ActorName}{(entry.Detail.Length > 0 ? " — " + entry.Detail : "")}"
            : entry.Detail;
    }

    internal static string Sentence(HistoryEntry entry) => entry.Kind switch
    {
        HistoryEventKind.Death         => DeathSentence(entry),
        HistoryEventKind.PvpKill       => PvpKillSentence(entry),
        HistoryEventKind.BossKill      => Lang.Get("scribe:scribe-history-boss-" + entry.RefCode, entry.SubjectName),
        HistoryEventKind.TemporalStorm => StormSentence(entry),
        _                              => entry.Detail,
    };

    private static string DeathSentence(HistoryEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.OtherName))
            return PvpDeathSentence(entry);
        if (entry.RefCode.Contains(':'))
            return CreatureDeathSentence(entry);
        return EnvironmentalDeathSentence(entry);
    }

    private static string CreatureDeathSentence(HistoryEntry entry)
    {
        string creature = CreatureName(entry.RefCode);
        int pool = ProbePoolSize("scribe:scribe-mob-death-");
        if (pool > 0)
        {
            int slot = HistoryFlavor.Slot(entry.FlavorSeed, pool);
            return Lang.Get($"scribe:scribe-mob-death-{slot}", entry.SubjectName, creature);
        }
        return Lang.Get("scribe:death-slain-by", entry.SubjectName, creature);
    }

    private static string EnvironmentalDeathSentence(HistoryEntry entry)
    {
        if (string.IsNullOrEmpty(entry.RefCode))
            return Lang.Get("scribe:death-generic", entry.SubjectName);

        long abs = Math.Abs((long)entry.FlavorSeed);
        for (int maxN = 4; maxN >= 1; maxN--)
        {
            string key = $"deathmsg-{entry.RefCode}-{(abs % maxN) + 1}";
            if (HasKey(key)) return Lang.Get(key, entry.SubjectName);
        }
        return Lang.Get("scribe:death-generic", entry.SubjectName);
    }

    private static string PvpDeathSentence(HistoryEntry entry)
    {
        string verbKey = ResolvePvpVerb(entry);
        string participleKey = verbKey + "-participle";
        string participle = HasKey(participleKey) ? Lang.Get(participleKey) : Lang.Get(verbKey);
        return Lang.Get("scribe:scribe-pvp-death-message", entry.SubjectName, participle, entry.OtherName);
    }

    private static string PvpKillSentence(HistoryEntry entry)
    {
        string verbKey = ResolvePvpVerb(entry);
        return Lang.Get("scribe:scribe-pvp-kill-message", entry.OtherName, Lang.Get(verbKey), entry.SubjectName);
    }

    private static string ResolvePvpVerb(HistoryEntry entry)
    {
        int genericPool = ProbePoolSize(HistoryPvpVerb.GenericPrefix);
        return HistoryPvpVerb.ResolveKey(entry.RefCode, entry.RefCode2, entry.FlavorSeed, genericPool, HasKey);
    }

    private static string StormSentence(HistoryEntry entry)
    {
        string key = "scribe:storm-strength-" + entry.RefCode;
        string value = Lang.Get(key);
        return value == key ? entry.RefCode : value;
    }

    /// <summary>Mirrors <c>Entity.GetPrefixAndCreatureName</c> from a stored <c>domain:path</c>
    /// code, so display does not need the dead entity instance.</summary>
    internal static string CreatureName(string entityCode)
    {
        int colon = entityCode.IndexOf(':');
        string domain = colon >= 0 ? entityCode[..colon] : "game";
        string path   = colon >= 0 ? entityCode[(colon + 1)..] : entityCode;
        string locale = Lang.CurrentLocale;
        if (Lang.AvailableLanguages.TryGetValue(locale, out var lang))
        {
            string key = domain + ":prefixandcreature-" + path;
            string old = domain + ":prefixandcreature-" + path.Replace("-", "");
            string? name = lang.GetMatchingIfExists(key) ?? lang.GetMatchingIfExists(old);
            if (!string.IsNullOrEmpty(name)) return name;
        }
        return Lang.Get("generic-wildanimal");
    }

    private static int ProbePoolSize(string keyPrefix)
    {
        string cacheKey = Lang.CurrentLocale + "\0" + keyPrefix;
        if (PoolSizeCache.TryGetValue(cacheKey, out int cached)) return cached;
        int n = 0;
        while (HasKey(keyPrefix + n)) n++;
        PoolSizeCache[cacheKey] = n;
        return n;
    }

    private static bool HasKey(string key)
        => Lang.HasTranslation(key, findWildcarded: false, logErrors: false);
}
