namespace Scribe.Core;

/// <summary>
/// A player's Scribe display/behavior preferences. These are per-player, CLIENT-LOCAL preferences —
/// persisted as JSON via the mod's client config (<c>ScribeModSystem.HudConfigFileName</c>), identical
/// across all of that player's worlds, and never server-synced. Kept deliberately small; new
/// preferences append as new properties (the JSON serializer defaults absent keys, so adding one never
/// breaks an existing file).
///
/// Defaults are what a player who has never changed a setting gets: see the property initializers.
/// Game-agnostic (pure BCL); the Mod layer owns persistence and any UI. The completion policy also
/// travels to the server in the completion request, where it is normalized the same way.
/// </summary>
public sealed class ScribePlayerSettings
{
    /// <summary>What completing a pinned task does to the task and the player's pin for it:
    /// <see cref="ScribeCompletionPolicy.Sink"/> (default — keep it pinned, the HUD de-prioritizes
    /// it), <see cref="ScribeCompletionPolicy.Unpin"/> (remove the pin), or
    /// <see cref="ScribeCompletionPolicy.Delete"/> (delete the underlying task). The client sends this
    /// with each completion request and the server applies it (the server owns the completion and any
    /// removal). Replaces the earlier boolean <c>CompleteUnpins</c>.</summary>
    public ScribeCompletionPolicy CompletionPolicy { get; set; } = ScribeCompletionPolicy.Sink;

    /// <summary>The pinned-task HUD: whether the player has collapsed or hidden it. Persisted and
    /// synced so the collapsed state is restored across sessions; toggled by the HUD's rebindable
    /// show/hide hotkey.</summary>
    public bool HudCollapsed { get; set; }

    /// <summary>Maximum number of pinned tasks the HUD shows at once (default 3); pins beyond this
    /// are summarized ("+N more"). A per-player display preference; the Mod layer owns the HUD and
    /// clamps this to a sane range on read (see <see cref="ScribePinCodec"/>).</summary>
    public int HudMaxRows { get; set; } = DefaultHudMaxRows;

    /// <summary>Default <see cref="HudMaxRows"/> for a player who has never changed it.</summary>
    public const int DefaultHudMaxRows = 3;

    /// <summary>Inclusive lower bound the codec clamps <see cref="HudMaxRows"/> to on read.</summary>
    public const int MinHudMaxRows = 1;

    /// <summary>Inclusive upper bound clamped on load, so a hand-edited or garbled preference file
    /// can't request an unbounded number of rows.</summary>
    public const int MaxHudMaxRows = 20;

    /// <summary>Clamps a loaded HUD row count to the safe range. Applied when reading the client
    /// preference config so a hand-edited or corrupted value can't produce an out-of-range state.</summary>
    public static int ClampHudMaxRows(int value) => Math.Clamp(value, MinHudMaxRows, MaxHudMaxRows);

    /// <summary>Maps a loaded completion-policy value to a defined <see cref="ScribeCompletionPolicy"/>,
    /// falling back to the default (<see cref="ScribeCompletionPolicy.Sink"/>) for any unrecognized
    /// value so a hand-edited or corrupted config can't select an undefined behavior. The client also
    /// carries its policy in the completion request, where the server normalizes it the same way.</summary>
    public static ScribeCompletionPolicy NormalizePolicy(ScribeCompletionPolicy value) =>
        Enum.IsDefined(typeof(ScribeCompletionPolicy), value) ? value : ScribeCompletionPolicy.Sink;

    /// <summary>Normalizes this instance's fields in place after a load from an untrusted source
    /// (hand-edited JSON): clamps <see cref="HudMaxRows"/> and falls an unknown
    /// <see cref="CompletionPolicy"/> back to the default. Returns this for chaining.</summary>
    public ScribePlayerSettings Normalized()
    {
        HudMaxRows = ClampHudMaxRows(HudMaxRows);
        CompletionPolicy = NormalizePolicy(CompletionPolicy);
        return this;
    }
}
