using Gui.Sound;
using Vintagestory.API.Client;

namespace Scribe;

/// <summary>
/// A no-op <see cref="ISoundPlayer"/> that silences Scribe's OWN LibGUI UI click sounds
/// (scribe-mute-ui-sounds). Swapped onto a Scribe dialog's <c>BuildOwner</c> in place of the stock
/// <see cref="SoundPlayer"/> when the player's <c>MuteUiSounds</c> preference is on; widgets pull the
/// player via <c>BuildContext.GetSoundPlayer()</c>, so this covers every Scribe control that plays a
/// sound (today only LibGUI's <c>Button</c> — the Lectern action buttons and numeric +/- steppers) and
/// automatically any control LibGUI teaches to click in future. Scoped to Scribe's dialogs only: vanilla
/// and other-mod audio route through their own players and are untouched.
///
/// <para><b>Why <see cref="Load"/> delegates to a real <see cref="SoundPlayer"/>:</b>
/// <see cref="SoundHandle"/>'s constructor is <c>internal</c> to the <c>gui</c> assembly, so this class
/// can't fabricate a handle. <see cref="Play"/> is what actually emits the click, so no-op-ing it is
/// what mutes; <see cref="Load"/> only PREPARES a sound (the caller must call <c>Start()</c> on the
/// handle to hear anything), and no Scribe control uses the <c>Load</c> path today — so delegating keeps
/// the contract's non-null-handle guarantee without playing anything. Stateless, so a single shared
/// instance is reused across dialogs/rebuilds rather than allocating per build.</para>
/// </summary>
internal sealed class SilentSoundPlayer : ISoundPlayer
{
    private readonly SoundPlayer loader;

    public SilentSoundPlayer(ICoreClientAPI capi)
    {
        // Used only to satisfy Load's non-null SoundHandle contract; never plays (Play is the no-op,
        // and a loaded-but-unstarted sound is silent).
        loader = new SoundPlayer(capi);
    }

    /// <summary>The mute itself: swallow every one-shot UI sound.</summary>
    public void Play(string name, Pitch pitch = default, float volume = 0.5f)
    {
        // Intentionally empty — this is the whole point of the silent player.
    }

    /// <summary>Prepare (but never start) a sound so callers using the handle API get a non-null handle.
    /// Nothing in Scribe drives this path; delegated purely to honor the interface's non-null contract.</summary>
    public SoundHandle Load(string name, bool loop = false, Pitch pitch = default, float volume = 0.5f)
        => loader.Load(name, loop, pitch, volume);
}
