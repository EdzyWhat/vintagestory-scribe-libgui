using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Scribe;

/// <summary>One objective definition from a Progression Framework quest's catalog entry — a stable
/// <see cref="Code"/> (matched against the same code in the player's <c>WatchedAttributes</c> tree, never
/// by position — Decision 4) and the count required to complete it, read directly from the catalog. Unlike
/// vsquest's <see cref="ScribeQuestObjectiveDef"/>, this needs no positional zip against a separately-read
/// live count: PF's own attribute tree already carries <c>{status, progress}</c> keyed by this same code.
/// <see cref="ItemCode"/> is set only when the objective's <c>items</c> list resolves to exactly one
/// concrete item with no alternates (a delivery-type objective — add-progression-framework-quest-objective-subtasks
/// 4.1); <see cref="Label"/> is the captured display text used when it doesn't (PF's own <c>Title</c>,
/// falling back to <see cref="Code"/> — never null, so a QuestObjective subtask always has something to show).</summary>
internal readonly record struct ScribePfObjectiveDef(string Code, int Required, string? ItemCode, string Label);

/// <summary>One Progression Framework quest's catalog entry, richer than the shared
/// <see cref="ScribeQuestCatalogEntry"/> the picker renders (which carries no PF-specific objective shape —
/// see that type's remarks). <see cref="ScribeQuestWatcher"/> keeps its own by-code lookup of these for
/// progress mirroring; <see cref="ToPickerEntry"/> projects the display-only fields a picker needs.
/// <see cref="IsServerScoped"/> tells the watcher which shared-state source to read for this code
/// (add-progression-framework-server-quest-tracking) — it is not rendered, only consumed by detection.</summary>
internal readonly record struct ScribeProgressionFrameworkQuestEntry(
    string QuestCode, string Title, string? Description, IReadOnlyList<ScribePfObjectiveDef> Objectives,
    bool IsServerScoped)
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
/// <para>Both player- and server-scoped quests are surfaced (add-progression-framework-server-quest-tracking):
/// a server-scoped quest is one shared instance every player contributes to together, with no per-player
/// state of its own — <see cref="ScribeProgressionFrameworkQuestEntry.IsServerScoped"/> records which shared-
/// state source <see cref="ScribeQuestWatcher"/> must read for it (the player's own <c>WatchedAttributes</c>
/// tree for a player-scoped code, a reflected read of the mod's shared server-quest state for a server-scoped
/// one). Progression Framework's own <c>QuestScope</c> enum defaults to Player when a quest's JSON omits
/// <c>"scope"</c> entirely (confirmed against the decompiled <c>Quest</c>/<c>QuestScope</c> source) — real
/// Seafarer data relies on this: most of its quests omit the field rather than writing
/// <c>"scope": "player"</c> explicitly, so this reader treats an absent/blank scope as player-scoped, and
/// only an explicit <c>"server"</c> as server-scoped.</para>
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

    /// <summary>The stable <c>ToggleKeyCombinationCode</c> Progression Framework's <c>GuiDialogLedger</c>
    /// overrides publicly — matches the reflection-free dialog-lookup pattern already used for the
    /// base-game Handbook (<c>VSAPI-NOTES.md</c>): scan <see cref="IGuiAPI.LoadedGuis"/> for the dialog
    /// whose <c>ToggleKeyCombinationCode</c> equals this string, rather than referencing the type.</summary>
    private const string LedgerToggleCode = "progressionframeworkledger";

    /// <summary>Name of <c>GuiDialogLedger</c>'s private <c>currentTab</c> field and the enum member it must
    /// be set to for the click to land on the Quest Log (not Training) tab — verified against the
    /// decompiled source (<c>reference/ProgressionInvestigations/</c>): a private nested
    /// <c>enum LedgerTab { Training, QuestLog }</c> field, defaulting to Training.</summary>
    private const string CurrentTabFieldName = "currentTab";
    private const string QuestLogTabName = "QuestLog";

    /// <summary>Cached once resolved; <see langword="null"/> forever once tab-forcing self-disables
    /// (add-progression-framework-quest-link-open Decision — mirrors <see cref="ScribeQuestWatcher"/>'s
    /// self-disabling reflection posture).</summary>
    private static FieldInfo? currentTabField;
    private static bool tabReflectionDisabled;

    /// <summary>Open Progression Framework's own Quest Log dialog (<c>GuiDialogLedger</c>), landed on its
    /// Quest Log tab, via <see cref="IGuiAPI.LoadedGuis"/> + the dialog's own public <c>TryOpen()</c> — no
    /// reflection for the open itself, matching the base-game Handbook's zero-reflection lookup. Returns
    /// false (no-op) when the dialog isn't registered (Progression Framework not installed, or not yet
    /// loaded), matching the existing "orphaned Quest Link" no-op behavior for a missing target.</summary>
    public static bool TryOpenQuestLog(ICoreClientAPI capi)
    {
        var dialog = capi.Gui.LoadedGuis.FirstOrDefault(d => d?.ToggleKeyCombinationCode == LedgerToggleCode);
        if (dialog is null) return false;

        ForceQuestLogTab(capi, dialog);
        dialog.TryOpen();
        return true;
    }

    /// <summary>Best-effort reflected write of <c>GuiDialogLedger</c>'s private <see cref="CurrentTabFieldName"/>
    /// field to its <see cref="QuestLogTabName"/> enum value, before <see cref="TryOpenQuestLog"/> calls
    /// <c>TryOpen()</c> (which internally recomposes from current state). Wrapped in try/catch: any failure
    /// (field renamed/retyped by a Progression Framework update) permanently disables this step for the
    /// session and is logged once — the plain, zero-reflection open in <see cref="TryOpenQuestLog"/> still
    /// happens regardless, so a reflection failure never regresses to "click does nothing," only to
    /// "opens on whichever tab the player last had."</summary>
    private static void ForceQuestLogTab(ICoreClientAPI capi, GuiDialog dialog)
    {
        if (tabReflectionDisabled) return;
        try
        {
            currentTabField ??= AccessTools.Field(dialog.GetType(), CurrentTabFieldName);
            if (currentTabField is null) { tabReflectionDisabled = true; return; }

            object questLog = Enum.Parse(currentTabField.FieldType, QuestLogTabName);
            currentTabField.SetValue(dialog, questLog);
        }
        catch (Exception ex)
        {
            tabReflectionDisabled = true;
            capi.Logger.Notification(
                "[scribe] Progression Framework Quest Log tab-forcing disabled ({0}) — the Ledger dialog " +
                "still opens, just possibly on the wrong tab.", ex.Message);
        }
    }

    /// <summary>Every player-scoped quest in the installed catalog, titled and sorted for a picker. Empty
    /// (never null/throws) when the mod isn't installed or its catalog fails to parse.</summary>
    public static IReadOnlyList<ScribeProgressionFrameworkQuestEntry> ReadCatalog(ICoreClientAPI capi)
    {
        if (!IsAvailable(capi)) return Array.Empty<ScribeProgressionFrameworkQuestEntry>();
        try
        {
            // One RawQuest object per asset file — matches QuestSystem.LoadQuests's own shape
            // (JToken.Parse(...).ToObject<Quest>() per file), NOT a JSON array of quests per file.
            var byLocation = capi.Assets.GetMany<RawQuest>(capi.Logger, "config/quests", null);
            var result = new List<ScribeProgressionFrameworkQuestEntry>();
            foreach (var (location, q) in byLocation)
            {
                string domain = location.Domain;
                if (string.IsNullOrEmpty(q.Code)) continue;
                bool isServerScoped = string.Equals(q.Scope, ServerScope, StringComparison.OrdinalIgnoreCase);

                string code = PrefixIfBare(q.Code, domain)!;
                string? titleLangKey = PrefixIfBare(q.TitleLangKey, domain);
                string? descLangKey = PrefixIfBare(q.DescriptionLangKey, domain);
                result.Add(new ScribeProgressionFrameworkQuestEntry(
                    code,
                    string.IsNullOrEmpty(titleLangKey) ? code : Lang.Get(titleLangKey),
                    !string.IsNullOrEmpty(descLangKey) && Lang.HasTranslation(descLangKey)
                        ? Lang.Get(descLangKey) : null,
                    BuildObjectives(q),
                    isServerScoped));
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
            .Select(o => new ScribePfObjectiveDef(o.Code!, o.Required, ResolveSingleItemCode(o),
                string.IsNullOrEmpty(o.Title) ? o.Code! : o.Title!))
            .ToList();
    }

    /// <summary>The objective's delivery item code, when its <c>items</c> list resolves to exactly one
    /// concrete item with no alternates (add-progression-framework-quest-objective-subtasks 4.1) — null for a
    /// multi-item/alternates delivery or a non-item objective type (kill/interact/etc.), which then renders
    /// as a captured label instead (see <see cref="ScribePfObjectiveDef.Label"/>).</summary>
    private static string? ResolveSingleItemCode(RawObjective o)
    {
        if (o.Items is not { Count: 1 }) return null;
        var item = o.Items[0];
        if (item.Alternates is { Count: > 0 }) return null;
        return string.IsNullOrEmpty(item.Item) ? null : item.Item;
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

    /// <summary>Mirrors Progression Framework's own <c>Objective</c> JSON shape. <see cref="Type"/>/
    /// <see cref="Pattern"/> describe how PF itself validates delivery/kill/etc. progress and have no bearing
    /// on Scribe's read-only progress mirroring (which reads PF's own already-computed status instead of
    /// re-deriving it) — read but unused here.
    ///
    /// <para><see cref="Items"/> mirrors PF's own <c>QuestItemRequirement</c> shape
    /// (<c>reference/ProgressionInvestigations/progressionframework-decompiled/ProgressionFramework.Quests/
    /// QuestItemRequirement.cs</c>, confirmed by decompile): <c>{ item, quantity, alternates }</c>, a JSON
    /// ARRAY of OBJECTS, not strings. An earlier version of this reader declared no <see cref="Items"/> field
    /// at all after a still-earlier version wrongly typed it as <c>List&lt;string&gt;</c>, which threw for
    /// every real Seafarer delivery objective and silently dropped the whole containing quest file from the
    /// catalog (Newtonsoft can't convert a JSON object into a string) — see
    /// <c>fix-quest-catalog-domain-scoping</c>. This reader is now correctly typed, so delivery objectives'
    /// items resolve for <see cref="ScribePfObjectiveDef.ItemCode"/> without repeating that failure.</para>
    /// <see cref="Title"/> is PF's own raw display text (not a lang key — confirmed against the decompiled
    /// <c>LedgerQuestLogTab.ComposeObjective</c>, which shows it verbatim, falling back to <c>Code</c>), used
    /// as <see cref="ScribePfObjectiveDef.Label"/> for a non-item objective.</summary>
    private sealed class RawObjective
    {
        [JsonProperty("code")]
        public string? Code { get; set; }

        [JsonProperty("title")]
        public string? Title { get; set; }

        [JsonProperty("type")]
        public string? Type { get; set; }

        [JsonProperty("items")]
        public List<RawItemRequirement>? Items { get; set; }

        [JsonProperty("required")]
        public int Required { get; set; }

        [JsonProperty("pattern")]
        public string? Pattern { get; set; }
    }

    /// <summary>Mirrors Progression Framework's own <c>QuestItemRequirement</c> shape exactly (see
    /// <see cref="RawObjective.Items"/>'s remarks). <see cref="Quantity"/> is read but unused — Scribe shows
    /// the OBJECTIVE's own <c>required</c> count (already the delivery target Progression Framework itself
    /// enforces), not a second per-item quantity.</summary>
    private sealed class RawItemRequirement
    {
        [JsonProperty("item")]
        public string? Item { get; set; }

        [JsonProperty("quantity")]
        public int Quantity { get; set; }

        [JsonProperty("alternates")]
        public List<string>? Alternates { get; set; }
    }
}
