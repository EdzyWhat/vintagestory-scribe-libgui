using System.Text;

namespace Scribe.Core;

/// <summary>
/// Rolling visitor log for a Scribe block: one entry per player per in-game day, capped at
/// <see cref="MaxEntries"/>. The Mod layer supplies the formatted date string and <c>Calendar.TotalDays</c>.
///
/// Accepted-version window (progressive reads — see docs/CODEC-MIGRATION.md):
///   Current : v2 — per entry: playerName, inGameDate, note, bool hasTimestamp, optional double timestamp
///   Min     : v1 — per entry: playerName, inGameDate, note (no timestamp; InGameTimestamp stays null)
///   Older / newer : rejected (fail-safe empty load)
///
/// Serialized format (SGBK, little-endian via <see cref="BinaryWriter"/>):
///   [4 bytes magic "SGBK"][1 byte version][int entryCount]
///   [per entry: string playerName, string inGameDate, string note,
///    then if version &gt;= 2: bool hasTimestamp, double inGameTimestamp when true]
/// </summary>
public sealed class GuestbookStore
{
    private static readonly byte[] Magic = "SGBK"u8.ToArray();
    private const byte Version = 2;
    private const byte MinVersion = 1;

    public const int MaxEntries  = 100;
    public const int MaxNoteLength = 140;

    /// <summary>Soft per-player cap: past it, adding an entry prunes that player's OLDEST note-less entry.
    /// Entries with a note and the just-added entry are never pruned, so the count can exceed this.
    /// Distinct from the whole-store <see cref="MaxEntries"/> cap.</summary>
    public const int SoftMaxEntriesPerPlayer = 10;

    private readonly List<GuestbookEntry> _entries = new();

    /// <summary>All entries, oldest first.</summary>
    public IReadOnlyList<GuestbookEntry> Entries => _entries;

    /// <summary>Adds an entry unless one with the same <c>(PlayerName, InGameDate)</c> exists. At
    /// <see cref="MaxEntries"/> the oldest entry is dropped; past <see cref="SoftMaxEntriesPerPlayer"/> the
    /// player's oldest note-less entry is pruned (never the one just added). Returns true if added.
    /// <paramref name="inGameTimestamp"/> is display-only and not part of dedup.</summary>
    public bool TryAddEntry(string playerName, string inGameDate, double? inGameTimestamp = null)
    {
        if (_entries.Any(e => e.PlayerName == playerName && e.InGameDate == inGameDate))
            return false;

        if (_entries.Count >= MaxEntries)
            _entries.RemoveAt(0);

        var added = new GuestbookEntry
        {
            PlayerName = playerName,
            InGameDate = inGameDate,
            InGameTimestamp = inGameTimestamp,
        };
        _entries.Add(added);

        // Skip the entry just added (it has no note yet but must survive); if every other entry has
        // a note, nothing is pruned.
        if (_entries.Count(e => e.PlayerName == playerName) > SoftMaxEntriesPerPlayer)
        {
            var prunable = _entries.FirstOrDefault(e =>
                e.PlayerName == playerName
                && !ReferenceEquals(e, added)
                && string.IsNullOrWhiteSpace(e.Note));
            if (prunable is not null)
                _entries.Remove(prunable);
        }

        return true;
    }

    /// <summary>Sets the note on the entry matching BOTH <paramref name="playerName"/> and
    /// <paramref name="inGameDate"/> (the unique key; a player has one entry per day). Clamped to
    /// <see cref="MaxNoteLength"/>. Returns false if no entry matches or the note is unchanged.</summary>
    public bool TrySetNote(string playerName, string inGameDate, string note)
    {
        var entry = _entries.FirstOrDefault(e => e.PlayerName == playerName && e.InGameDate == inGameDate);
        if (entry is null) return false;

        if (note.Length > MaxNoteLength) note = note[..MaxNoteLength];
        if (entry.Note == note) return false;

        entry.Note = note;
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
                w.Write(e.PlayerName);
                w.Write(e.InGameDate);
                w.Write(e.Note);
                bool hasTimestamp = e.InGameTimestamp.HasValue;
                w.Write(hasTimestamp);
                if (hasTimestamp) w.Write(e.InGameTimestamp!.Value);
            }
        }
        return ms.ToArray();
    }

    public static GuestbookStore Deserialize(byte[]? bytes)
    {
        var store = new GuestbookStore();
        if (bytes is null || bytes.Length < Magic.Length + 1) return store;

        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var r  = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            var magic = r.ReadBytes(Magic.Length);
            if (!magic.AsSpan().SequenceEqual(Magic)) return store;
            byte version = r.ReadByte();
            if (version < MinVersion || version > Version) return store;

            int count = r.ReadInt32();
            if (count < 0 || count > MaxEntries) return store;

            for (int i = 0; i < count; i++)
            {
                var entry = new GuestbookEntry
                {
                    PlayerName = r.ReadString(),
                    InGameDate = r.ReadString(),
                    Note       = r.ReadString(),
                };
                if (version >= 2)
                {
                    bool hasTimestamp = r.ReadBoolean();
                    if (hasTimestamp) entry.InGameTimestamp = r.ReadDouble();
                }
                store._entries.Add(entry);
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            // Malformed: keep whatever was read so far.
        }

        return store;
    }
}
