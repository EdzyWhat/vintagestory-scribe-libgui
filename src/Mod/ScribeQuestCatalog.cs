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
/// One entry from an installed quest mod's catalog, resolved for the Quest Link picker.
/// <see cref="QuestCode"/> is the quest's own domain-qualified id (e.g. <c>"vsquest:quest-freeghost"</c>),
/// stored verbatim after the <c>"quest:"</c> prefix (<see cref="Scribe.Core.ScribeLinkTarget.ForQuest"/>)
/// so auto-detect can compare against the same string. <see cref="Source"/>
/// (<see cref="Scribe.Core.ScribeQuestSource"/>) is always recorded on the created Link.
/// <see cref="Objectives"/> is vsquest-specific (positional tracking) and always empty for a Progression
/// Framework entry, which is read by objective code instead. It includes static gather objectives; live
/// progress formatting filters those out because vsquest exposes no gather counter.
/// </summary>
internal readonly record struct ScribeQuestCatalogEntry(
    string Source, string QuestCode, string Title, string? Description,
    IReadOnlyList<ScribeQuestObjectiveDef> Objectives);

/// <summary>Which vsquest tracker list an objective belongs to. Order matches
/// <c>VsQuest.ActiveQuest.trackerProgress()</c>'s concatenation (kill, block-place, block-break), which is how
/// live counts line up positionally with objectives. Gather is static-only (no live count).</summary>
internal enum ScribeQuestObjectiveKind { Kill, BlockPlace, BlockBreak, Gather }

/// <summary>One static objective (a required count of a matching entity/block code), paired positionally
/// with a live <c>EventTracker.count</c> by <see cref="ScribeQuestWatcher"/>. Gather objectives live here
/// too; <see cref="ScribeQuestCatalog.FormatProgress"/> excludes them from the positional zip.</summary>
internal readonly record struct ScribeQuestObjectiveDef(
    ScribeQuestObjectiveKind Kind, IReadOnlyList<string> ValidCodes, int Demand);

/// <summary>An exact objective match resolved against the live registries. <see cref="HasItemStack"/> says
/// whether the row can render a real inventory icon (entities use the generic objective icon).</summary>
internal readonly record struct ScribeQuestObjectiveResolution(
    string Code, string DisplayName, bool HasItemStack);

/// <summary>
/// Reads the installed quest mod's static <c>config/quests/*.json</c> catalog. Reflection- and
/// dependency-free: it parses the same JSON assets with a local DTO and never touches the vsquest assembly,
/// gated only on <see cref="IsAvailable"/> (same soft-dependency pattern as <see cref="CarryOnBridge"/>).
///
/// <para>Searches every installed mod's domain, as <c>VsQuest.QuestSystem.AssetsLoaded</c> does, because
/// quest content comes from separate dependent mods under their own domains.</para>
///
/// <para>Title/description come from <c>Lang.Get(id + "-title"/"-desc")</c> (the JSON carries no display
/// text). They are captured once at picker-open and persisted into the created Link, which never re-reads
/// this catalog (orphan-safe).</para>
/// </summary>
internal static class ScribeQuestCatalog
{
    public const string VsQuestModId = "vsquest";
    private const string StaticObjectiveCodePrefix = "vsquest-objective-";

    /// <summary>Whether a QuestObjective stable key belongs to the VS Quest catalog path. Works from pin
    /// snapshots, so it needs no loaded document.</summary>
    public static bool IsStaticObjectiveCode(string? code)
        => code?.StartsWith(StaticObjectiveCodePrefix, StringComparison.Ordinal) == true;

    /// <summary>Formats live counts against their static objectives as "Kills 2/5 · Blocks placed 0/3",
    /// zipping the lists positionally (see <see cref="ScribeQuestObjectiveDef"/>). Null when either list is
    /// empty. Gather objectives are never included.</summary>
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

    /// <summary>Resolves an objective with exactly one concrete code (gather: item registry; place/break:
    /// block; kill: entity). Wildcards, alternates, malformed codes and registry misses return null so the
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

    /// <summary>Converts objectives into the Core reconciliation shape used by manual Link creation and the
    /// accept-time auto-link. The positional key only needs to distinguish siblings under one parent.</summary>
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

    public static bool IsAvailable(ICoreClientAPI capi) => capi.ModLoader.IsModEnabled(VsQuestModId);

    /// <summary>Every quest in the installed <c>vsquest</c> catalog, titled and sorted for a picker. Empty
    /// (never null/throws) when vsquest isn't installed or its catalog fails to parse.</summary>
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

    /// <summary>Flattens objectives in the SAME order <c>ActiveQuest.trackerProgress()</c> concatenates counts
    /// (kill, place, break), with static-only gather last.</summary>
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

    /// <summary>Mirrors <c>VsQuest.Quest</c>'s JSON shape without referencing its assembly.</summary>
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
