namespace Scribe.Core;

/// <summary>
/// A player's Scribe display/behavior preferences. Held server-side alongside their pin set,
/// persisted with the save game, and synced to that player. Kept deliberately small; new
/// preferences append as new fields (see <see cref="ScribePinCodec"/>'s versioned settings blob)
/// so adding one never breaks an existing save.
///
/// Defaults are what a player who has never changed a setting gets: see the property initializers.
/// Game-agnostic (pure BCL); the Mod layer owns persistence, sync, and any UI.
/// </summary>
public sealed class ScribePlayerSettings
{
    /// <summary>When true (the default), completing a task the player has pinned also removes that
    /// player's pin for it — the "check it off and it leaves my list" gesture. A player can opt out
    /// so completed tasks stay pinned (e.g. to keep a visible record). Enforced server-side, since
    /// the server owns both the completion and the pin removal.</summary>
    public bool CompleteUnpins { get; set; } = true;

    /// <summary>Reserved for the pinned-task HUD (a later change): whether the player has collapsed
    /// or minimized it. Persisted and synced now so the HUD change needs no format bump; nothing
    /// reads it yet.</summary>
    public bool HudCollapsed { get; set; }
}
