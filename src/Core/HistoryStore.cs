using System.Text;

namespace Scribe.Core;

/// <summary>
/// Append-only chronicle for a Notebook item. Stores events recorded automatically by the
/// game (crafted, picked up, deaths, PvP kills, boss kills, temporal storms) and up to
/// <see cref="MaxManual"/> player-authored entries.
///
/// Sliding-window caps drop the oldest entry of a given kind when the cap is reached (so the
/// 10 most recent deaths always survive). Manual entries are rejected when the cap is full.
/// PickedUp and Crafted use identity deduplication instead.
///
/// Serialized format (SHST v1, little-endian via <see cref="BinaryWriter"/>):
///   [4 bytes magic "SHST"][1 byte version][int entryCount]
///   [per entry: byte kind, string actorName, string detail, string inGameDate, bool isManual]
///
/// See docs/CODEC-MIGRATION.md for the version-bump pattern.
/// </summary>
public sealed class HistoryStore
{
    private static readonly byte[] Magic = "SHST"u8.ToArray();
    private const byte Version      = 1;
    private const byte PriorVersion = 1; // no prior version yet; scaffold per ScribePinCodec pattern

    public const int MaxDeaths    = 10;
    public const int MaxStorms    = 5;
    public const int MaxPvpKills  = 10;
    public const int MaxBossKills = 10;

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
    /// <item><b>Death / PvpKill / BossKill / TemporalStorm</b> — sliding window: oldest of that kind
    /// is dropped when the cap is reached, then the new entry is appended.</item>
    /// <item><b>Manual</b> — rejected (returns false) when <see cref="MaxManual"/> is already reached.</item>
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
                store._entries.Add(new HistoryEntry
                {
                    Kind       = (HistoryEventKind)r.ReadByte(),
                    ActorName  = r.ReadString(),
                    Detail     = r.ReadString(),
                    InGameDate = r.ReadString(),
                });
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            // Malformed — return whatever was read so far.
        }

        return store;

        // Migration stub — no-op while PriorVersion == Version.
        // When a new version adds fields, add ApplyV1ToV2Migrations(version, ref entries) here.
        // See docs/CODEC-MIGRATION.md.
    }
}
