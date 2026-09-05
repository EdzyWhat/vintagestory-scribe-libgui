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
/// Client-only "soft auto-detect" (design.md Decision 10, Layer 2 — add-assignment-and-quest-support
/// §11). Watches for quest accept/completion using vsquest's own SERVER-SYNCED state rather than any
/// reflection: <c>VsQuest.QuestSystem</c> stamps <c>"lastaccepted-{questId}[-{playerUid}]"</c> and
/// <c>"playercompleted-{playerUid}"</c> onto the quest-giver ENTITY's <c>WatchedAttributes</c> (confirmed
/// in <c>vsquest-src/Systems/QuestSystem.cs</c> and <c>Entity/Behavior/BehaviorQuestGiver.cs</c>) — a
/// normal, vanilla-API-visible <see cref="Vintagestory.API.Datastructures.ITreeAttribute"/> that syncs to
/// every client with that entity loaded, no dialog or reflection required. Every catalog quest id is
/// checked directly against those two keys on every loaded "questgiver"-behavior entity, which is why
/// this needs no Harmony/AccessTools at all for accept/complete detection.
///
/// <para>Only the RICHER kill/block-place/block-break progress mirroring (§11.3) has no entity-attribute
/// equivalent — vsquest keeps live tracker counts only inside the open <c>VsQuest.QuestSelectGui</c>'s
/// <c>activeQuests</c> field. That best-effort enrichment alone uses the Tallybook-proven reflection
/// route (<see cref="AccessTools.Field"/>/<see cref="AccessTools.Property"/> against the dialog by type
/// name), wrapped in try/catch with permanent self-disable on first failure — accept/complete detection
/// above is entirely unaffected if this half breaks.</para>
///
/// <para>Session-only bookkeeping (<see cref="_acceptedSeen"/>/<see cref="_completedSeen"/>): this class
/// only suppresses repeat notifications for the life of the client session, not across relogs. It is NOT
/// the mechanism that prevents a duplicate auto-created Quest Link — that dedup happens server-side
/// (<c>ScribeModSystem.Quest.cs</c> checks the target document for an existing Link to the same quest
/// code before appending), so a re-notification on rejoin is a harmless, disclosed limitation rather than
/// a correctness bug.</para>
///
/// <para><b>Progression Framework (add-progression-framework-quest-support §3)</b>: a second,
/// independently-gated tick path detects accept/complete/per-objective progress for player-scoped quests
/// by reading Progression Framework's own server-synced <c>WatchedAttributes</c> tree
/// (<see cref="PfPlayerQuestTreeKey"/>, a public const on that mod's <c>QuestSystem</c>) directly off
/// <c>capi.World.Player.Entity</c> — no entity scan needed (the tree is keyed on the player's own entity,
/// not an NPC's) and no reflection at all, unlike vsquest's progress-mirroring half above. Gated and
/// fail-closed independently of the vsquest path (§3.5): a failure here disables PF detection only, for
/// the session, and never touches vsquest's own accept/complete/progress state.</para>
///
/// <para><b>Progression Framework server-scoped quests (add-progression-framework-server-quest-tracking)</b>:
/// a THIRD, independently-gated tick path detects the same accept/complete/per-objective signals for
/// server-scoped quests — one shared quest instance every player on the world contributes to together, with
/// no per-player state of its own. Unlike the player-scoped tree above, this shared state has no
/// <c>WatchedAttributes</c> equivalent to read: Progression Framework syncs it to clients over its own
/// private network channel into a private field (<c>QuestSystem.clientServerQuestState</c>) with no public
/// accessor. This path reads that ONE field via reflection (<see cref="AccessTools.Field"/>, cached after
/// first success) — the only reflective hop; everything read off the resulting dictionary's values
/// (<see cref="Vintagestory.API.Datastructures.TreeAttribute"/>, a real vanilla-API type) is a normal,
/// non-reflective call, exactly like every other tree read in this class. Fail-closed independently of both
/// paths above: a failure here (missing type, missing field, unexpected field type — e.g. after a
/// Progression Framework update) permanently disables only server-scoped detection for the session.</para>
/// </summary>
internal sealed class ScribeQuestWatcher
{
    private const int TickIntervalMs = 1000;

    private readonly ICoreClientAPI capi;
    private readonly Action<ScribeQuestCatalogEntry> onAccepted;
    private readonly Action<ScribeQuestCatalogEntry> onCompleted;

    private long tickListenerId;
    private IReadOnlyList<ScribeQuestCatalogEntry>? catalog;
    private Dictionary<string, ScribeQuestCatalogEntry>? catalogByCode;

    private readonly HashSet<string> _acceptedSeen = new(StringComparer.Ordinal);
    private readonly HashSet<string> _completedSeen = new(StringComparer.Ordinal);

    // ---- best-effort dialog reflection (progress mirroring only) ----
    private const string QuestGuiTypeName = "VsQuest.QuestSelectGui";
    private bool dialogReflectionDisabled;
    private readonly Dictionary<string, int[]> liveProgress = new(StringComparer.Ordinal);

    // ---- Progression Framework (player-scoped quests, own WatchedAttributes tree) ----
    private const string PfPlayerQuestTreeKey = "progressionframework:questlog";
    // Matches QuestSystem's own status strings exactly (decompiled source, confirmed 2026-09-02):
    // AcceptQuest writes "active"; CompleteQuest/CompleteObjective write "completed" (not "complete").
    private const string PfStatusActive = "active";
    private const string PfStatusComplete = "completed";
    private bool pfDetectionDisabled;
    private IReadOnlyList<ScribeProgressionFrameworkQuestEntry>? pfCatalog;
    private Dictionary<string, ScribeProgressionFrameworkQuestEntry>? pfCatalogByCode;
    private readonly HashSet<string> pfAcceptedSeen = new(StringComparer.Ordinal);
    private readonly HashSet<string> pfCompletedSeen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> pfObjectiveStatus = new(StringComparer.Ordinal);
    // Parallel to pfObjectiveStatus (same questCode -> objCode keying), added for
    // add-progression-framework-quest-objective-subtasks: PF's numeric "progress" int per objective, read
    // alongside "status" but never conflated with it — a malformed/missing progress value must disable only
    // this objective's live count (quest-auto-detect's fail-safe scenario), never accept/complete detection.
    private readonly Dictionary<string, Dictionary<string, int>> pfObjectiveProgress = new(StringComparer.Ordinal);

    // ---- Progression Framework server-scoped quests (reflected shared state, no WatchedAttributes) ----
    private const string PfQuestSystemTypeName = "ProgressionFramework.Quests.QuestSystem";
    private const string PfClientServerQuestStateFieldName = "clientServerQuestState";
    private bool serverQuestDetectionDisabled;
    private ModSystem? pfServerQuestSystem;
    private FieldInfo? pfServerQuestStateField;

    public ScribeQuestWatcher(
        ICoreClientAPI capi, Action<ScribeQuestCatalogEntry> onAccepted, Action<ScribeQuestCatalogEntry> onCompleted)
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

    /// <summary>Live kill/block-place/block-break progress for a quest, positionally zipped against its
    /// catalog <see cref="ScribeQuestCatalogEntry.Objectives"/> — index <c>i</c> here is objective
    /// <c>i</c> there. Only populated while (or after) the quest's own dialog has been read this session;
    /// returns false (no entry) otherwise, including whenever vsquest isn't installed.</summary>
    public bool TryGetLiveProgress(string questCode, out IReadOnlyList<int> counts)
    {
        if (liveProgress.TryGetValue(questCode, out var arr)) { counts = arr; return true; }
        counts = Array.Empty<int>();
        return false;
    }

    /// <summary>The static objective definitions for a cataloged quest, positionally aligned with
    /// <see cref="TryGetLiveProgress"/>'s counts (§11.3). False before the catalog has been read once
    /// (the first tick after login) or for an uncataloged quest code. VS Quest only — see
    /// <see cref="TryGetPfObjectiveStatus"/> for Progression Framework's equivalent.</summary>
    public bool TryGetObjectives(string questCode, out IReadOnlyList<ScribeQuestObjectiveDef> objectives)
    {
        if (catalogByCode != null && catalogByCode.TryGetValue(questCode, out var q)) { objectives = q.Objectives; return true; }
        objectives = Array.Empty<ScribeQuestObjectiveDef>();
        return false;
    }

    /// <summary>Progression Framework's catalog objective definitions (code + required count) for a
    /// cataloged quest (Decision 4 — matched by code, not position, against
    /// <see cref="TryGetPfObjectiveStatus"/>). False before the PF catalog has been read once, PF isn't
    /// installed, or the quest code isn't cataloged.</summary>
    public bool TryGetPfObjectiveDefs(string questCode, out IReadOnlyList<ScribePfObjectiveDef> objectives)
    {
        if (pfCatalogByCode != null && pfCatalogByCode.TryGetValue(questCode, out var q)) { objectives = q.Objectives; return true; }
        objectives = Array.Empty<ScribePfObjectiveDef>();
        return false;
    }

    /// <summary>Progression Framework's live per-objective status, keyed by each objective's own stable
    /// code (Decision 4 — no positional zip needed, unlike vsquest, since PF's own attribute tree already
    /// correlates status to a code). False until this quest's tree has been read at least once this
    /// session, including whenever Progression Framework isn't installed or detection has self-disabled.</summary>
    public bool TryGetPfObjectiveStatus(string questCode, out IReadOnlyDictionary<string, string> statusByCode)
    {
        if (pfObjectiveStatus.TryGetValue(questCode, out var map)) { statusByCode = map; return true; }
        statusByCode = EmptyStatusMap;
        return false;
    }

    /// <summary>Progression Framework's live per-objective NUMERIC progress, keyed by each objective's own
    /// stable code — the count add-progression-framework-quest-objective-subtasks drives a QuestObjective
    /// subtask's <c>CurrentQuantity</c> from. False until this quest's tree has been read at least once this
    /// session, including whenever Progression Framework isn't installed or detection has self-disabled.</summary>
    public bool TryGetPfObjectiveProgress(string questCode, out IReadOnlyDictionary<string, int> progressByCode)
    {
        if (pfObjectiveProgress.TryGetValue(questCode, out var map)) { progressByCode = map; return true; }
        progressByCode = EmptyProgressMap;
        return false;
    }

    private static readonly Dictionary<string, string> EmptyStatusMap = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> EmptyProgressMap = new(StringComparer.Ordinal);

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

    /// <summary>Checks every catalog quest against one quest-giver entity's synced
    /// <c>WatchedAttributes</c> for a fresh accept or completion (see class doc-comment for the exact key
    /// shapes). Pure vanilla API — no reflection.</summary>
    private void ScanGiver(Entity giver, string uid)
    {
        var wa = giver.WatchedAttributes;
        if (wa is null) return;

        foreach (var q in catalog!)
        {
            if (!_acceptedSeen.Contains(q.QuestCode)
                && (wa.HasAttribute("lastaccepted-" + q.QuestCode + "-" + uid)
                    || wa.HasAttribute("lastaccepted-" + q.QuestCode)))
            {
                _acceptedSeen.Add(q.QuestCode);
                onAccepted(q);
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

    /// <summary>Progression Framework's independently-gated detection path (§3.1-3.5): reads that mod's
    /// own <see cref="PfPlayerQuestTreeKey"/> tree directly off the player's own entity — no entity scan,
    /// no reflection. Any failure (missing/malformed tree shape, e.g. after a PF update) permanently
    /// disables THIS path only for the session; vsquest's detection above is read in a separate try scope
    /// and is provably unaffected.</summary>
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
                if (string.Equals(status, PfStatusActive, StringComparison.OrdinalIgnoreCase)
                    && pfAcceptedSeen.Add(questCode))
                {
                    onAccepted(def.ToPickerEntry());
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
                    // A missing "progress" key leaves the last-cached value alone (quest-auto-detect's
                    // fail-safe scenario: a malformed/absent count never overwrites with a spurious 0, and
                    // never affects the status read above).
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

    /// <summary>Progression Framework server-scoped quests' independently-gated detection path (see class
    /// doc-comment): reads the mod's shared server-quest state via one reflected field access, then walks it
    /// exactly like <see cref="ScanProgressionFramework"/> walks the player-scoped tree — same catalog, same
    /// <see cref="pfAcceptedSeen"/>/<see cref="pfCompletedSeen"/>/<see cref="pfObjectiveStatus"/> dictionaries,
    /// same <see cref="onAccepted"/>/<see cref="onCompleted"/> callbacks, so a server-scoped quest's
    /// activation/completion is indistinguishable to the rest of the system from a player-scoped one. Any
    /// failure (missing type, missing field, unexpected field shape — e.g. after a Progression Framework
    /// update) permanently disables only this path for the session; player-scoped detection above is read in
    /// a separate try scope and is provably unaffected.</summary>
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
                if (string.Equals(status, PfStatusActive, StringComparison.OrdinalIgnoreCase)
                    && pfAcceptedSeen.Add(questCode))
                {
                    onAccepted(def.ToPickerEntry());
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

    /// <summary>Best-effort refresh of <see cref="liveProgress"/> from the open quest dialog, if any
    /// (§11.3). Mirrors Tallybook's <c>VsQuests.ReadQuestDialog()</c> exactly: find the dialog by type
    /// name among <see cref="IGuiAPI.OpenedGuis"/>/<see cref="IGuiAPI.LoadedGuis"/>, then read
    /// <c>activeQuests</c> and each active quest's three tracker lists via <see cref="AccessTools"/>. Any
    /// exception permanently disables this half (accept/complete detection is untouched).</summary>
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
