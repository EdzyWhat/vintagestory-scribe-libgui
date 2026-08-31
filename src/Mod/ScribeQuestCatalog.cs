using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace Scribe;

/// <summary>
/// One entry from an installed quest mod's catalog, resolved for the Quest Link picker
/// (add-assignment-and-quest-support 10.1). <see cref="QuestCode"/> is the quest's own (already
/// domain-qualified) id, e.g. <c>"vsquest:quest-freeghost"</c> — stored verbatim after the <c>"quest:"</c>
/// prefix (<see cref="Scribe.Core.ScribeLinkTarget.ForQuest"/>), so a Layer 2 auto-detect correlation can
/// compare against the exact same string later without re-deriving anything.
/// </summary>
internal readonly record struct ScribeQuestCatalogEntry(
    string QuestCode, string Title, string? Description,
    IReadOnlyList<ScribeQuestObjectiveDef> Objectives);

/// <summary>Which vsquest tracker list an objective belongs to (add-assignment-and-quest-support §11
/// progress mirroring) — matches <c>VsQuest.ActiveQuest.trackerProgress()</c>'s fixed concatenation
/// order (kill, then block-place, then block-break), which is how a live tracker count read off the
/// open quest dialog lines up positionally with the static objective it belongs to.</summary>
internal enum ScribeQuestObjectiveKind { Kill, BlockPlace, BlockBreak }

/// <summary>One static objective definition (a required count of a matching entity/block code),
/// paired positionally with a live <c>EventTracker.count</c> read off <c>VsQuest.ActiveQuest</c> by
/// <see cref="ScribeQuestWatcher"/> to render "count/demand" progress (§11.3). Gather objectives have
/// no such counter in vsquest (it scans inventory on demand instead) and so are never represented
/// here — a permanent, documented gap (design.md Decision 10).</summary>
internal readonly record struct ScribeQuestObjectiveDef(
    ScribeQuestObjectiveKind Kind, IReadOnlyList<string> ValidCodes, int Demand);

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
/// <para>Scoped to the <c>vsquest</c> domain's own catalog only — a third-party datapack contributing
/// quests under its own mod domain (mirroring how <c>VsQuest.QuestSystem.AssetsLoaded</c> itself scans
/// every installed mod's domain) is a disclosed, out-of-scope edge case (add-assignment-and-quest-support
/// 10.1's note in tasks.md).</para>
///
/// <para>Title/description are resolved via <c>Lang.Get(id + "-title"/"-desc")</c> — the catalog JSON
/// carries no display text of its own (confirmed by decompiling <c>VsQuest.QuestSelectGui</c>). Captured
/// once at picker-open time; the caller persists the resolved strings into the created Link block, which
/// never re-reads this catalog (orphan-safe per Decision 10).</para>
/// </summary>
internal static class ScribeQuestCatalog
{
    public const string VsQuestModId = "vsquest";

    /// <summary>Formats live kill/block-place/block-break counts against their static objectives as a
    /// compact "Kills 2/5 · Blocks placed 0/3" line (§11.3), zipping the two lists positionally (index
    /// <c>i</c> in one is objective/count <c>i</c> in the other — see <see cref="ScribeQuestObjectiveDef"/>'s
    /// doc-comment). Null when either list is empty, so a caller can treat null as "nothing to show"
    /// (no gather-objective progress is ever included here — a permanent gap, design.md Decision 10).</summary>
    public static string? FormatProgress(
        IReadOnlyList<ScribeQuestObjectiveDef> objectives, IReadOnlyList<int> counts)
    {
        if (objectives.Count == 0 || counts.Count == 0) return null;
        var parts = new List<string>();
        for (int i = 0; i < objectives.Count && i < counts.Count; i++)
        {
            string kindLabel = objectives[i].Kind switch
            {
                ScribeQuestObjectiveKind.Kill => Lang.Get("scribe:scribe-questobjective-kill"),
                ScribeQuestObjectiveKind.BlockPlace => Lang.Get("scribe:scribe-questobjective-blockplace"),
                _ => Lang.Get("scribe:scribe-questobjective-blockbreak"),
            };
            int demand = objectives[i].Demand;
            parts.Add($"{kindLabel} {Math.Min(counts[i], demand)}/{demand}");
        }
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

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
            var byLocation = capi.Assets.GetMany<List<RawQuest>>(capi.Logger, "config/quests", VsQuestModId);
            return byLocation.Values
                .SelectMany(list => list)
                .Where(q => !string.IsNullOrEmpty(q.Id))
                .Select(q => new ScribeQuestCatalogEntry(
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

    /// <summary>Flattens a raw quest's kill/block-place/block-break objective lists into one ordered list,
    /// in the SAME order <c>ActiveQuest.trackerProgress()</c> concatenates its live tracker counts (kill,
    /// then place, then break) — <see cref="ScribeQuestWatcher"/> zips this list against that live list
    /// positionally, so the order here must not change independently of vsquest's own.</summary>
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
