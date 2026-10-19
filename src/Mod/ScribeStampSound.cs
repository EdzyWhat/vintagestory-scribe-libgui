using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// The one-shot stamp-thump cue shared by every <see cref="ScribeStamp"/> flourish. Wire it to
/// <see cref="ScribeStamp.OnDescend"/> so it plays only when the flourish is mounted and seen.
///
/// <para>Non-load-bearing: a null <c>LoadSound</c> logs one warning and no-ops. <see cref="EnumSoundType.Sound"/>
/// routes it through the base-game "Sound Effects" volume; <c>DisposeOnFinish</c> self-cleans the clip.</para>
/// <para>Volume is FIXED at unity: the final loudness is baked into <c>stamp.ogg</c>, which stays within
/// the engine's safe [0,1] range.</para>
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
            Volume           = 1f,   // level baked into stamp.ogg; not mapped to the alarm slider
        });

        if (sound == null)
        {
            capi.Logger.Warning("[scribe] ScribeStampSound.Play: LoadSound returned null for scribe:sounds/stamp — stamp cue muted.");
            return;
        }

        sound.Start();
    }
}
