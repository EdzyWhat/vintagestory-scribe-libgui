namespace Scribe.Core;

/// <summary>One visitor entry in a lectern's guestbook: the player's display name, the in-game
/// calendar date (date only, no time) used as the identity key, an optional numeric in-game
/// timestamp for live-localized display, and an optional short note the visitor may write. No VS
/// API references — the date string and timestamp are passed from the Mod layer.</summary>
public sealed class GuestbookEntry
{
    public string PlayerName { get; set; } = "";
    public string InGameDate { get; set; } = "";
    public string Note       { get; set; } = "";

    /// <summary>In-game <c>Calendar.TotalDays</c> captured at record time so the Guestbook tab can
    /// rebuild <c>scribe:date-format</c> in the viewing client's locale. Null on v1 blobs (no
    /// timestamp was stored); <c>0</c> is a real calendar day (world day one), not a missing
    /// sentinel.</summary>
    public double? InGameTimestamp { get; set; }
}
