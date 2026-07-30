namespace Scribe.Core;

/// <summary>One entry in a Notebook's <see cref="HistoryStore"/>. No VS API references — the
/// formatted in-game date string is passed in by the Mod layer, matching the pattern used by
/// <see cref="GuestbookEntry"/>.</summary>
public sealed class HistoryEntry
{
    public HistoryEventKind Kind       { get; set; }
    public string ActorName            { get; set; } = "";   // player name; empty for world events
    public string Detail               { get; set; } = "";   // death msg, storm strength, boss name, manual text
    public string InGameDate           { get; set; } = "";   // formatted calendar date (Mod layer supplies)
}
