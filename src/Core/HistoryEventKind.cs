namespace Scribe.Core;

/// <summary>The kind of event recorded in a Notebook's <see cref="HistoryStore"/>.</summary>
public enum HistoryEventKind : byte
{
    Crafted       = 0,
    PickedUp      = 1,
    Death         = 2,
    PvpKill       = 3,
    BossKill      = 4,
    TemporalStorm = 5,

    /// <summary>Reserved for future lore/location discovery; not written yet, but round-trips through the codec.</summary>
    LoreDiscovery = 6,

    /// <summary>Player-authored free text, addressed by <see cref="HistoryEntry.EntryId"/> and editable/deletable
    /// only by <see cref="HistoryEntry.ActorName"/>.</summary>
    Manual = 7,
}
