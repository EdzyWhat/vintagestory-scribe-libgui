using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// The one-shot stamp-thump cue shared by every <see cref="ScribeStamp"/> flourish — the Scriptorium's
/// Copy/Import/Export stamps and the Assignment Desk's "Submitted to Player" stamp
/// (refine-assignment-desk-inbox-ux 10.3) — extracted so the sound-load boilerplate isn't duplicated per
/// dialog. Wire it to <see cref="ScribeStamp.OnDescend"/> so it plays only when the flourish is actually
/// mounted and seen by this client.
///
/// <para>Non-load-bearing (mirrors <see cref="ScribeAlarmSound"/> and the stamp bitmap): a null
/// <c>LoadSound</c> logs one warning and no-ops. <see cref="EnumSoundType.Sound"/> routes it through the
/// base-game "Sound Effects" volume; <c>DisposeOnFinish</c> self-cleans the ~0.6s clip.</para>
/// <para>Volume is FIXED at unity: the final loudness (the "alarm-volume-140" level the author calibrated
/// to in-game) is baked into the mono <c>stamp.ogg</c> (+16.9 dB / 7× the source, ~ −14 dBFS peak), so
/// unity plays it at exactly that level and stays within the engine's safe [0,1] range.
/// <see cref="EnumSoundType.Sound"/> still routes it through the base-game "Sound Effects" slider — that
/// is the intended, retained volume tie.</para>
/// </summary>
internal static class ScribeStampSound
{
    public static void Play(ICoreClientAPI capi)
    {
        var sound = capi.World.LoadSound(new SoundParams(new AssetLocation("scribe:sounds/stamp"))
        {
            ShouldLoop       = false,
            DisposeOnFinish  = true,
            SoundType        = EnumSoundType.Sound,
            RelativePosition = true,
            Position         = new Vec3f(0f, 0f, 0f),
            Volume           = 1f,   // level baked into stamp.ogg; unmapped from the alarm slider (2026-08-20)
        });

        if (sound == null)
        {
            capi.Logger.Warning("[scribe] ScribeStampSound.Play: LoadSound returned null for scribe:sounds/stamp — stamp cue muted.");
            return;
        }

        sound.Start();
    }
}
