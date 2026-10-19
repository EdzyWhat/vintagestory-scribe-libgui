using Gui.Sound;
using Vintagestory.API.Client;

namespace Scribe;

/// <summary>
/// A no-op <see cref="ISoundPlayer"/> that silences Scribe's OWN LibGUI UI click sounds. Swapped onto a
/// Scribe dialog's <c>BuildOwner</c> in place of the stock <see cref="SoundPlayer"/> when <c>MuteUiSounds</c>
/// is on; widgets pull it via <c>BuildContext.GetSoundPlayer()</c>. Scoped to Scribe's dialogs only.
///
/// <para><see cref="Load"/> delegates to a real <see cref="SoundPlayer"/> because
/// <see cref="SoundHandle"/>'s constructor is <c>internal</c> to <c>gui</c>, so a non-null handle can't be
/// fabricated. That is silent: <see cref="Play"/> is what emits clicks, and a loaded sound is silent until
/// <c>Start()</c>. Stateless, so one shared instance is reused.</para>
/// </summary>
internal sealed class SilentSoundPlayer : ISoundPlayer
{
    private readonly SoundPlayer loader;

    public SilentSoundPlayer(ICoreClientAPI capi)
    {
        // Only satisfies Load's non-null SoundHandle contract.
        loader = new SoundPlayer(capi);
    }

    /// <summary>The mute itself: swallows every one-shot UI sound.</summary>
    public void Play(string name, Pitch pitch = default, float volume = 0.5f)
    {
        // Intentionally empty.
    }

    /// <summary>Prepares (never starts) a sound so handle-API callers get a non-null handle.</summary>
    public SoundHandle Load(string name, bool loop = false, Pitch pitch = default, float volume = 0.5f)
        => loader.Load(name, loop, pitch, volume);
}
