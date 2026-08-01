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
    // ── Timer ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Server → client: push the current timer state to the player.</summary>
    private void PushTimerTo(IServerPlayer player)
    {
        if (sapi is null || timerStores is null) return;
        var store = timerStores.TryGetValue(player.PlayerUID, out var s) ? s : new TimerStore();
        sapi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeTimerStateMessage
        {
            Status              = store.Status,
            Mode                = store.Mode,
            Label               = store.Label,
            RemainingSeconds    = store.RemainingSeconds,
            FiredElapsedSeconds = store.FiredElapsedSeconds,
        }, player);
    }

    private void OnServerReceivedSetTimer(IServerPlayer fromPlayer, ScribeSetTimerMessage message)
    {
        if (sapi is null || timerStores is null) return;
        if (message.DurationSeconds <= 0) return;

        timerStores[fromPlayer.PlayerUID] = new TimerStore
        {
            Status           = TimerStatus.Running,
            Mode             = message.Mode,
            Label            = message.Label ?? "",
            RemainingSeconds = message.DurationSeconds,
        };
        PushTimerTo(fromPlayer);
    }

    private void OnServerReceivedClearTimer(IServerPlayer fromPlayer, ScribeClearTimerMessage _)
    {
        if (sapi is null || timerStores is null) return;
        timerStores.Remove(fromPlayer.PlayerUID);
        PushTimerTo(fromPlayer);
    }

    private void OnClientReceivedTimerState(ScribeTimerStateMessage message)
    {
        MyTimer = new TimerStore
        {
            Status              = message.Status,
            Mode                = message.Mode,
            Label               = message.Label ?? "",
            RemainingSeconds    = message.RemainingSeconds,
            FiredElapsedSeconds = message.FiredElapsedSeconds,
        };
        MyTimerChanged?.Invoke();
        // Refresh the Timer tab in any open Clockmaker's Notebook dialog.
        if (capi is not null)
        {
            foreach (var dialog in capi.Gui.OpenedGuis.OfType<GuiDialogClockmakerNotebook>())
                if (dialog.IsOpened()) dialog.RefreshTimerView();
        }
    }

    /// <summary>1-second server tick: decrement running timers and fire at zero. A timer's
    /// <c>RemainingSeconds</c> is stored in the unit the player entered. In RealTime mode it counts down
    /// one-per-real-second; in InGame mode it drains at the world's in-game time rate, so an entered
    /// in-game duration fires when that much in-game time has actually passed (≈30× faster than real time
    /// by default). This also means InGame timers pause exactly when the world does.
    ///
    /// <para>The server does NOT auto-clear a fired timer: the 30 s auto-disappear is governed by the
    /// player's client-local <see cref="ScribePlayerSettings.TimerAutoDisappear"/> preference, which only
    /// the client knows, so the client drives the clear (timer-auto-disappear-setting). The server merely
    /// accumulates <see cref="TimerStore.FiredElapsedSeconds"/> on the fired store so the flash window is
    /// persisted and resumes (not restarts) across a relog.</para></summary>
    private void OnTimerTick(float _)
    {
        if (sapi is null || timerStores is null) return;

        double inGameRate = ScribeTimeRate.InGamePerReal(sapi);

        foreach (var (uid, store) in timerStores)
        {
            var player = sapi.World.PlayerByUid(uid) as IServerPlayer;

            if (store.Status == TimerStatus.Running)
            {
                store.RemainingSeconds -= store.Mode == TimerMode.InGame ? inGameRate : 1.0;
                if (store.RemainingSeconds <= 0)
                {
                    store.RemainingSeconds = 0;
                    store.Status = TimerStatus.Fired;
                    store.FiredElapsedSeconds = 0;
                }
                if (player is not null) PushTimerTo(player);
            }
            else if (store.Status == TimerStatus.Fired)
            {
                // Keep the persisted fired-elapsed advancing (real seconds — the flash window is real-time
                // regardless of the timer's countdown mode). No auto-removal here: the client sends the
                // clear when its "Timer disappears" preference is on and the window elapses.
                store.FiredElapsedSeconds += 1.0;
            }
        }
    }

}
