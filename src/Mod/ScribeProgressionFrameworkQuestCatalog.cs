using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace Scribe;

/// <summary>One objective definition from a Progression Framework quest's catalog entry — a stable
/// <see cref="Code"/> (matched against the same code in the player's <c>WatchedAttributes</c> tree, never
/// by position — Decision 4) and the count required to complete it, read directly from the catalog. Unlike
/// vsquest's <see cref="ScribeQuestObjectiveDef"/>, this needs no positional zip against a separately-read
/// live count: PF's own attribute tree already carries <c>{status, progress}</c> keyed by this same code.</summary>
internal readonly record struct ScribePfObjectiveDef(string Code, int Required);

/// <summary>One Progression Framework quest's catalog entry, richer than the shared
/// <see cref="ScribeQuestCatalogEntry"/> the picker renders (which carries no PF-specific objective shape —
/// see that type's remarks). <see cref="ScribeQuestWatcher"/> keeps its own by-code lookup of these for
/// progress mirroring; <see cref="ToPickerEntry"/> projects the display-only fields a picker needs.</summary>
internal readonly record struct ScribeProgressionFrameworkQuestEntry(
    string QuestCode, string Title, string? Description, IReadOnlyList<ScribePfObjectiveDef> Objectives)
{
    public ScribeQuestCatalogEntry ToPickerEntry() => new(
        ScribeQuestSource.ProgressionFramework, QuestCode, Title, Description,
        Array.Empty<ScribeQuestObjectiveDef>());
}

/// <summary>
/// Reads the installed Progression Framework mod's static <c>config/quests/*.json</c> catalog — the second,
/// independently-gated Quest Link backend (add-progression-framework-quest-support Decision 2). Mirrors
/// <see cref="ScribeQuestCatalog"/>'s shape (soft-dependency, no reflection, no compiled reference,
/// <see cref="IsAvailable"/>-gated shared-asset-system read) but reads Progression Framework's own JSON
/// schema (<c>code</c>, <c>npc</c>, <c>scope</c>, <c>*LangKey</c>, <c>objectives[]</c>, <c>rewards[]</c>,
/// <c>prerequisites[]</c> — verified against Seafarer's real quest files this session, decompiled in
/// <c>reference/ProgressionInvestigations/</c>, gitignored) rather than vsquest's.
///
/// <para>Only player-scoped quests are surfaced (Non-Goal: server-scoped quests have no
/// <c>WatchedAttributes</c> equivalent to auto-detect against, so offering them in the picker would create
/// Quest Links that can never auto-track). Progression Framework's own <c>QuestScope</c> enum defaults to
/// Player when a quest's JSON omits <c>"scope"</c> entirely (confirmed against the decompiled
/// <c>Quest</c>/<c>QuestScope</c> source) — real Seafarer data relies on this: most of its quests omit the
/// field rather than writing <c>"scope": "player"</c> explicitly, so this reader treats an absent/blank
/// scope as player-scoped too, and excludes only an explicit <c>"server"</c>.</para>
///
/// <para>Title/description resolve via each quest's own <c>titleLangKey</c>/<c>descriptionLangKey</c> —
/// unlike vsquest's fixed <c>{id}-title</c>/<c>{id}-desc</c> convention, PF quests carry their own lang key
/// directly in the catalog JSON. Both the quest <c>code</c> and its lang keys may be written BARE (no
/// domain prefix) in the JSON — confirmed against real Seafarer files (e.g. <c>"code": "dawnmarie-orchard"</c>)
/// and against PF's own loader (<c>QuestSystem.LoadQuests</c>'s <c>PrefixIfBare</c>), which prefixes a bare
/// code/lang-key with the ASSET'S OWN domain at load time (never the requesting mod's). This reader
/// replicates that exact prefixing so a picker-created Link's stored code matches what later shows up in
/// the player's own <c>WatchedAttributes</c> tree verbatim.</para>
/// </summary>
internal static class ScribeProgressionFrameworkQuestCatalog
{
    public const string ModId = "progressionframework";
    private const string ServerScope = "server";

    /// <summary>Whether the Quest Link option should offer Progression Framework entries at all.</summary>
    public static bool IsAvailable(ICoreClientAPI capi) => capi.ModLoader.IsModEnabled(ModId);

    /// <summary>Every player-scoped quest in the installed catalog, titled and sorted for a picker. Empty
    /// (never null/throws) when the mod isn't installed or its catalog fails to parse.</summary>
    public static IReadOnlyList<ScribeProgressionFrameworkQuestEntry> ReadCatalog(ICoreClientAPI capi)
    {
        if (!IsAvailable(capi)) return Array.Empty<ScribeProgressionFrameworkQuestEntry>();
        try
        {
            var byLocation = capi.Assets.GetMany<List<RawQuest>>(capi.Logger, "config/quests", ModId);
            var result = new List<ScribeProgressionFrameworkQuestEntry>();
            foreach (var (location, quests) in byLocation)
            {
                string domain = location.Domain;
                foreach (var q in quests)
                {
                    if (string.IsNullOrEmpty(q.Code)) continue;
                    if (string.Equals(q.Scope, ServerScope, StringComparison.OrdinalIgnoreCase)) continue;

                    string code = PrefixIfBare(q.Code, domain)!;
                    string? titleLangKey = PrefixIfBare(q.TitleLangKey, domain);
                    string? descLangKey = PrefixIfBare(q.DescriptionLangKey, domain);
                    result.Add(new ScribeProgressionFrameworkQuestEntry(
                        code,
                        string.IsNullOrEmpty(titleLangKey) ? code : Lang.Get(titleLangKey),
                        !string.IsNullOrEmpty(descLangKey) && Lang.HasTranslation(descLangKey)
                            ? Lang.Get(descLangKey) : null,
                        BuildObjectives(q)));
                }
            }
            return result.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            return Array.Empty<ScribeProgressionFrameworkQuestEntry>();
        }
    }

    /// <summary>Mirrors PF's own <c>QuestSystem.PrefixIfBare</c> exactly: a code/lang-key already containing
    /// a domain (<c>:</c>) is left untouched; a bare one is prefixed with <paramref name="domain"/> — the
    /// asset's own domain, not necessarily <see cref="ModId"/> (a third-party quest pack contributes assets
    /// under its own domain).</summary>
    private static string? PrefixIfBare(string? key, string domain)
        => string.IsNullOrEmpty(key) || key.Contains(':') ? key : domain + ":" + key;

    private static List<ScribePfObjectiveDef> BuildObjectives(RawQuest q)
    {
        if (q.Objectives is null) return new List<ScribePfObjectiveDef>();
        return q.Objectives
            .Where(o => !string.IsNullOrEmpty(o.Code))
            .Select(o => new ScribePfObjectiveDef(o.Code!, o.Required))
            .ToList();
    }

    /// <summary>Formats a Progression Framework quest's per-objective status (Decision 4) as "N of M
    /// objectives complete" for a multi-objective quest, or the bare status word for a single-objective one
    /// (the requirement's "accept/complete state only, without needing an aggregate count" scenario). Null
    /// when there's nothing to show (no cataloged objectives, or none read from the attribute tree yet).</summary>
    public static string? FormatProgress(
        IReadOnlyList<ScribePfObjectiveDef> objectives, IReadOnlyDictionary<string, string> statusByCode)
    {
        if (objectives.Count == 0) return null;

        if (objectives.Count == 1)
        {
            return statusByCode.TryGetValue(objectives[0].Code, out string? status) && !string.IsNullOrEmpty(status)
                ? Lang.Get("scribe:scribe-questobjective-pf-status", status)
                : null;
        }

        // "completed" — matches QuestSystem's own objective-tree status string exactly (confirmed against
        // the decompiled source: GetOrAddObjectiveTree seeds "pending", CompleteObjective writes "completed").
        int complete = objectives.Count(o =>
            statusByCode.TryGetValue(o.Code, out string? status)
            && string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase));
        return Lang.Get("scribe:scribe-questobjective-pf-aggregate", complete, objectives.Count);
    }

    /// <summary>The fields this reader needs from a PF quest catalog entry — mirrors Progression
    /// Framework's own <c>Quest</c> JSON shape without referencing that mod's assembly.</summary>
    private sealed class RawQuest
    {
        [JsonProperty("code")]
        public string? Code { get; set; }

        [JsonProperty("npc")]
        public string? Npc { get; set; }

        [JsonProperty("scope")]
        public string? Scope { get; set; }

        [JsonProperty("titleLangKey")]
        public string? TitleLangKey { get; set; }

        [JsonProperty("descriptionLangKey")]
        public string? DescriptionLangKey { get; set; }

        [JsonProperty("objectives")]
        public List<RawObjective>? Objectives { get; set; }

        [JsonProperty("rewards")]
        public List<object>? Rewards { get; set; }

        [JsonProperty("prerequisites")]
        public List<string>? Prerequisites { get; set; }
    }

    /// <summary>Mirrors Progression Framework's own <c>Objective</c> JSON shape. Only <see cref="Code"/> and
    /// <see cref="Required"/> are read by this reader — <see cref="Type"/>/<see cref="Items"/>/
    /// <see cref="Pattern"/> describe how PF itself validates delivery/kill/etc. progress and have no
    /// bearing on Scribe's read-only progress mirroring (which reads PF's own already-computed status
    /// instead of re-deriving it).</summary>
    private sealed class RawObjective
    {
        [JsonProperty("code")]
        public string? Code { get; set; }

        [JsonProperty("type")]
        public string? Type { get; set; }

        [JsonProperty("items")]
        public List<string>? Items { get; set; }

        [JsonProperty("required")]
        public int Required { get; set; }

        [JsonProperty("pattern")]
        public string? Pattern { get; set; }
    }
}
