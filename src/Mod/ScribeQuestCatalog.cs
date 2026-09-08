using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Scribe;

/// <summary>
/// One entry from an installed quest mod's catalog, resolved for the Quest Link picker
/// (add-assignment-and-quest-support 10.1). <see cref="QuestCode"/> is the quest's own (already
/// domain-qualified) id, e.g. <c>"vsquest:quest-freeghost"</c> — stored verbatim after the <c>"quest:"</c>
/// prefix (<see cref="Scribe.Core.ScribeLinkTarget.ForQuest"/>), so a Layer 2 auto-detect correlation can
/// compare against the exact same string later without re-deriving anything. <see cref="Source"/> is the
/// backend mod that owns this entry (<see cref="Scribe.Core.ScribeQuestSource"/> —
/// add-progression-framework-quest-support Decision 1), always recorded on the created Link so later
/// resolution never has to guess which backend a quest code came from. <see cref="Objectives"/> is
/// vsquest-specific (positional kill/place/break tracking, §11.3) — a Progression Framework entry from
/// <see cref="ScribeProgressionFrameworkQuestCatalog"/> always leaves it empty; its own per-objective
/// progress is read by objective code, not by position (Decision 4), through
/// <see cref="ScribeQuestWatcher"/>'s separate PF-specific surface. For vsquest, it includes the
/// static gather objectives used by one-shot QuestObjective generation; live progress formatting
/// filters those back out because vsquest exposes no gather counter.
/// </summary>
internal readonly record struct ScribeQuestCatalogEntry(
    string Source, string QuestCode, string Title, string? Description,
    IReadOnlyList<ScribeQuestObjectiveDef> Objectives);

/// <summary>Which vsquest tracker list an objective belongs to (add-assignment-and-quest-support §11
/// progress mirroring) — matches <c>VsQuest.ActiveQuest.trackerProgress()</c>'s fixed concatenation
/// order (kill, then block-place, then block-break), which is how a live tracker count read off the
/// open quest dialog lines up positionally with the static objective it belongs to. Gather is a
/// static-only kind and has no corresponding live tracker count.</summary>
internal enum ScribeQuestObjectiveKind { Kill, BlockPlace, BlockBreak, Gather }

/// <summary>One static objective definition (a required count of a matching entity/block code),
/// paired positionally with a live <c>EventTracker.count</c> read off <c>VsQuest.ActiveQuest</c> by
/// <see cref="ScribeQuestWatcher"/> to render "count/demand" progress (§11.3). Gather objectives also
/// live here for one-shot static criteria generation; <see cref="ScribeQuestCatalog.FormatProgress"/>
/// excludes them from the positional live-count zip because vsquest exposes no gather counter.</summary>
internal readonly record struct ScribeQuestObjectiveDef(
    ScribeQuestObjectiveKind Kind, IReadOnlyList<string> ValidCodes, int Demand);

/// <summary>An exact objective match resolved against the live game registries. <see cref="Code"/> is
/// the objective's sole concrete match code, <see cref="DisplayName"/> is its localized player-facing
/// name, and <see cref="HasItemStack"/> says whether the current QuestObjective row can render it with
/// a real inventory icon. Entities resolve to a name and use the generic objective icon.</summary>
internal readonly record struct ScribeQuestObjectiveResolution(
    string Code, string DisplayName, bool HasItemStack);

/// <summary>
/// Reads the installed quest mod's static <c>config/quests/*.json</c> catalog (design.md Decision 10,
/// "Layer 1"). Deliberately reflection- and dependency-free: unlike the Layer 2 soft auto-detect (§11,
/// which reaches into <c>VsQuest.QuestSelectGui</c> internals via Harmony/reflection), Layer 1 only needs
/// the quest ids/titles/descriptions that <c>vsquest</c> itself loads from these same JSON assets — so it
/// reads them itself with a tiny local DTO, never touching the vsquest assembly at all. This keeps the
/// "no new mod dependencies" guardrail intact: <c>vsquest</c> is optional and gated purely on
/// <see cref="IsAvailable"/> (<c>IsModEnabled</c>), the same pattern <c>ConfigLib</c> and
/// <see cref="CarryOnBridge"/> use for their own soft dependencies.
///
/// <para>Searches every installed mod's domain, not just <c>vsquest</c>'s own, mirroring how
/// <c>VsQuest.QuestSystem.AssetsLoaded</c> itself scans every installed mod's domain — real quest content is
/// always contributed by a separate dependent mod under its own domain (e.g. VS Village)
/// (fix-quest-catalog-domain-scoping).</para>
///
/// <para>Title/description are resolved via <c>Lang.Get(id + "-title"/"-desc")</c> — the catalog JSON
/// carries no display text of its own (confirmed by decompiling <c>VsQuest.QuestSelectGui</c>). Captured
/// once at picker-open time; the caller persists the resolved strings into the created Link block, which
/// never re-reads this catalog (orphan-safe per Decision 10).</para>
/// </summary>
internal static class ScribeQuestCatalog
{
    public const string VsQuestModId = "vsquest";
    private const string StaticObjectiveCodePrefix = "vsquest-objective-";

    /// <summary>Whether a QuestObjective stable key belongs to the one-shot VS Quest catalog path.
    /// The key survives in pin snapshots, so this remains available when the owning document is
    /// unloaded.</summary>
    public static bool IsStaticObjectiveCode(string? code)
        => code?.StartsWith(StaticObjectiveCodePrefix, StringComparison.Ordinal) == true;

    /// <summary>Formats live kill/block-place/block-break counts against their static objectives as a
    /// compact "Kills 2/5 · Blocks placed 0/3" line (§11.3), zipping the two lists positionally (index
    /// <c>i</c> in one is objective/count <c>i</c> in the other — see <see cref="ScribeQuestObjectiveDef"/>'s
    /// doc-comment). Null when either list is empty, so a caller can treat null as "nothing to show"
    /// (no gather-objective progress is ever included here — a permanent gap, design.md Decision 10).</summary>
    public static string? FormatProgress(
        IReadOnlyList<ScribeQuestObjectiveDef> objectives, IReadOnlyList<int> counts)
    {
        if (objectives.Count == 0 || counts.Count == 0) return null;
        var trackedObjectives = objectives.Where(o => o.Kind != ScribeQuestObjectiveKind.Gather).ToList();
        var parts = new List<string>();
        for (int i = 0; i < trackedObjectives.Count && i < counts.Count; i++)
        {
            string kindLabel = KindLabel(trackedObjectives[i].Kind);
            int demand = trackedObjectives[i].Demand;
            parts.Add($"{kindLabel} {Math.Min(counts[i], demand)}/{demand}");
        }
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>Resolve an objective whose <c>validCodes</c> contains exactly one concrete code. Gather
    /// objectives use the item registry, place/break use the block registry, and kill uses the entity
    /// registry. Wildcards, alternate codes, malformed codes, and registry misses return null so the
    /// caller can use <see cref="FormatFallbackLabel"/>.</summary>
    public static ScribeQuestObjectiveResolution? ResolveExactObjective(
        ICoreClientAPI capi, ScribeQuestObjectiveDef objective)
    {
        if (objective.ValidCodes.Count != 1) return null;
        string code = objective.ValidCodes[0];
        if (string.IsNullOrWhiteSpace(code) || code.Contains('*')) return null;

        AssetLocation location;
        try { location = new AssetLocation(code); }
        catch { return null; }

        switch (objective.Kind)
        {
            case ScribeQuestObjectiveKind.Gather:
                var item = capi.World.GetItem(location);
                return item is null ? null
                    : new ScribeQuestObjectiveResolution(code, new ItemStack(item).GetName(), true);

            case ScribeQuestObjectiveKind.BlockPlace:
            case ScribeQuestObjectiveKind.BlockBreak:
                var block = capi.World.GetBlock(location);
                return block is null ? null
                    : new ScribeQuestObjectiveResolution(code, new ItemStack(block).GetName(), true);

            case ScribeQuestObjectiveKind.Kill:
                if (capi.World.GetEntityType(location) is null) return null;
                string langKey = $"{location.Domain}:item-creature-{location.Path}";
                string displayName = Lang.HasTranslation(langKey) ? Lang.Get(langKey) : code;
                return new ScribeQuestObjectiveResolution(code, displayName, false);

            default:
                return null;
        }
    }

    /// <summary>Localized generic label for a wildcard, alternate-code, or unresolved objective.</summary>
    public static string FormatFallbackLabel(ScribeQuestObjectiveDef objective)
        => $"{KindLabel(objective.Kind)} {objective.Demand}";

    /// <summary>Convert all modeled objectives into the Core reconciliation shape used by both manual
    /// Quest Link creation and the accept-time auto-link message. The positional key is private to the
    /// parent Link and only needs to distinguish siblings because VS Quest criteria are generated once.</summary>
    public static IReadOnlyList<(string Code, string? ItemCode, string? Label, int Required)>
        BuildStaticObjectives(ICoreClientAPI capi, IReadOnlyList<ScribeQuestObjectiveDef> objectives)
        => objectives.Select((objective, index) =>
        {
            var resolved = ResolveExactObjective(capi, objective);
            string? itemCode = resolved is { HasItemStack: true } ? resolved.Value.Code : null;
            string label = resolved?.DisplayName ?? FormatFallbackLabel(objective);
            return ($"{StaticObjectiveCodePrefix}{index}", itemCode, (string?)label, objective.Demand);
        }).ToList();

    private static string KindLabel(ScribeQuestObjectiveKind kind) => kind switch
    {
        ScribeQuestObjectiveKind.Kill => Lang.Get("scribe:scribe-questobjective-kill"),
        ScribeQuestObjectiveKind.BlockPlace => Lang.Get("scribe:scribe-questobjective-blockplace"),
        ScribeQuestObjectiveKind.BlockBreak => Lang.Get("scribe:scribe-questobjective-blockbreak"),
        _ => Lang.Get("scribe:scribe-questobjective-gather"),
    };

    /// <summary>Whether the Quest Link option should be offered at all.</summary>
    public static bool IsAvailable(ICoreClientAPI capi) => capi.ModLoader.IsModEnabled(VsQuestModId);

    /// <summary>Every quest in the installed <c>vsquest</c> catalog, titled and sorted for a picker.
    /// Empty (never null/throws) when vsquest isn't installed or its catalog fails to parse — a picker
    /// with nothing to show is the caller's problem, not this reader's.</summary>
    public static IReadOnlyList<ScribeQuestCatalogEntry> ReadCatalog(ICoreClientAPI capi)
    {
        if (!IsAvailable(capi)) return Array.Empty<ScribeQuestCatalogEntry>();
        try
        {
            var byLocation = capi.Assets.GetMany<List<RawQuest>>(capi.Logger, "config/quests", null);
            return byLocation.Values
                .SelectMany(list => list)
                .Where(q => !string.IsNullOrEmpty(q.Id))
                .Select(q => new ScribeQuestCatalogEntry(
                    ScribeQuestSource.VsQuest,
                    q.Id!,
                    Lang.Get(q.Id! + "-title"),
                    Lang.HasTranslation(q.Id + "-desc") ? Lang.Get(q.Id + "-desc") : null,
                    BuildObjectives(q)))
                .OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<ScribeQuestCatalogEntry>();
        }
    }

    /// <summary>Flattens a raw quest's modeled objective lists into one ordered list. The live-tracked
    /// prefix stays in the SAME order <c>ActiveQuest.trackerProgress()</c> concatenates its counts (kill,
    /// then place, then break). Gather follows that prefix because it is static-only; <see
    /// cref="FormatProgress"/> also filters it explicitly before the positional zip.</summary>
    private static List<ScribeQuestObjectiveDef> BuildObjectives(RawQuest q)
    {
        var result = new List<ScribeQuestObjectiveDef>();
        void AddAll(ScribeQuestObjectiveKind kind, List<RawObjective>? objectives)
        {
            if (objectives is null) return;
            foreach (var o in objectives)
                result.Add(new ScribeQuestObjectiveDef(kind, o.ValidCodes ?? new List<string>(), o.Demand));
        }
        AddAll(ScribeQuestObjectiveKind.Kill, q.KillObjectives);
        AddAll(ScribeQuestObjectiveKind.BlockPlace, q.BlockPlaceObjectives);
        AddAll(ScribeQuestObjectiveKind.BlockBreak, q.BlockBreakObjectives);
        AddAll(ScribeQuestObjectiveKind.Gather, q.GatherObjectives);
        return result;
    }

    /// <summary>The fields this reader needs from a quest catalog entry — mirrors <c>VsQuest.Quest</c>'s
    /// JSON shape without referencing that type/assembly.</summary>
    private sealed class RawQuest
    {
        [JsonProperty("id")]
        public string? Id { get; set; }

        [JsonProperty("killObjectives")]
        public List<RawObjective>? KillObjectives { get; set; }

        [JsonProperty("gatherObjectives")]
        public List<RawObjective>? GatherObjectives { get; set; }

        [JsonProperty("blockPlaceObjectives")]
        public List<RawObjective>? BlockPlaceObjectives { get; set; }

        [JsonProperty("blockBreakObjectives")]
        public List<RawObjective>? BlockBreakObjectives { get; set; }
    }

    /// <summary>Mirrors <c>VsQuest.Objective</c>'s JSON shape (a required count of any matching code).</summary>
    private sealed class RawObjective
    {
        [JsonProperty("validCodes")]
        public List<string>? ValidCodes { get; set; }

        [JsonProperty("demand")]
        public int Demand { get; set; }
    }
}
