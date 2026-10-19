namespace Scribe.Core;

/// <summary>One visitor entry in a lectern's guestbook. <see cref="InGameDate"/> (date only) is the
/// identity key; the date string and timestamp come from the Mod layer.</summary>
public sealed class GuestbookEntry
{
    public string PlayerName { get; set; } = "";
    public string InGameDate { get; set; } = "";
    public string Note       { get; set; } = "";

    /// <summary>In-game <c>Calendar.TotalDays</c> at record time, used to rebuild
    /// <c>scribe:date-format</c> in the viewer's locale. Null on v1 blobs; <c>0</c> is a real day,
    /// not a missing sentinel.</summary>
    public double? InGameTimestamp { get; set; }
}
