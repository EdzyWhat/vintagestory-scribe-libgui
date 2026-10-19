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

/// <summary>One objective of a Progression Framework quest: a stable <see cref="Code"/> (matched against the
/// player's <c>WatchedAttributes</c> tree, never by position) and the count required. <see cref="ItemCode"/>
/// is set only when the <c>items</c> list resolves to exactly one concrete item with no alternates;
/// otherwise <see cref="Label"/> (PF's <c>Title</c>, falling back to <see cref="Code"/>, never null) is shown.</summary>
internal readonly record struct ScribePfObjectiveDef(string Code, int Required, string? ItemCode, string Label);

/// <summary>One PF quest's catalog entry, richer than the picker's <see cref="ScribeQuestCatalogEntry"/>;
/// <see cref="ToPickerEntry"/> projects the display fields. <see cref="IsServerScoped"/> tells
/// <see cref="ScribeQuestWatcher"/> which shared-state source to read; it is not rendered.</summary>
internal readonly record struct ScribeProgressionFrameworkQuestEntry(
    string QuestCode, string Title, string? Description, IReadOnlyList<ScribePfObjectiveDef> Objectives,
    bool IsServerScoped)
{
    public ScribeQuestCatalogEntry ToPickerEntry() => new(
        ScribeQuestSource.ProgressionFramework, QuestCode, Title, Description,
        Array.Empty<ScribeQuestObjectiveDef>());
}

/// <summary>
/// Reads the installed Progression Framework mod's static <c>config/quests/*.json</c> catalog, the second
/// Quest Link backend. Like <see cref="ScribeQuestCatalog"/> it is a soft dependency with no reflection or
/// compiled reference (gated by <see cref="IsAvailable"/>), but it reads PF's own JSON schema.
///
/// <para>A server-scoped quest is one shared instance; <see cref="ScribeProgressionFrameworkQuestEntry.IsServerScoped"/>
/// tells <see cref="ScribeQuestWatcher"/> which state source to read. PF defaults an omitted <c>"scope"</c> to
/// Player (real Seafarer data relies on this), so an absent/blank scope is player-scoped and only an explicit
/// <c>"server"</c> is server-scoped.</para>
///
/// <para>Titles/descriptions come from each quest's own lang keys. The quest <c>code</c> and lang keys may be
/// BARE; PF's loader prefixes them with the ASSET'S OWN domain, and this reader replicates that exactly so a
/// Link's stored code matches the player's <c>WatchedAttributes</c> tree verbatim.</para>
/// </summary>
internal static class ScribeProgressionFrameworkQuestCatalog
{
    public const string ModId = "progressionframework";
    private const string ServerScope = "server";

    /// <summary>Whether the Quest Link option should offer Progression Framework entries at all.</summary>
    public static bool IsAvailable(ICoreClientAPI capi) => capi.ModLoader.IsModEnabled(ModId);

    /// <summary>The public <c>ToggleKeyCombinationCode</c> of PF's <c>GuiDialogLedger</c>; the dialog is found
    /// by scanning <see cref="IGuiAPI.LoadedGuis"/> for it rather than referencing the type.</summary>
    private const string LedgerToggleCode = "progressionframeworkledger";

    /// <summary><c>GuiDialogLedger</c>'s private <c>currentTab</c> field (a nested <c>enum LedgerTab
    /// { Training, QuestLog }</c>, default Training) and the member to set so the dialog opens on the Quest Log.</summary>
    private const string CurrentTabFieldName = "currentTab";
    private const string QuestLogTabName = "QuestLog";

    /// <summary>Cached once resolved; tab-forcing self-disables permanently on failure.</summary>
    private static FieldInfo? currentTabField;
    private static bool tabReflectionDisabled;

    /// <summary>Opens PF's Quest Log dialog on its Quest Log tab (no reflection for the open itself). Returns
    /// false (no-op) when the dialog isn't registered.</summary>
    public static bool TryOpenQuestLog(ICoreClientAPI capi)
    {
        var dialog = capi.Gui.LoadedGuis.FirstOrDefault(d => d?.ToggleKeyCombinationCode == LedgerToggleCode);
        if (dialog is null) return false;

        ForceQuestLogTab(capi, dialog);
        dialog.TryOpen();
        return true;
    }

    /// <summary>Best-effort reflected write of <see cref="CurrentTabFieldName"/> to <see cref="QuestLogTabName"/>
    /// before <c>TryOpen()</c> recomposes. Any failure (field renamed by a PF update) disables this step for the
    /// session and is logged once; the dialog still opens, possibly on the wrong tab.</summary>
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

    /// <summary>Every quest in the installed catalog, titled and sorted for a picker. Empty (never null/throws)
    /// when the mod isn't installed or its catalog fails to parse.</summary>
    public static IReadOnlyList<ScribeProgressionFrameworkQuestEntry> ReadCatalog(ICoreClientAPI capi)
    {
        if (!IsAvailable(capi)) return Array.Empty<ScribeProgressionFrameworkQuestEntry>();
        try
        {
            // One RawQuest object per asset file, NOT an array per file (matches PF's own loader).
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

    /// <summary>Mirrors PF's <c>QuestSystem.PrefixIfBare</c>: a key containing <c>:</c> is untouched; a bare one
    /// gets the asset's own <paramref name="domain"/> (not necessarily <see cref="ModId"/>).</summary>
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

    /// <summary>The delivery item code when <c>items</c> is exactly one item with no alternates; otherwise null
    /// (the objective renders as <see cref="ScribePfObjectiveDef.Label"/>).</summary>
    private static string? ResolveSingleItemCode(RawObjective o)
    {
        if (o.Items is not { Count: 1 }) return null;
        var item = o.Items[0];
        if (item.Alternates is { Count: > 0 }) return null;
        return string.IsNullOrEmpty(item.Item) ? null : item.Item;
    }

    /// <summary>"N of M objectives complete" for a multi-objective quest, the bare status word for a
    /// single-objective one; null when there's nothing to show.</summary>
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

        // "completed" is the status string PF's objective tree writes (it seeds "pending").
        int complete = objectives.Count(o =>
            statusByCode.TryGetValue(o.Code, out string? status)
            && string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase));
        return Lang.Get("scribe:scribe-questobjective-pf-aggregate", complete, objectives.Count);
    }

    /// <summary>PF's <c>Quest</c> JSON shape, without referencing that mod's assembly.</summary>
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

    /// <summary>PF's <c>Objective</c> JSON shape. <see cref="Type"/>/<see cref="Pattern"/> are read but unused
    /// (Scribe mirrors PF's already-computed status).
    ///
    /// <para><see cref="Items"/> is a JSON array of OBJECTS (<c>{ item, quantity, alternates }</c>), not strings;
    /// typing it as <c>List&lt;string&gt;</c> would throw and silently drop every quest file with a delivery
    /// objective from the catalog.</para>
    /// <see cref="Title"/> is PF's raw display text (not a lang key), used as
    /// <see cref="ScribePfObjectiveDef.Label"/> for a non-item objective.</summary>
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

    /// <summary>PF's <c>QuestItemRequirement</c> shape (see <see cref="RawObjective.Items"/>).
    /// <see cref="Quantity"/> is unused: Scribe shows the objective's own <c>required</c> count.</summary>
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
