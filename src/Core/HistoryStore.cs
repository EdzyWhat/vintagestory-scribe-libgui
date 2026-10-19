using System.Text;

namespace Scribe.Core;

/// <summary>
/// Append-only chronicle for a Notebook item: automatically recorded game events plus up to
/// <see cref="MaxManual"/> player-authored entries. Per-kind sliding-window caps drop the oldest entry
/// of a kind; PickedUp and Crafted use identity deduplication instead.
///
/// Accepted-version window (progressive reads — see docs/CODEC-MIGRATION.md):
///   Current : v4 — adds per-entry schema + live fact fields (subject/other/ref/ref2/flavorSeed)
///   v3      : adds a per-entry sortable <c>InGameTimestamp</c> (8-byte double)
///   v2      : adds a per-entry <c>EntryId</c> (16-byte Guid), used only by Manual entries
///   Min     : v1 — no EntryId, no InGameTimestamp, no live facts
///   Older   : rejected
///
/// v1/v2 payloads get synthetic timestamps (<see cref="ApplyV2ToV3Migrations"/>); v1–v3 entries are
/// marked <see cref="HistorySchema.Baked"/> (<see cref="ApplyV3ToV4Migrations"/>).
///
/// Serialized format (SHST v4, little-endian via <see cref="BinaryWriter"/>):
///   [4 bytes magic "SHST"][1 byte version][int entryCount]
///   [per entry: byte kind, string actorName, string detail, string inGameDate, 16 bytes entryId,
///    8 bytes inGameTimestamp, byte schema, string subjectName, string otherName, string refCode,
///    string refCode2, int flavorSeed]
/// </summary>
public sealed class HistoryStore
{
    private static readonly byte[] Magic = "SHST"u8.ToArray();
    private const byte Version    = 4;
    private const byte MinVersion = 1;

    public const int MaxDeaths    = 30;
    public const int MaxStorms    = 10;
    public const int MaxPvpKills  = 30;
    public const int MaxBossKills = 20;
    public const int MaxManual    = 30;

    /// <summary>Hard allocation guard: reject any payload larger than this.</summary>
    public const int MaxHistoryBytes = 64 * 1024;

    private readonly List<HistoryEntry> _entries = new();

    /// <summary>All entries, oldest first by <see cref="HistoryEntry.InGameTimestamp"/> (ties by write order).</summary>
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

    /// <summary>Inserts before the first entry with a strictly greater timestamp, so equal timestamps
    /// keep write order.</summary>
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
        // _entries is sorted by timestamp, so the first match is the oldest of this kind.
        int idx = _entries.FindIndex(e => e.Kind == kind);
        if (idx >= 0) _entries.RemoveAt(idx);
    }

    /// <summary>Updates a Manual entry's text, addressed by <see cref="HistoryEntry.EntryId"/> and
    /// authorized by <paramref name="authorName"/> matching the entry's <see cref="HistoryEntry.ActorName"/>
    /// (the caller must pass the requesting player's real name, not a client-claimed one). Text is clamped to
    /// <see cref="Scribe.Core.ScribeDocumentCodec.MaxTaskTextLength"/>. Returns false on an unknown id,
    /// non-Manual entry, author mismatch, or unchanged value; editing to empty text keeps the entry.</summary>
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

    /// <summary>Removes a Manual entry, authorized like <see cref="TrySetManualEntryText"/>; returns false
    /// on an unknown id, non-Manual entry, or author mismatch.</summary>
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
                HistoryEntryCodec.WriteEntry(w, e);
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
                store._entries.Add(HistoryEntryCodec.ReadEntry(r, version));

            // Version-gated: a v3 payload already has real timestamps and must not be rewritten.
            if (version < 3) ApplyV2ToV3Migrations(store._entries);
            if (version < 4) ApplyV3ToV4Migrations(store._entries);
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            // Malformed: keep whatever was read so far.
        }

        return store;
    }

    /// <summary>v1/v2 → v3: assigns each entry a strictly-increasing negative timestamp in list order,
    /// preserving order while sorting before any real timestamp (<c>Calendar.TotalDays</c> is never negative).</summary>
    private static void ApplyV2ToV3Migrations(List<HistoryEntry> entries)
    {
        for (int i = 0; i < entries.Count; i++)
            entries[i].InGameTimestamp = i - entries.Count;
    }

    /// <summary>v1–v3 → v4: marks every entry <see cref="HistorySchema.Baked"/>; live rows exist only in v4+.</summary>
    private static void ApplyV3ToV4Migrations(List<HistoryEntry> entries)
    {
        foreach (var e in entries)
            e.Schema = HistorySchema.Baked;
    }
}
