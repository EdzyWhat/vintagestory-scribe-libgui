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

    /// <summary>Which screen corner/edge the HUD is pinned to (default <see cref="ScribeHudAnchor.TopRight"/>,
    /// pre-offset left of the minimap by the Mod layer). A per-player display preference; the Mod layer
    /// maps it to a screen position. An unknown value falls back to the default on load.</summary>
    public ScribeHudAnchor HudAnchor { get; set; } = ScribeHudAnchor.TopRight;

    /// <summary>Horizontal pixel nudge applied to the HUD from its <see cref="HudAnchor"/>, so it can be
    /// moved clear of another on-screen overlay (minimap / coordinate / block-info). Positive moves the
    /// HUD toward screen-center from a right anchor and rightward from a left/middle anchor; the Mod
    /// layer owns the exact sign convention per anchor. Defaults to a Mod-supplied value (0 here; the
    /// Mod layer's default top-right resolver applies the minimap clearance).</summary>
    public int HudOffsetX { get; set; }

    /// <summary>Vertical pixel nudge applied to the HUD from its <see cref="HudAnchor"/> (see
    /// <see cref="HudOffsetX"/>). Defaults to 0.</summary>
    public int HudOffsetY { get; set; }

    /// <summary>Fixed pixel width of the HUD's task-row area (default 250); a long task wraps within
    /// this width instead of the HUD growing arbitrarily wide. Clamped to a sane range on load.</summary>
    public int HudRowWidth { get; set; } = DefaultHudRowWidth;

    /// <summary>Default <see cref="HudRowWidth"/> for a player who has never changed it.</summary>
    public const int DefaultHudRowWidth = 250;

    /// <summary>Inclusive lower bound clamped on load, so a hand-edited value can't collapse the HUD to
    /// an unusably narrow (or non-positive) width.</summary>
    public const int MinHudRowWidth = 80;

    /// <summary>Inclusive upper bound clamped on load, so a hand-edited value can't stretch the HUD
    /// across the whole screen.</summary>
    public const int MaxHudRowWidth = 1000;

    /// <summary>Clamps a loaded HUD row count to the safe range. Applied when reading the client
    /// preference config so a hand-edited or corrupted value can't produce an out-of-range state.</summary>
    public static int ClampHudMaxRows(int value) => Math.Clamp(value, MinHudMaxRows, MaxHudMaxRows);

    /// <summary>Clamps a loaded HUD row width to the safe range (see <see cref="HudRowWidth"/>).</summary>
    public static int ClampHudRowWidth(int value) => Math.Clamp(value, MinHudRowWidth, MaxHudRowWidth);

    /// <summary>Maps a loaded HUD anchor value to a defined <see cref="ScribeHudAnchor"/>, falling back
    /// to the default (<see cref="ScribeHudAnchor.TopRight"/>) for any unrecognized value so a
    /// hand-edited or corrupted config can't select an undefined anchor.</summary>
    public static ScribeHudAnchor NormalizeAnchor(ScribeHudAnchor value) =>
        Enum.IsDefined(typeof(ScribeHudAnchor), value) ? value : ScribeHudAnchor.TopRight;

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
        HudAnchor = NormalizeAnchor(HudAnchor);
        HudRowWidth = ClampHudRowWidth(HudRowWidth);
        return this;
    }
}
