namespace Scribe.Core;

/// <summary>One entry in a Notebook's <see cref="HistoryStore"/>; the date string comes from the Mod layer.</summary>
public sealed class HistoryEntry
{
    public HistoryEventKind Kind       { get; set; }
    public string ActorName            { get; set; } = "";   // player name; empty for world events
    public string Detail               { get; set; } = "";   // Manual body, or a legacy baked sentence
    public string InGameDate           { get; set; } = "";   // formatted calendar date (legacy baked rows)

    /// <summary>How this row is displayed; defaults to <see cref="HistorySchema.Baked"/> (v1–v3 payloads).</summary>
    public HistorySchema Schema        { get; set; } = HistorySchema.Baked;

    /// <summary>Live-schema subject (victim for Death/PvpKill, slayer for BossKill). Empty on baked
    /// rows and on TemporalStorm.</summary>
    public string SubjectName          { get; set; } = "";

    /// <summary>Live-schema counterpart (killer for Death/PvpKill). Empty otherwise.</summary>
    public string OtherName            { get; set; } = "";

    /// <summary>Live-schema opaque token whose meaning depends on <see cref="Kind"/>: creature
    /// entity code, environmental cause, <c>EnumTool</c> name, boss key, or storm strength.</summary>
    public string RefCode              { get; set; } = "";

    /// <summary>Second live-schema token (PvP <c>EnumDamageType</c> name). Empty for other kinds.</summary>
    public string RefCode2             { get; set; } = "";

    /// <summary>Live-schema seed mapped into the viewer's flavor/verb pool by remainder at display
    /// time. Unused (0) on baked rows and on kinds that do not pick from a pool.</summary>
    public int FlavorSeed              { get; set; }

    /// <summary>Sortable in-game timestamp (<c>sapi.World.Calendar.TotalDays</c>) captured at record
    /// time; never derived from <see cref="InGameDate"/>, which has no time of day.
    /// <see cref="HistoryStore"/> sorts by this (ties by write order), so a delayed write lands in its
    /// true chronological position.</summary>
    public double InGameTimestamp      { get; set; }

    /// <summary>Stable id, meaningful only for <see cref="HistoryEventKind.Manual"/> entries (other kinds
    /// leave <see cref="Guid.Empty"/>). Minted client-side so a draft has a stable key before any round-trip.</summary>
    public Guid EntryId                { get; set; } = Guid.Empty;
}
