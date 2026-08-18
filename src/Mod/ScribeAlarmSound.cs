using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// Manages the Clockmaker Notebook alarm sound for a single timer-fire event.
/// Plays <c>scribe:alarm/clockbell</c> (ambient, no positional attenuation) once, driving
/// a three-phase volume envelope via a per-frame tick listener:
/// <list type="bullet">
///   <item><b>RampUp</b> (0–1 s): easeInCubic from silence to nominal volume.</item>
///   <item><b>Breathing</b> (1 s – clip end): sine-wave ±10% oscillation, 3 s period.</item>
///   <item><b>FadingOut</b> (500 ms): easeInOutSine to silence on early dismiss.</item>
/// </list>
/// Create one instance when the timer fires; call <see cref="Dismiss"/> on player dismiss;
/// check <see cref="IsDone"/> to know when it has self-cleaned.
/// </summary>
internal sealed class ScribeAlarmSound : IDisposable
{
    private enum Phase { RampUp, Breathing, FadingOut, Done }

    private const float RampDuration    = 0.5f;   // seconds
    private const float BreathPeriod    = 3f;     // seconds per sine cycle
    private const float BreathAmplitude = 0.1f;   // ±10 % of nominal
    private const float FadeDuration    = 0.3f;   // seconds for dismiss fade

    private readonly ICoreClientAPI  _capi;
    private readonly ILoadedSound?   _sound;
    private readonly Func<float>     _getNominalVolume;
    private readonly long            _tickId;

    private Phase  _phase          = Phase.RampUp;
    private float  _elapsed;            // total elapsed since start
    private float  _lastVol;            // last SetVolume value — used to seed fade-out
    private float  _fadeStartVol;       // volume at the instant Dismiss() was called
    private float  _fadeStartElapsed;   // _elapsed at the instant Dismiss() was called
    private bool   _disposed;
    private bool   _tickUnregistered;
    private bool   _pauseSubscribed;

    public ScribeAlarmSound(ICoreClientAPI capi, Func<float> getNominalVolume)
    {
        _capi             = capi;
        _getNominalVolume = getNominalVolume;

        _sound = capi.World.LoadSound(new SoundParams(new AssetLocation("scribe:sounds/alarm/clockbell"))
        {
            ShouldLoop       = false,
            DisposeOnFinish  = false,
            SoundType        = EnumSoundType.Sound,
            RelativePosition = true,
            Position         = new Vintagestory.API.MathTools.Vec3f(0f, 0f, 0f),
            Volume           = 0f,
        });

        if (_sound == null)
        {
            capi.Logger.Warning("[scribe] ScribeAlarmSound: LoadSound returned null for scribe:alarm/clockbell — alarm muted.");
            _phase = Phase.Done;
            return;
        }

        _sound.Start();

        _tickId = capi.World.RegisterGameTickListener(OnTick, 0);

        _pauseSubscribed = true;
        capi.Event.PauseResume += OnPauseResume;
    }

    public bool IsDone => _phase == Phase.Done;

    /// <summary>Trigger the 500 ms easeInOutSine fade-out. No-op if already fading or done.</summary>
    public void Dismiss()
    {
        if (_phase is Phase.RampUp or Phase.Breathing)
        {
            _fadeStartVol     = _lastVol;
            _fadeStartElapsed = _elapsed;
            _phase            = Phase.FadingOut;
        }
    }

    private void OnTick(float dt)
    {
        if (_phase == Phase.Done) return;

        _elapsed += dt;

        switch (_phase)
        {
            case Phase.RampUp:
            {
                float t   = Math.Min(_elapsed / RampDuration, 1f);
                float vol = Nominal * (t * t * t);          // easeInCubic
                Apply(vol);
                if (_elapsed >= RampDuration)
                    _phase = Phase.Breathing;
                break;
            }

            case Phase.Breathing:
            {
                float breathT = _elapsed - RampDuration;
                float vol     = Nominal * (1f + BreathAmplitude
                    * (float)Math.Sin(2 * Math.PI * breathT / BreathPeriod));
                Apply(Math.Clamp(vol, 0f, 1f));

                if (_sound?.HasStopped == true)
                    Finish();
                break;
            }

            case Phase.FadingOut:
            {
                float t    = Math.Min((_elapsed - _fadeStartElapsed) / FadeDuration, 1f);
                float ease = (1f - (float)Math.Cos(Math.PI * t)) / 2f;  // easeInOutSine
                float vol  = _fadeStartVol * (1f - ease);
                Apply(Math.Clamp(vol, 0f, 1f));

                if (t >= 1f)
                    Finish();
                break;
            }
        }
    }

    private void OnPauseResume(bool isPaused)
    {
        if (_sound is null or { IsDisposed: true }) return;
        if (isPaused) _sound.Pause();
        else          _sound.Start();
    }

    private float Nominal => Math.Clamp(_getNominalVolume(), 0f, 1f);

    private void Apply(float vol)
    {
        _lastVol = vol;
        if (_sound is { IsDisposed: false })
            _sound.SetVolume(vol);
    }

    private void Finish()
    {
        _phase = Phase.Done;
        StopAndDisposeSound();
        UnregisterTick();
        UnsubscribePause();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _phase    = Phase.Done;
        StopAndDisposeSound();
        UnregisterTick();
        UnsubscribePause();
    }

    private void StopAndDisposeSound()
    {
        if (_sound is { IsDisposed: false })
        {
            _sound.Stop();
            _sound.Dispose();
        }
    }

    private void UnregisterTick()
    {
        if (_tickUnregistered || _tickId == 0) return;
        _tickUnregistered = true;
        _capi.World.UnregisterGameTickListener(_tickId);
    }

    private void UnsubscribePause()
    {
        if (!_pauseSubscribed) return;
        _pauseSubscribed = false;
        _capi.Event.PauseResume -= OnPauseResume;
    }
}
