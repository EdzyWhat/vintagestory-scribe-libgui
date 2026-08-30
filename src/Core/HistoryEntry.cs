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

    /// <summary>Stable per-entry identifier, meaningful only for <see cref="HistoryEventKind.Manual"/>
    /// entries — every other kind leaves this <see cref="Guid.Empty"/>, since only a Manual entry is
    /// ever individually addressed for edit/delete. Minted client-side when the entry is created
    /// (mirroring <c>ScribeBlock.TaskId</c>'s client-generated-Guid pattern), not server-side, so the
    /// client has a stable local key to track an in-progress draft before any server round-trip.</summary>
    public Guid EntryId                { get; set; } = Guid.Empty;
}
