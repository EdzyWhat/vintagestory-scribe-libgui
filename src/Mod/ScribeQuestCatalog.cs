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
internal readonly record struct ScribeQuestCatalogEntry(string QuestCode, string Title, string? Description);

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
                    Lang.HasTranslation(q.Id + "-desc") ? Lang.Get(q.Id + "-desc") : null))
                .OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<ScribeQuestCatalogEntry>();
        }
    }

    /// <summary>The one field this reader needs from a quest catalog entry — mirrors
    /// <c>VsQuest.Quest.id</c>'s JSON shape without referencing that type/assembly.</summary>
    private sealed class RawQuest
    {
        [JsonProperty("id")]
        public string? Id { get; set; }
    }
}
