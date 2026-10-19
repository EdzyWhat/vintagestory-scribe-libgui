using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Scribe;

/// <summary>
/// Client-only soft auto-detect of quest accept/completion, with no reflection for the detection itself:
/// <c>VsQuest.QuestSystem</c> stamps <c>"lastaccepted-{questId}[-{playerUid}]"</c> and
/// <c>"playercompleted-{playerUid}"</c> onto the quest-giver ENTITY's synced <c>WatchedAttributes</c>, and
/// every catalog quest id is checked against those keys on every loaded "questgiver" entity.
///
/// <para>Only the richer kill/block-place/block-break progress has no such equivalent (vsquest keeps live
/// counts inside the open <c>VsQuest.QuestSelectGui</c>'s <c>activeQuests</c>), so that enrichment alone uses
/// reflection, with permanent self-disable on first failure; accept/complete detection is unaffected.</para>
///
/// <para><see cref="_acceptedSeen"/>/<see cref="_completedSeen"/> only suppress repeat notifications for the
/// client session. Duplicate auto-created Quest Links are prevented server-side
/// (<c>ScribeModSystem.Quest.cs</c>), so a re-notification on rejoin is harmless.</para>
///
/// <para><b>Progression Framework</b> is a second, independently fail-closed tick path: it reads PF's synced
/// <c>WatchedAttributes</c> tree (<see cref="PfPlayerQuestTreeKey"/>) off the player's own entity, with no
/// entity scan and no reflection. A failure disables PF detection only, never vsquest's.</para>
///
/// <para><b>PF server-scoped quests</b> are a third, independently fail-closed path. Their shared state has no
/// <c>WatchedAttributes</c> equivalent: PF syncs it into a private field
/// (<c>QuestSystem.clientServerQuestState</c>) with no accessor, so that ONE field is read via reflection
/// (cached); everything read off its values is a normal tree call. A failure (missing type/field, unexpected
/// type after a PF update) disables only server-scoped detection.</para>
/// </summary>
internal sealed class ScribeQuestWatcher
{
    private const int TickIntervalMs = 1000;

    private readonly ICoreClientAPI capi;
    private readonly Action<ScribeQuestCatalogEntry, bool> onAccepted;
    private readonly Action<ScribeQuestCatalogEntry> onCompleted;

    private long tickListenerId;
    private IReadOnlyList<ScribeQuestCatalogEntry>? catalog;
    private Dictionary<string, ScribeQuestCatalogEntry>? catalogByCode;

    private readonly HashSet<string> _acceptedSeen = new(StringComparer.Ordinal);
    private readonly HashSet<string> _completedSeen = new(StringComparer.Ordinal);

    /// <summary>Per-quest last-observed <c>lastaccepted-*</c> value (the in-game DAY of acceptance). A value
    /// that grows mid-session (not the first read, which only means "accepted before login") signals a fresh
    /// re-accept of a repeatable quest.</summary>
    private readonly Dictionary<string, double> _acceptedFingerprint = new(StringComparer.Ordinal);

    private const string QuestGuiTypeName = "VsQuest.QuestSelectGui";
    private bool dialogReflectionDisabled;
    private readonly Dictionary<string, int[]> liveProgress = new(StringComparer.Ordinal);

    // ---- Progression Framework (player-scoped) ----
    private const string PfPlayerQuestTreeKey = "progressionframework:questlog";
    // PF's own status strings ("completed", not "complete").
    private const string PfStatusActive = "active";
    private const string PfStatusComplete = "completed";
    private bool pfDetectionDisabled;
    private IReadOnlyList<ScribeProgressionFrameworkQuestEntry>? pfCatalog;
    private Dictionary<string, ScribeProgressionFrameworkQuestEntry>? pfCatalogByCode;
    private readonly HashSet<string> pfAcceptedSeen = new(StringComparer.Ordinal);
    private readonly HashSet<string> pfCompletedSeen = new(StringComparer.Ordinal);
    /// <summary>Per-quest last-observed PF "status", the PF analogue of <see cref="_acceptedFingerprint"/>: a
    /// mid-session flip away from then back to "active" signals a fresh re-accept.</summary>
    private readonly Dictionary<string, string> pfStatusFingerprint = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> pfObjectiveStatus = new(StringComparer.Ordinal);
    // Parallel to pfObjectiveStatus: PF's numeric "progress" per objective, kept separate so a bad value
    // affects only the live count, never accept/complete detection.
    private readonly Dictionary<string, Dictionary<string, int>> pfObjectiveProgress = new(StringComparer.Ordinal);

    // ---- Progression Framework (server-scoped, reflected) ----
    private const string PfQuestSystemTypeName = "ProgressionFramework.Quests.QuestSystem";
    private const string PfClientServerQuestStateFieldName = "clientServerQuestState";
    private bool serverQuestDetectionDisabled;
    private ModSystem? pfServerQuestSystem;
    private FieldInfo? pfServerQuestStateField;

    public ScribeQuestWatcher(
        ICoreClientAPI capi, Action<ScribeQuestCatalogEntry, bool> onAccepted, Action<ScribeQuestCatalogEntry> onCompleted)
    {
        this.capi = capi;
        this.onAccepted = onAccepted;
        this.onCompleted = onCompleted;
        tickListenerId = capi.Event.RegisterGameTickListener(OnTick, TickIntervalMs);
    }

    public void Dispose()
    {
        if (tickListenerId != 0)
        {
            capi.Event.UnregisterGameTickListener(tickListenerId);
            tickListenerId = 0;
        }
    }

    /// <summary>Live counts, positionally aligned with the catalog <see cref="ScribeQuestCatalogEntry.Objectives"/>.
    /// False until the quest's dialog has been read this session, or when vsquest isn't installed.</summary>
    public bool TryGetLiveProgress(string questCode, out IReadOnlyList<int> counts)
    {
        if (liveProgress.TryGetValue(questCode, out var arr)) { counts = arr; return true; }
        counts = Array.Empty<int>();
        return false;
    }

    /// <summary>Static objectives for a cataloged quest, aligned with <see cref="TryGetLiveProgress"/>. False
    /// before the first catalog read or for an uncataloged code. VS Quest only (see
    /// <see cref="TryGetPfObjectiveStatus"/> for PF).</summary>
    public bool TryGetObjectives(string questCode, out IReadOnlyList<ScribeQuestObjectiveDef> objectives)
    {
        if (catalogByCode != null && catalogByCode.TryGetValue(questCode, out var q)) { objectives = q.Objectives; return true; }
        objectives = Array.Empty<ScribeQuestObjectiveDef>();
        return false;
    }

    /// <summary>PF's catalog objectives (code + required count), matched by code against
    /// <see cref="TryGetPfObjectiveStatus"/>. False before the PF catalog is read, without PF, or for an
    /// uncataloged code.</summary>
    public bool TryGetPfObjectiveDefs(string questCode, out IReadOnlyList<ScribePfObjectiveDef> objectives)
    {
        if (pfCatalogByCode != null && pfCatalogByCode.TryGetValue(questCode, out var q)) { objectives = q.Objectives; return true; }
        objectives = Array.Empty<ScribePfObjectiveDef>();
        return false;
    }

    /// <summary>PF's live per-objective status, keyed by objective code. False until the quest's tree has been
    /// read this session, or when PF is absent or detection self-disabled.</summary>
    public bool TryGetPfObjectiveStatus(string questCode, out IReadOnlyDictionary<string, string> statusByCode)
    {
        if (pfObjectiveStatus.TryGetValue(questCode, out var map)) { statusByCode = map; return true; }
        statusByCode = EmptyStatusMap;
        return false;
    }

    /// <summary>PF's live per-objective NUMERIC progress by objective code; drives a QuestObjective subtask's
    /// <c>CurrentQuantity</c>. False under the same conditions as <see cref="TryGetPfObjectiveStatus"/>.</summary>
    public bool TryGetPfObjectiveProgress(string questCode, out IReadOnlyDictionary<string, int> progressByCode)
    {
        if (pfObjectiveProgress.TryGetValue(questCode, out var map)) { progressByCode = map; return true; }
        progressByCode = EmptyProgressMap;
        return false;
    }

    private static readonly Dictionary<string, string> EmptyStatusMap = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> EmptyProgressMap = new(StringComparer.Ordinal);

    /// <summary>Whether the player has been observed to start (accept or complete) the quest this session, read
    /// from the session-cached detection sets. vsquest requires the giver to have been scanned
    /// (<see cref="ScanGiver"/>); for PF any recorded status counts (<see cref="pfStatusFingerprint"/>). Only
    /// filters the "Add Quest Link" picker.</summary>
    public bool HasStarted(string questCode)
        => _acceptedSeen.Contains(questCode)
            || _completedSeen.Contains(questCode)
            || pfStatusFingerprint.ContainsKey(questCode);

    private void OnTick(float dt)
    {
        if (ScribeQuestCatalog.IsAvailable(capi))
        {
            catalog ??= ScribeQuestCatalog.ReadCatalog(capi);
            if (catalog.Count > 0)
            {
                catalogByCode ??= catalog.ToDictionary(e => e.QuestCode, StringComparer.Ordinal);

                string? uid = capi.World.Player?.PlayerUID;
                if (!string.IsNullOrEmpty(uid))
                {
                    foreach (var entity in capi.World.LoadedEntities.Values)
                    {
                        if (entity is not { Alive: true }) continue;
                        if (entity.GetBehavior("questgiver") is null) continue;
                        ScanGiver(entity, uid);
                    }
                }

                ReadDialogProgress();
            }
        }

        ScanProgressionFramework();
        ScanProgressionFrameworkServerQuests();
    }

    /// <summary>Checks every catalog quest against one giver's synced <c>WatchedAttributes</c> for a fresh
    /// accept or completion (key shapes in the class doc).</summary>
    private void ScanGiver(Entity giver, string uid)
    {
        var wa = giver.WatchedAttributes;
        if (wa is null) return;

        foreach (var q in catalog!)
        {
            string perPlayerKey = "lastaccepted-" + q.QuestCode + "-" + uid;
            string sharedKey = "lastaccepted-" + q.QuestCode;
            bool hasPerPlayer = wa.HasAttribute(perPlayerKey);
            bool hasShared = !hasPerPlayer && wa.HasAttribute(sharedKey);
            if (!hasPerPlayer && !hasShared) continue;

            double value = wa.GetDouble(hasPerPlayer ? perPlayerKey : sharedKey);
            bool freshTransition = false;
            if (_acceptedFingerprint.TryGetValue(q.QuestCode, out double prevValue) && value > prevValue)
            {
                freshTransition = true;
                _acceptedSeen.Remove(q.QuestCode); // force a refire below
            }
            _acceptedFingerprint[q.QuestCode] = value;

            if (_acceptedSeen.Add(q.QuestCode))
            {
                onAccepted(q, freshTransition);
            }
        }

        var completedIds = wa.GetStringArray("playercompleted-" + uid);
        if (completedIds is null) return;
        foreach (string id in completedIds)
        {
            if (_completedSeen.Contains(id)) continue;
            if (!catalogByCode!.TryGetValue(id, out var q)) continue; // a quest pack we don't have cataloged
            _completedSeen.Add(id);
            onCompleted(q);
        }
    }

    /// <summary>PF player-scoped detection: reads <see cref="PfPlayerQuestTreeKey"/> off the player's entity.
    /// Any failure (e.g. malformed tree after a PF update) disables only this path for the session.</summary>
    private void ScanProgressionFramework()
    {
        if (pfDetectionDisabled) return;
        if (!ScribeProgressionFrameworkQuestCatalog.IsAvailable(capi)) return;

        try
        {
            pfCatalog ??= ScribeProgressionFrameworkQuestCatalog.ReadCatalog(capi);
            if (pfCatalog.Count == 0) return;
            pfCatalogByCode ??= pfCatalog.ToDictionary(e => e.QuestCode, StringComparer.Ordinal);

            var tree = capi.World.Player?.Entity?.WatchedAttributes?.GetTreeAttribute(PfPlayerQuestTreeKey);
            if (tree is null) return;

            foreach (var questEntry in tree)
            {
                string questCode = questEntry.Key;
                if (questEntry.Value is not ITreeAttribute questTree) continue;
                if (!pfCatalogByCode.TryGetValue(questCode, out var def)) continue; // a quest pack we don't have cataloged

                string status = questTree.GetString("status") ?? "";
                bool freshTransition = false;
                if (pfStatusFingerprint.TryGetValue(questCode, out var prevStatus)
                    && !string.Equals(prevStatus, PfStatusActive, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(status, PfStatusActive, StringComparison.OrdinalIgnoreCase))
                {
                    freshTransition = true;
                    pfAcceptedSeen.Remove(questCode); // force a refire below
                }
                pfStatusFingerprint[questCode] = status;

                if (string.Equals(status, PfStatusActive, StringComparison.OrdinalIgnoreCase)
                    && pfAcceptedSeen.Add(questCode))
                {
                    onAccepted(def.ToPickerEntry(), freshTransition);
                }
                else if (string.Equals(status, PfStatusComplete, StringComparison.OrdinalIgnoreCase)
                    && pfCompletedSeen.Add(questCode))
                {
                    onCompleted(def.ToPickerEntry());
                }

                if (questTree.GetTreeAttribute("objectives") is not ITreeAttribute objectivesTree) continue;
                var statusByCode = pfObjectiveStatus.TryGetValue(questCode, out var existing)
                    ? existing
                    : pfObjectiveStatus[questCode] = new Dictionary<string, string>(StringComparer.Ordinal);
                var progressByCode = pfObjectiveProgress.TryGetValue(questCode, out var existingProgress)
                    ? existingProgress
                    : pfObjectiveProgress[questCode] = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var objEntry in objectivesTree)
                {
                    if (objEntry.Value is not ITreeAttribute objTree) continue;
                    statusByCode[objEntry.Key] = objTree.GetString("status") ?? "";
                    // A missing "progress" key keeps the cached value rather than overwriting with 0.
                    if (objTree.HasAttribute("progress"))
                        progressByCode[objEntry.Key] = objTree.GetInt("progress", 0);
                }
            }
        }
        catch (Exception ex)
        {
            pfDetectionDisabled = true;
            capi.Logger.Notification(
                "[scribe] Progression Framework quest detection disabled ({0}) — VS Quest detection is unaffected.",
                ex.Message);
        }
    }

    /// <summary>PF server-scoped detection (see class doc): reads the shared state via one reflected field, then
    /// walks it like <see cref="ScanProgressionFramework"/> using the same catalog, seen-sets, status
    /// dictionaries and callbacks, so consumers can't tell the scopes apart. Any failure disables only this path.</summary>
    private void ScanProgressionFrameworkServerQuests()
    {
        if (serverQuestDetectionDisabled) return;
        if (!ScribeProgressionFrameworkQuestCatalog.IsAvailable(capi)) return;

        try
        {
            pfCatalog ??= ScribeProgressionFrameworkQuestCatalog.ReadCatalog(capi);
            if (pfCatalog.Count == 0) return;
            pfCatalogByCode ??= pfCatalog.ToDictionary(e => e.QuestCode, StringComparer.Ordinal);

            pfServerQuestSystem ??= capi.ModLoader.GetModSystem(PfQuestSystemTypeName);
            if (pfServerQuestSystem is null) return;

            pfServerQuestStateField ??= AccessTools.Field(pfServerQuestSystem.GetType(), PfClientServerQuestStateFieldName);
            if (pfServerQuestStateField?.GetValue(pfServerQuestSystem) is not Dictionary<string, TreeAttribute> stateByCode) return;

            foreach (var (questCode, questTree) in stateByCode)
            {
                if (!pfCatalogByCode.TryGetValue(questCode, out var def) || !def.IsServerScoped) continue;

                string status = questTree.GetString("status") ?? "";
                bool freshTransition = false;
                if (pfStatusFingerprint.TryGetValue(questCode, out var prevStatus)
                    && !string.Equals(prevStatus, PfStatusActive, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(status, PfStatusActive, StringComparison.OrdinalIgnoreCase))
                {
                    freshTransition = true;
                    pfAcceptedSeen.Remove(questCode); // force a refire below
                }
                pfStatusFingerprint[questCode] = status;

                if (string.Equals(status, PfStatusActive, StringComparison.OrdinalIgnoreCase)
                    && pfAcceptedSeen.Add(questCode))
                {
                    onAccepted(def.ToPickerEntry(), freshTransition);
                }
                else if (string.Equals(status, PfStatusComplete, StringComparison.OrdinalIgnoreCase)
                    && pfCompletedSeen.Add(questCode))
                {
                    onCompleted(def.ToPickerEntry());
                }

                if (questTree.GetTreeAttribute("objectives") is not ITreeAttribute objectivesTree) continue;
                var statusByObjCode = pfObjectiveStatus.TryGetValue(questCode, out var existing)
                    ? existing
                    : pfObjectiveStatus[questCode] = new Dictionary<string, string>(StringComparer.Ordinal);
                var progressByObjCode = pfObjectiveProgress.TryGetValue(questCode, out var existingProgress)
                    ? existingProgress
                    : pfObjectiveProgress[questCode] = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var objEntry in objectivesTree)
                {
                    if (objEntry.Value is not ITreeAttribute objTree) continue;
                    statusByObjCode[objEntry.Key] = objTree.GetString("status") ?? "";
                    if (objTree.HasAttribute("progress"))
                        progressByObjCode[objEntry.Key] = objTree.GetInt("progress", 0);
                }
            }
        }
        catch (Exception ex)
        {
            serverQuestDetectionDisabled = true;
            capi.Logger.Notification(
                "[scribe] Progression Framework server-scoped quest detection disabled ({0}) — player-scoped detection is unaffected.",
                ex.Message);
        }
    }

    /// <summary>Best-effort refresh of <see cref="liveProgress"/> from the open quest dialog (found by type name),
    /// reading <c>activeQuests</c> and each quest's three tracker lists via <see cref="AccessTools"/>. Any
    /// exception permanently disables this half only.</summary>
    private void ReadDialogProgress()
    {
        if (dialogReflectionDisabled) return;
        try
        {
            var dialog = FindQuestDialog();
            if (dialog is null) return;

            var dialogType = dialog.GetType();
            if (AccessTools.Field(dialogType, "activeQuests")?.GetValue(dialog) is not IEnumerable activeQuests) return;

            foreach (var active in activeQuests)
            {
                if (active is null) continue;
                var activeType = active.GetType();
                if (AccessTools.Property(activeType, "questId")?.GetValue(active) is not string questId) continue;

                var counts = new List<int>();
                foreach (string trackerListName in TrackerListNames)
                {
                    if (AccessTools.Property(activeType, trackerListName)?.GetValue(active) is not IEnumerable trackers)
                        continue;
                    foreach (var tracker in trackers)
                    {
                        object? count = tracker is null ? null : AccessTools.Property(tracker.GetType(), "count")?.GetValue(tracker);
                        counts.Add(count is null ? 0 : Convert.ToInt32(count));
                    }
                }
                liveProgress[questId] = counts.ToArray();
            }
        }
        catch (Exception ex)
        {
            dialogReflectionDisabled = true;
            capi.Logger.Notification(
                "[scribe] quest progress mirroring disabled ({0}) — accept/completion detection is unaffected.",
                ex.Message);
        }
    }

    private static readonly string[] TrackerListNames = { "killTrackers", "blockPlaceTrackers", "blockBreakTrackers" };

    private GuiDialog? FindQuestDialog()
    {
        foreach (var dialog in capi.Gui.OpenedGuis)
            if (dialog is not null && dialog.GetType().FullName == QuestGuiTypeName && dialog.IsOpened())
                return dialog;
        foreach (var dialog in capi.Gui.LoadedGuis)
            if (dialog is not null && dialog.GetType().FullName == QuestGuiTypeName && dialog.IsOpened())
                return dialog;
        return null;
    }
}
