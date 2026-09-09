using System.Text;

namespace Scribe.Core;

/// <summary>
/// Server-side queue of <see cref="HistoryEntry"/> records that could not be written immediately
/// because the document they belong to was evicted from its holder's inventory by something else
/// during the same event dispatch (see fix-death-history-inventory-race design.md — today, only the
/// Death branch of <c>OnEntityDeath</c> enqueues here). Keyed by the target document's own
/// <c>ScribeDocument.DocId</c>, never by player: a queued entry is written into the exact document
/// it was queued against the next time that document is observed in a live carried slot, regardless
/// of who is carrying it by then — never redirected to a different document.
///
/// No pruning: an entry for a document that is genuinely never seen again persists indefinitely
/// (explicit decision — see design.md's "Pending store: no pruning").
///
/// Game-agnostic (pure BCL) so it's unit-testable without a game install; the Mod layer's
/// <c>OnHistoryScanTick</c> calls <see cref="TakeAll"/> and owns this store's savegame persistence,
/// mirroring <see cref="ScribePlayerLocationStore"/>'s pattern.
/// </summary>
public sealed class PendingHistoryStore
{
    private readonly Dictionary<Guid, List<HistoryEntry>> _pending = new();

    /// <summary>Queues <paramref name="entry"/> against <paramref name="docId"/>, appending to any
    /// entries already queued for that document.</summary>
    public void Enqueue(Guid docId, HistoryEntry entry)
    {
        if (!_pending.TryGetValue(docId, out var list))
        {
            list = new List<HistoryEntry>();
            _pending[docId] = list;
        }
        list.Add(entry);
    }

    /// <summary>Removes and returns every entry queued against <paramref name="docId"/> (empty if
    /// none). The caller is expected to write these into the document's own <see cref="HistoryStore"/>
    /// immediately — nothing else re-reads a taken entry.</summary>
    public List<HistoryEntry> TakeAll(Guid docId)
    {
        if (!_pending.TryGetValue(docId, out var list)) return new List<HistoryEntry>();
        _pending.Remove(docId);
        return list;
    }

    /// <summary>Number of distinct documents with at least one queued entry. Test/diagnostic use.</summary>
    public int PendingDocumentCount => _pending.Count;

    // ---------------- Persistence ----------------

    private static readonly byte[] Magic = "SPHS"u8.ToArray();
    private const byte Version = 1;

    /// <summary>Serializes every queued entry, for every document, for the savegame blob.</summary>
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
                {
                    w.Write((byte)e.Kind);
                    w.Write(e.ActorName);
                    w.Write(e.Detail);
                    w.Write(e.InGameDate);
                    w.Write(e.EntryId.ToByteArray());
                    w.Write(e.InGameTimestamp);
                }
            }
        }
        return ms.ToArray();
    }

    /// <summary>Replaces the in-memory store from the persisted blob (called on world load). A
    /// null/malformed blob leaves the store empty rather than throwing — a corrupt save degrades to
    /// "no queued entries" instead of crashing the load.</summary>
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
            if (version != Version) return;

            int docCount = r.ReadInt32();
            if (docCount < 0 || docCount > bytes.Length) return;

            for (int i = 0; i < docCount; i++)
            {
                var docId = new Guid(r.ReadBytes(16));
                int entryCount = r.ReadInt32();
                if (entryCount < 0 || entryCount > bytes.Length) return;

                var list = new List<HistoryEntry>(entryCount);
                for (int j = 0; j < entryCount; j++)
                {
                    list.Add(new HistoryEntry
                    {
                        Kind            = (HistoryEventKind)r.ReadByte(),
                        ActorName       = r.ReadString(),
                        Detail          = r.ReadString(),
                        InGameDate      = r.ReadString(),
                        EntryId         = new Guid(r.ReadBytes(16)),
                        InGameTimestamp = r.ReadDouble(),
                    });
                }
                if (list.Count > 0) _pending[docId] = list;
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            // Malformed — leave the store empty.
            _pending.Clear();
        }
    }
}
