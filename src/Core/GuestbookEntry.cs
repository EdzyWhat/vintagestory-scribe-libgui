namespace Scribe.Core;

/// <summary>One visitor entry in a lectern's guestbook: the player's display name, the in-game
/// calendar date (date only, no time), and an optional short note the visitor may write. No VS
/// API references — the date string is passed from the Mod layer.</summary>
public sealed class GuestbookEntry
{
    public string PlayerName { get; set; } = "";
    public string InGameDate { get; set; } = "";
    public string Note       { get; set; } = "";
}
