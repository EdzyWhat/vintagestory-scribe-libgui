using System.Text;

namespace Scribe.Core;

/// <summary>
/// Rolling visitor log for a Scribe block. Append-only from the game's perspective: entries are
/// added by the Mod layer (one per player per in-game day), capped at <see cref="MaxEntries"/>.
/// Core holds only plain-string data; the Mod layer supplies the formatted date-only string.
///
/// Serialized format (SGBK, versioned binary):
///   [4 bytes magic "SGBK"][1 byte version][int entryCount]
///   [per entry: string playerName, string inGameDate, string note]
/// </summary>
public sealed class GuestbookStore
{
    private static readonly byte[] Magic = "SGBK"u8.ToArray();
    private const byte Version = 1;

    public const int MaxEntries  = 100;
    public const int MaxNoteLength = 140;

    /// <summary>Soft per-player cap: once a single player has more than this many entries, adding another
    /// prunes that player's OLDEST note-less entry to keep their log readable. An entry that carries a note
    /// is never pruned, and the day just added is never pruned — so a player who leaves a note on every
    /// visit can still accumulate more than this many entries. Distinct from the hard, whole-store
    /// <see cref="MaxEntries"/> ring buffer.</summary>
    public const int SoftMaxEntriesPerPlayer = 10;

    private readonly List<GuestbookEntry> _entries = new();

    /// <summary>All entries in insertion order (oldest first). The display layer reverses for newest-first.</summary>
    public IReadOnlyList<GuestbookEntry> Entries => _entries;

    /// <summary>Adds a new entry if no existing entry has the same <c>(PlayerName, InGameDate)</c>.
    /// When the hard whole-store <see cref="MaxEntries"/> cap is reached the oldest entry is dropped to
    /// make room. Separately, once the adding player would exceed the soft
    /// <see cref="SoftMaxEntriesPerPlayer"/> cap, that player's OLDEST note-less entry is pruned to keep
    /// their log readable — entries carrying a note are never pruned (so a player who leaves a note every
    /// visit keeps all of them), and the just-added entry is never the one pruned. Returns true if an
    /// entry was added.</summary>
    public bool TryAddEntry(string playerName, string inGameDate)
    {
        if (_entries.Any(e => e.PlayerName == playerName && e.InGameDate == inGameDate))
            return false;

        if (_entries.Count >= MaxEntries)
            _entries.RemoveAt(0);

        var added = new GuestbookEntry { PlayerName = playerName, InGameDate = inGameDate };
        _entries.Add(added);

        // Soft per-player pruning: if this player now has more than SoftMaxEntriesPerPlayer entries,
        // drop their oldest note-less one. Skip the entry we just added (it's empty but must survive),
        // and never touch an entry with note text — so if every other entry has a note we keep >cap.
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

    /// <summary>Updates the note on the specific entry matching BOTH <paramref name="playerName"/> and
    /// <paramref name="inGameDate"/> — the natural key <see cref="TryAddEntry"/> guarantees is unique per
    /// entry (one per player per in-game day). A player may have several entries (one per day visited), so
    /// the date discriminator is required to reach the intended day's note rather than collapsing every
    /// edit onto the player's first entry. Note is clamped to <see cref="MaxNoteLength"/>. Returns false if
    /// no entry matches or the note is unchanged.</summary>
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
            if (r.ReadByte() != Version) return store;

            int count = r.ReadInt32();
            if (count < 0 || count > MaxEntries) return store;

            for (int i = 0; i < count; i++)
            {
                store._entries.Add(new GuestbookEntry
                {
                    PlayerName = r.ReadString(),
                    InGameDate = r.ReadString(),
                    Note       = r.ReadString(),
                });
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            // Malformed — return whatever was read so far (or empty if nothing).
        }

        return store;
    }
}
