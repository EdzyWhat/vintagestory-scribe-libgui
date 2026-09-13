namespace Scribe.Core;

/// <summary>One entry in a Notebook's <see cref="HistoryStore"/>. No VS API references — the
/// formatted in-game date string is passed in by the Mod layer, matching the pattern used by
/// <see cref="GuestbookEntry"/>.</summary>
public sealed class HistoryEntry
{
    public HistoryEventKind Kind       { get; set; }
    public string ActorName            { get; set; } = "";   // player name; empty for world events
    public string Detail               { get; set; } = "";   // Manual body, or a legacy baked sentence
    public string InGameDate           { get; set; } = "";   // formatted calendar date (legacy baked rows)

    /// <summary>How this row should be displayed. Defaults to <see cref="HistorySchema.Baked"/> so
    /// existing constructors and v1–v3 payloads stay finished-sentence rows until the Mod layer
    /// starts writing live facts.</summary>
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

    /// <summary>Sortable in-game timestamp captured at the moment the entry is recorded, distinct
    /// from the display-only <see cref="InGameDate"/> string. Supplied by the Mod layer from
    /// <c>sapi.World.Calendar.TotalDays</c> (the same value <c>NotebookHost.FormatDate</c> already
    /// reads internally to build the display string) — never reverse-derived from
    /// <see cref="InGameDate"/>, which has no time-of-day component. <see cref="HistoryStore"/> keeps
    /// <see cref="Entries"/> sorted by this value (ties broken by write order) instead of by raw
    /// insertion order, so a delayed write (e.g. a queued Death entry flushed later) lands in its
    /// true chronological position.</summary>
    public double InGameTimestamp      { get; set; }

    /// <summary>Stable per-entry identifier, meaningful only for <see cref="HistoryEventKind.Manual"/>
    /// entries — every other kind leaves this <see cref="Guid.Empty"/>, since only a Manual entry is
    /// ever individually addressed for edit/delete. Minted client-side when the entry is created
    /// (mirroring <c>ScribeBlock.TaskId</c>'s client-generated-Guid pattern), not server-side, so the
    /// client has a stable local key to track an in-progress draft before any server round-trip.</summary>
    public Guid EntryId                { get; set; } = Guid.Empty;
}
