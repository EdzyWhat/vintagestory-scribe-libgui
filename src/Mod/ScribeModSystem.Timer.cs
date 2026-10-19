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
        var previousStatus = MyTimer?.Status;

        MyTimer = new TimerStore
        {
            Status              = message.Status,
            Mode                = message.Mode,
            Label               = message.Label ?? "",
            RemainingSeconds    = message.RemainingSeconds,
            FiredElapsedSeconds = message.FiredElapsedSeconds,
        };
        MyTimerChanged?.Invoke();

        // Start the alarm on the first transition to Fired, only while the clip is still in its playing window (a relog near expiry must not restart it).
        if (capi is not null
            && message.Status == TimerStatus.Fired
            && previousStatus != TimerStatus.Fired
            && message.FiredElapsedSeconds < 26.0
            && (_activeAlarm is null || _activeAlarm.IsDone))
        {
            _activeAlarm?.Dispose();
            _activeAlarm = new ScribeAlarmSound(capi, () => MySettings.TimerAlarmVolume / 100f);
        }

        if (capi is not null)
        {
            foreach (var dialog in capi.Gui.OpenedGuis.OfType<GuiDialogClockmakerNotebook>())
                if (dialog.IsOpened()) dialog.RefreshTimerView();
        }
    }

    /// <summary>Begins the alarm's 500 ms fade-out; called by both dismiss paths (HUD fired-row click, Stop Timer). Safe when no alarm is playing.</summary>
    public void DismissAlarm()
    {
        _activeAlarm?.Dismiss();
    }

    /// <summary>1-second server tick: decrements running timers and fires at zero. <c>RemainingSeconds</c> is in
    /// the unit entered: RealTime counts one per real second; InGame drains at the world's in-game rate (so it
    /// pauses when the world does).
    ///
    /// <para>The server does NOT auto-clear a fired timer: auto-disappear depends on the client-local
    /// <see cref="ScribePlayerSettings.TimerAutoDisappear"/>, so the client drives the clear. The server only
    /// accumulates <see cref="TimerStore.FiredElapsedSeconds"/> so the flash window resumes across a relog.</para></summary>
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
                // Real seconds regardless of countdown mode; no auto-removal (the client sends the clear).
                store.FiredElapsedSeconds += 1.0;
            }
        }
    }

}
