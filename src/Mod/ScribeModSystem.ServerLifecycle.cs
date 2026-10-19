using System;
using System.Collections.Generic;
using Gui.Rendering;             // SkiaAssetLoader
using Gui.Rendering.Text;        // FontRegistry, FontWeight
using Gui.Sound;                 // ISoundPlayer, SoundPlayer (UI click sound)
using Scribe.Core;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Scribe;

public sealed partial class ScribeModSystem
{
    /// <summary>
    /// Clockmaker's Notebook crafting is gated by the vanilla <c>tinkerer</c> trait via the recipe's
    /// <c>requiresTrait</c>. The <c>scribeClockmakerRequiresTrait</c> worldconfig boolean (default enforced) lets
    /// an operator disable it by nulling <see cref="GridRecipe.RequiresTrait"/> on the loaded recipes; a second
    /// MatchesGridRecipe handler can't dependably override the survival mod's deny. Runs in StartServerSide,
    /// after grid recipes register and World.Config is populated.
    /// </summary>
    private static void ApplyClockmakerTraitGate(ICoreServerAPI api)
    {
        // Always pass an explicit default: worlds created before this key existed won't have it baked
        // into the savegame, and GetBool does not consult the registered attribute default at read time.
        bool requireTrait = api.World.Config.GetBool("scribeClockmakerRequiresTrait", true);
        if (requireTrait) return;

        int cleared = 0;
        foreach (var recipe in api.World.GridRecipes)
        {
            if (recipe.Output?.Code?.Path == "scribeclockmakernotebook" && recipe.RequiresTrait is not null)
            {
                recipe.RequiresTrait = null;
                cleared++;
            }
        }

        if (cleared > 0)
        {
            api.Logger.Notification(
                "[scribe] scribeClockmakerRequiresTrait disabled: cleared the tinkerer trait requirement on {0} Clockmaker's Notebook recipe(s).",
                cleared);
        }
    }

    private void OnSaveGameLoaded()
    {
        if (sapi is null || pinStore is null) return;
        var pinBytes = sapi.WorldManager.SaveGame.GetData(PinStoreSaveKey);
        pinStore.LoadFrom(pinBytes);

        assignmentStore?.LoadFrom(sapi.WorldManager.SaveGame.GetData(AssignmentStoreSaveKey));
        questDecisionStore?.LoadFrom(sapi.WorldManager.SaveGame.GetData(QuestDecisionStoreSaveKey));
        playerLocationStore?.LoadFrom(sapi.WorldManager.SaveGame.GetData(PlayerLocationStoreSaveKey));
        pendingHistoryStore?.LoadFrom(sapi.WorldManager.SaveGame.GetData(PendingHistoryStoreSaveKey));
        knownPlayersStore?.LoadFrom(sapi.WorldManager.SaveGame.GetData(KnownPlayersStoreSaveKey));

        if (timerStores is not null)
        {
            timerStores.Clear();
            var timerBytes = sapi.WorldManager.SaveGame.GetData(TimerStoreSaveKey);
            if (timerBytes is not null)
            {
                try
                {
                    using var ms = new System.IO.MemoryStream(timerBytes, writable: false);
                    using var r  = new System.IO.BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
                    int count = r.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        string uid   = r.ReadString();
                        int    len   = r.ReadInt32();
                        byte[] blob  = r.ReadBytes(len);
                        var    store = TimerStore.Deserialize(blob);
                        // Resume Running and fired-but-undismissed timers; a Fired timer carries FiredElapsedSeconds so its
                        // auto-disappear resumes rather than restarts (matches the filter in OnGameWorldSave). v1 saves never
                        // persisted Fired timers, so no legacy Fired blob can carry elapsed=0 and flash a full window.
                        if (store.Status == TimerStatus.Running && store.RemainingSeconds > 0)
                            timerStores[uid] = store;
                        else if (store.Status == TimerStatus.Fired)
                            timerStores[uid] = store;
                    }
                }
                catch { /* Malformed — start fresh. */ }
            }
        }
    }

    private void OnGameWorldSave()
    {
        if (sapi is null || pinStore is null) return;
        sapi.WorldManager.SaveGame.StoreData(PinStoreSaveKey, pinStore.SerializePins());

        if (assignmentStore is not null)
            sapi.WorldManager.SaveGame.StoreData(AssignmentStoreSaveKey, assignmentStore.SerializeStore());

        if (questDecisionStore is not null)
            sapi.WorldManager.SaveGame.StoreData(QuestDecisionStoreSaveKey, questDecisionStore.SerializeStore());

        if (playerLocationStore is not null)
            sapi.WorldManager.SaveGame.StoreData(PlayerLocationStoreSaveKey, playerLocationStore.Serialize());

        if (pendingHistoryStore is not null)
            sapi.WorldManager.SaveGame.StoreData(PendingHistoryStoreSaveKey, pendingHistoryStore.SerializeStore());

        if (knownPlayersStore is not null)
            sapi.WorldManager.SaveGame.StoreData(KnownPlayersStoreSaveKey, knownPlayersStore.Serialize());

        if (timerStores is not null)
        {
            // Persist Running and fired-but-undismissed timers (dropping a fired one would lose an unacknowledged
            // notification); FiredElapsedSeconds is saved so auto-disappear resumes. Idle timers are dropped.
            var persisted = timerStores
                .Where(kv => kv.Value.Status is TimerStatus.Running or TimerStatus.Fired)
                .ToList();
            if (persisted.Count > 0)
            {
                using var ms = new System.IO.MemoryStream();
                using (var w = new System.IO.BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    w.Write(persisted.Count);
                    foreach (var (uid, store) in persisted)
                    {
                        var blob = store.Serialize();
                        w.Write(uid);
                        w.Write(blob.Length);
                        w.Write(blob);
                    }
                }
                sapi.WorldManager.SaveGame.StoreData(TimerStoreSaveKey, ms.ToArray());
            }
            else
            {
                sapi.WorldManager.SaveGame.StoreData(TimerStoreSaveKey, System.Array.Empty<byte>());
            }
        }
    }

    private void OnPlayerNowPlaying(IServerPlayer player)
    {
        PushPinsTo(player);
        PushTimerTo(player);
        PushAssignmentsTo(player);
        PushQuestDecisionsTo(player);
        UpsertKnownPlayerAndBroadcast(player);
    }

}
