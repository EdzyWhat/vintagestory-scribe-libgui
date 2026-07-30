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

    /// <summary>Reserved for future lore/location discovery tracking. Not wired in this version.
    /// A <see cref="HistoryEntry"/> with this kind round-trips through the codec correctly so a
    /// future version can begin writing these entries without a breaking codec change.</summary>
    LoreDiscovery = 6,
}
