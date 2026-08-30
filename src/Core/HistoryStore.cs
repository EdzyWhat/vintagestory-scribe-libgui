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
/// Accepted-version window:
///   Current : v2 — adds a per-entry <c>EntryId</c> (16-byte Guid), used only by Manual entries
///   Prior   : v1 — migrated by <see cref="ApplyV1ToV2Migrations"/> (fills EntryId = Guid.Empty)
///   Older   : rejected
///
/// Serialized format (SHST v2, little-endian via <see cref="BinaryWriter"/>):
///   [4 bytes magic "SHST"][1 byte version][int entryCount]
///   [per entry: byte kind, string actorName, string detail, string inGameDate, 16 bytes entryId]
///
/// See docs/CODEC-MIGRATION.md for the version-bump pattern.
/// </summary>
public sealed class HistoryStore
{
    private static readonly byte[] Magic = "SHST"u8.ToArray();
    private const byte Version      = 2;
    private const byte PriorVersion = 1;

    public const int MaxDeaths    = 30;
    public const int MaxStorms    = 10;
    public const int MaxPvpKills  = 30;
    public const int MaxBossKills = 20;
    public const int MaxManual    = 30;

    /// <summary>Hard allocation guard: reject any payload larger than this.</summary>
    public const int MaxHistoryBytes = 64 * 1024;

    private readonly List<HistoryEntry> _entries = new();

    /// <summary>All entries in insertion order (oldest first). The display layer reverses for newest-first.</summary>
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

        _entries.Add(entry);
        return true;
    }

    private void DropOldestOfKindIfAtCap(HistoryEventKind kind, int cap)
    {
        int count = _entries.Count(e => e.Kind == kind);
        if (count < cap) return;
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
            if (version != Version && version != PriorVersion) return store;

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
                if (version == Version) entry.EntryId = new Guid(r.ReadBytes(16));
                store._entries.Add(entry);
            }

            if (version == PriorVersion) ApplyV1ToV2Migrations(store._entries);
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            // Malformed — return whatever was read so far.
        }

        return store;
    }

    /// <summary>v1 → v2 migration: v1 had no per-entry <c>EntryId</c> field, so every entry read from
    /// a v1 payload already defaulted to <see cref="Guid.Empty"/> (the property's own default) —
    /// nothing to actually transform. v1 also predates <see cref="HistoryEventKind.Manual"/>, so no
    /// v1 entry can be one anyway. Kept as an explicit named step per docs/CODEC-MIGRATION.md rather
    /// than silently relying on the default, so a future v3 migration has a clear precedent to follow.</summary>
    private static void ApplyV1ToV2Migrations(List<HistoryEntry> entries)
    {
        // No-op: HistoryEntry.EntryId already defaults to Guid.Empty.
    }
}
