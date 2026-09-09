using System.Text;

namespace Scribe.Core;

/// <summary>
/// Append-only chronicle for a Notebook item. Stores events recorded automatically by the
/// game (crafted, picked up, deaths, PvP kills, boss kills, temporal storms) and up to
/// <see cref="MaxManual"/> player-authored entries.
///
/// Sliding-window caps drop the oldest entry of a given kind when the cap is reached (so the
/// most recent entries of each kind always survive, per <see cref="MaxDeaths"/> etc.). Manual
/// entries use the same sliding-window policy. PickedUp and Crafted use identity deduplication
/// instead.
///
/// Accepted-version window (progressive reads — see docs/CODEC-MIGRATION.md):
///   Current : v3 — adds a per-entry sortable <c>InGameTimestamp</c> (8-byte double)
///   v2      : adds a per-entry <c>EntryId</c> (16-byte Guid), used only by Manual entries
///   Min     : v1 — no EntryId, no InGameTimestamp
///   Older   : rejected
///
/// A v1 or v2 payload has no recorded timestamp, so <see cref="ApplyV2ToV3Migrations"/> assigns
/// every entry a synthetic one from a strictly-increasing negative sequence in its existing list
/// order — preserving relative order while sorting before anything recorded after this change.
///
/// Serialized format (SHST v3, little-endian via <see cref="BinaryWriter"/>):
///   [4 bytes magic "SHST"][1 byte version][int entryCount]
///   [per entry: byte kind, string actorName, string detail, string inGameDate, 16 bytes entryId,
///    8 bytes inGameTimestamp]
///
/// See docs/CODEC-MIGRATION.md for the version-bump pattern.
/// </summary>
public sealed class HistoryStore
{
    private static readonly byte[] Magic = "SHST"u8.ToArray();
    private const byte Version    = 3;
    private const byte MinVersion = 1;

    public const int MaxDeaths    = 30;
    public const int MaxStorms    = 10;
    public const int MaxPvpKills  = 30;
    public const int MaxBossKills = 20;
    public const int MaxManual    = 30;

    /// <summary>Hard allocation guard: reject any payload larger than this.</summary>
    public const int MaxHistoryBytes = 64 * 1024;

    private readonly List<HistoryEntry> _entries = new();

    /// <summary>All entries in chronological order by <see cref="HistoryEntry.InGameTimestamp"/>
    /// (oldest first; ties broken by write order). The display layer reverses for newest-first.</summary>
    public IReadOnlyList<HistoryEntry> Entries => _entries;

    /// <summary>
    /// Adds an entry, enforcing per-kind caps and deduplication rules:
    /// <list type="bullet">
    /// <item><b>Crafted</b> — rejected if any Crafted entry already exists.</item>
    /// <item><b>PickedUp</b> — rejected if any PickedUp entry with the same <c>ActorName</c> exists.</item>
    /// <item><b>Death / PvpKill / BossKill / TemporalStorm / Manual</b> — sliding window: oldest of
    /// that kind is dropped when the cap is reached, then the new entry is appended.</item>
    /// <item><b>LoreDiscovery</b> — appended unconditionally (no cap in this version).</item>
    /// </list>
    /// Returns true if an entry was added.
    /// </summary>
    public bool TryAddEntry(HistoryEntry entry)
    {
        switch (entry.Kind)
        {
            case HistoryEventKind.Crafted:
                if (_entries.Any(e => e.Kind == HistoryEventKind.Crafted)) return false;
                break;

            case HistoryEventKind.PickedUp:
                if (_entries.Any(e => e.Kind == HistoryEventKind.PickedUp && e.ActorName == entry.ActorName))
                    return false;
                break;

            case HistoryEventKind.Death:
                DropOldestOfKindIfAtCap(HistoryEventKind.Death, MaxDeaths);
                break;

            case HistoryEventKind.PvpKill:
                DropOldestOfKindIfAtCap(HistoryEventKind.PvpKill, MaxPvpKills);
                break;

            case HistoryEventKind.BossKill:
                DropOldestOfKindIfAtCap(HistoryEventKind.BossKill, MaxBossKills);
                break;

            case HistoryEventKind.TemporalStorm:
                DropOldestOfKindIfAtCap(HistoryEventKind.TemporalStorm, MaxStorms);
                break;

            case HistoryEventKind.Manual:
                DropOldestOfKindIfAtCap(HistoryEventKind.Manual, MaxManual);
                break;

            // LoreDiscovery and any future kinds: append unconditionally.
        }

        InsertSorted(entry);
        return true;
    }

    /// <summary>Inserts <paramref name="entry"/> at its correct position by
    /// <see cref="HistoryEntry.InGameTimestamp"/> — before the first existing entry with a strictly
    /// greater timestamp, so entries with an equal timestamp keep write order (the tie-break the
    /// requirement calls for).</summary>
    private void InsertSorted(HistoryEntry entry)
    {
        int idx = _entries.Count;
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].InGameTimestamp > entry.InGameTimestamp) { idx = i; break; }
        }
        _entries.Insert(idx, entry);
    }

    private void DropOldestOfKindIfAtCap(HistoryEventKind kind, int cap)
    {
        int count = _entries.Count(e => e.Kind == kind);
        if (count < cap) return;
        // _entries is globally sorted by timestamp, so the first match of this kind is also the
        // chronologically oldest one of that kind.
        int idx = _entries.FindIndex(e => e.Kind == kind);
        if (idx >= 0) _entries.RemoveAt(idx);
    }

    /// <summary>Updates an existing Manual entry's text, addressed by <see cref="HistoryEntry.EntryId"/>
    /// and authorized by matching <paramref name="authorName"/> against the entry's own
    /// <see cref="HistoryEntry.ActorName"/> — the caller (Mod layer) supplies the REQUESTING player's
    /// own name, never a client-claimed identity, mirroring <c>GuestbookStore.TrySetNote</c>'s
    /// sender-identity check. Text is clamped to <see cref="Scribe.Core.ScribeDocumentCodec.MaxTaskTextLength"/>.
    /// No-ops (returns false) on an unknown <c>EntryId</c>, a non-Manual entry, an author mismatch, or
    /// an unchanged value — an entry may be edited down to empty text without being removed (only a
    /// never-created draft is discarded; that's a Mod-layer concern, not this store's).</summary>
    public bool TrySetManualEntryText(Guid entryId, string authorName, string text)
    {
        var entry = _entries.FirstOrDefault(e =>
            e.Kind == HistoryEventKind.Manual && e.EntryId == entryId && e.ActorName == authorName);
        if (entry is null) return false;

        if (text.Length > ScribeDocumentCodec.MaxTaskTextLength)
            text = text[..ScribeDocumentCodec.MaxTaskTextLength];
        if (entry.Detail == text) return false;

        entry.Detail = text;
        return true;
    }

    /// <summary>Removes a Manual entry, addressed by <see cref="HistoryEntry.EntryId"/> and authorized
    /// the same way as <see cref="TrySetManualEntryText"/>. No-ops (returns false) on an unknown
    /// <c>EntryId</c>, a non-Manual entry, or an author mismatch.</summary>
    public bool TryDeleteManualEntry(Guid entryId, string authorName)
    {
        var entry = _entries.FirstOrDefault(e =>
            e.Kind == HistoryEventKind.Manual && e.EntryId == entryId && e.ActorName == authorName);
        if (entry is null) return false;

        _entries.Remove(entry);
        return true;
    }

    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(_entries.Count);
            foreach (var e in _entries)
            {
                w.Write((byte)e.Kind);
                w.Write(e.ActorName);
                w.Write(e.Detail);
                w.Write(e.InGameDate);
                w.Write(e.EntryId.ToByteArray());
                w.Write(e.InGameTimestamp);
            }
        }
        return ms.ToArray();
    }

    public static HistoryStore Deserialize(byte[]? bytes)
    {
        var store = new HistoryStore();
        if (bytes is null || bytes.Length < Magic.Length + 1) return store;
        if (bytes.Length > MaxHistoryBytes) return store; // allocation guard

        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var r  = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            var magic = r.ReadBytes(Magic.Length);
            if (!magic.AsSpan().SequenceEqual(Magic)) return store;

            byte version = r.ReadByte();
            if (version < MinVersion || version > Version) return store;

            int count = r.ReadInt32();
            if (count < 0 || count > bytes.Length) return store;

            for (int i = 0; i < count; i++)
            {
                var entry = new HistoryEntry
                {
                    Kind       = (HistoryEventKind)r.ReadByte(),
                    ActorName  = r.ReadString(),
                    Detail     = r.ReadString(),
                    InGameDate = r.ReadString(),
                };
                // v1 has neither field — its C# defaults (Guid.Empty / 0.0) apply until overwritten
                // below by the migration step for anything older than the current version.
                if (version >= 2) entry.EntryId = new Guid(r.ReadBytes(16));
                if (version >= 3) entry.InGameTimestamp = r.ReadDouble();
                store._entries.Add(entry);
            }

            if (version < Version) ApplyV2ToV3Migrations(store._entries);
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            // Malformed — return whatever was read so far.
        }

        return store;
    }

    /// <summary>v1/v2 → v3 migration: neither prior version recorded a sortable timestamp, so every
    /// entry read from one is assigned a synthetic timestamp from a strictly-increasing negative
    /// sequence in its existing list order — preserving that order exactly (a plain numeric sort)
    /// while guaranteeing every migrated entry sorts before any timestamp a freshly-recorded entry
    /// can carry (<c>Calendar.TotalDays</c> is never negative). A v1 payload also has no per-entry
    /// <c>EntryId</c>, but that already defaults to <see cref="Guid.Empty"/> — nothing to transform
    /// there.</summary>
    private static void ApplyV2ToV3Migrations(List<HistoryEntry> entries)
    {
        for (int i = 0; i < entries.Count; i++)
            entries[i].InGameTimestamp = i - entries.Count;
    }
}
