using System.Text;

namespace Scribe.Core;

/// <summary>
/// Server-side queue of <see cref="HistoryEntry"/> records that could not be written immediately
/// because their document was evicted from its holder's inventory during the same event dispatch
/// (only the Death branch of <c>OnEntityDeath</c> enqueues here). Keyed by the target document's
/// <c>ScribeDocument.DocId</c>, never by player: a queued entry is written into that exact document
/// the next time it is seen in a live carried slot.
///
/// No pruning: entries for a document never seen again persist, an accepted cost for a rare path.
/// A count cap or age expiry would add bookkeeping for little gain (a small, unbounded set of tiny entries);
/// revisit if save growth shows otherwise.
/// The Mod layer's <c>OnHistoryScanTick</c> calls <see cref="TakeAll"/> and owns persistence,
/// mirroring <see cref="ScribePlayerLocationStore"/>.
///
/// Accepted-version window (progressive reads — see docs/CODEC-MIGRATION.md):
///   Current : v2 — per-entry layout matches SHST v4 (schema + live fact fields)
///   Min     : v1 — per-entry layout matches SHST v3 (baked sentences, no fact fields)
///   Older / newer : rejected (fail-safe empty load)
///
/// Serialized format (SPHS v2, little-endian via <see cref="BinaryWriter"/>):
///   [4 bytes magic "SPHS"][1 byte version][int docCount]
///   [per document: 16 bytes docId, int entryCount, then that many entries via
///    <see cref="HistoryEntryCodec"/>]
/// </summary>
public sealed class PendingHistoryStore
{
    private readonly Dictionary<Guid, List<HistoryEntry>> _pending = new();

    /// <summary>Queues <paramref name="entry"/> against <paramref name="docId"/>.</summary>
    public void Enqueue(Guid docId, HistoryEntry entry)
    {
        if (!_pending.TryGetValue(docId, out var list))
        {
            list = new List<HistoryEntry>();
            _pending[docId] = list;
        }
        list.Add(entry);
    }

    /// <summary>Removes and returns every entry queued against <paramref name="docId"/> (empty if none).
    /// The caller must write them into the document's <see cref="HistoryStore"/> immediately.</summary>
    public List<HistoryEntry> TakeAll(Guid docId)
    {
        if (!_pending.TryGetValue(docId, out var list)) return new List<HistoryEntry>();
        _pending.Remove(docId);
        return list;
    }

    /// <summary>Number of documents with queued entries (test/diagnostic).</summary>
    public int PendingDocumentCount => _pending.Count;

    // ---------------- Persistence ----------------

    private static readonly byte[] Magic = "SPHS"u8.ToArray();
    private const byte Version    = 2;
    private const byte MinVersion = 1;

    public byte[] SerializeStore()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(_pending.Count);
            foreach (var (docId, entries) in _pending)
            {
                w.Write(docId.ToByteArray());
                w.Write(entries.Count);
                foreach (var e in entries)
                    HistoryEntryCodec.WriteEntry(w, e);
            }
        }
        return ms.ToArray();
    }

    /// <summary>Replaces the store from the persisted blob on world load; a null/malformed blob leaves
    /// it empty rather than throwing.</summary>
    public void LoadFrom(byte[]? bytes)
    {
        _pending.Clear();
        if (bytes is null) return;

        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var r  = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            var magic = r.ReadBytes(Magic.Length);
            if (!magic.AsSpan().SequenceEqual(Magic)) return;
            byte version = r.ReadByte();
            if (version < MinVersion || version > Version) return;

            // SPHS v1 stored the SHST-v3 field set (entryId + timestamp, no facts); v2 matches SHST v4.
            byte entryLayoutVersion = version >= 2 ? (byte)4 : (byte)3;

            int docCount = r.ReadInt32();
            if (docCount < 0 || docCount > bytes.Length) return;

            for (int i = 0; i < docCount; i++)
            {
                var docId = new Guid(r.ReadBytes(16));
                int entryCount = r.ReadInt32();
                if (entryCount < 0 || entryCount > bytes.Length) return;

                var list = new List<HistoryEntry>(entryCount);
                for (int j = 0; j < entryCount; j++)
                    list.Add(HistoryEntryCodec.ReadEntry(r, entryLayoutVersion));
                if (list.Count > 0) _pending[docId] = list;
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            // Malformed: leave the store empty.
            _pending.Clear();
        }
    }
}
