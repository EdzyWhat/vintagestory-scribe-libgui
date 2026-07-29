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

    private readonly List<GuestbookEntry> _entries = new();

    /// <summary>All entries in insertion order (oldest first). The display layer reverses for newest-first.</summary>
    public IReadOnlyList<GuestbookEntry> Entries => _entries;

    /// <summary>Adds a new entry if no existing entry has the same <c>(PlayerName, InGameDate)</c>.
    /// When the cap is reached the oldest entry is dropped to make room. Returns true if an entry
    /// was added.</summary>
    public bool TryAddEntry(string playerName, string inGameDate)
    {
        if (_entries.Any(e => e.PlayerName == playerName && e.InGameDate == inGameDate))
            return false;

        if (_entries.Count >= MaxEntries)
            _entries.RemoveAt(0);

        _entries.Add(new GuestbookEntry { PlayerName = playerName, InGameDate = inGameDate });
        return true;
    }

    /// <summary>Updates the note on the first entry matching <paramref name="playerName"/>. Note is
    /// clamped to <see cref="MaxNoteLength"/>. Returns false if no entry is found or the note is
    /// unchanged.</summary>
    public bool TrySetNote(string playerName, string note)
    {
        var entry = _entries.FirstOrDefault(e => e.PlayerName == playerName);
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
