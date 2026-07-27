using System.Text;

namespace Scribe.Core;

/// <summary>
/// Serializes a <see cref="ScribeDocument"/> to a byte array and back. The same bytes are
/// used for both world persistence and network sync, so the round-trip is exact and any
/// malformed input fails safely (returns false) rather than throwing.
///
/// Current format (v4, little-endian via <see cref="BinaryWriter"/>):
///   [4 bytes magic "SCRB"][1 byte version][16 bytes DocId][int blockCount]
///   [per block: 16 bytes TaskId, byte kind, bool done, int depth, bool hasAssignedToUid,
///    string assignedToUid (only if hasAssignedToUid), string text]
///
/// APPEND-ONLY VERSION DISCIPLINE: <see cref="Version"/> is a single global counter and new
/// fields append in version order (never interleave; never two "v4"s). See
/// docs/specs/README.md convention #1. When adding v5, append its fields after v4's layout and
/// extend the reader with a new branch — do not reorder existing fields.
///
/// Field history:
///   v1 — flat tasks + a single note (pre-ordered-blocks).
///   v2 — ordered blocks: [kind, done, depth, text].
///   v3 — appended a per-block `pinned` bool and optional `assignedToUid`:
///        [kind, done, depth, pinned, hasAssignedToUid, assignedToUid?, text].
///   v4 — added a 16-byte document id (after the version byte) and a 16-byte per-block id
///        (first field of each block), and DROPPED the per-block `pinned` bool (pinning moved
///        to a per-player store). The reader still accepts v3 (see <see cref="TryDeserialize"/>).
///
/// A hand-rolled format keeps Core free of any external dependency. The version byte lets
/// us evolve the format while still reading the immediately prior save layout.
/// </summary>
public static class ScribeDocumentCodec
{
    private static readonly byte[] Magic = "SCRB"u8.ToArray();
    private const byte Version = 4; // see the "Field history" above; v4 adds DocId/TaskId, drops pinned.

    /// <summary>The immediately prior format version the reader still accepts (with generated ids).</summary>
    private const byte PriorVersion = 3;

    /// <summary>
    /// Hard upper bound on the number of blocks a single document may hold. A document is edited by
    /// whoever holds the lectern's edit lock and submitted to the server as raw bytes, so it is
    /// trusted-but-client input: the server persists and re-syncs whatever deserializes. These caps
    /// bound that trust so a malformed or hostile payload can't bloat a block entity's saved tree
    /// attributes (and every client's re-sync) without limit. Enforced in <see cref="TryDeserialize"/>,
    /// which fails the whole payload (returns false) when either is exceeded — the codec is the single
    /// chokepoint for both the network path and world-persistence load, so one check covers both.
    /// Generous relative to any realistic hand-authored checklist.
    /// </summary>
    public const int MaxBlocks = 1000;

    /// <summary>Hard upper bound on a single block's text length, in characters. See <see cref="MaxBlocks"/>.
    /// Applies to freeform Text/note sections; Task blocks are held to the tighter
    /// <see cref="MaxTaskTextLength"/> and CLIPPED (not rejected) on read.</summary>
    public const int MaxTextLength = 10_000;

    /// <summary>Soft length limit for a checkbox Task's text, in characters. Unlike <see cref="MaxTextLength"/>
    /// (a hard reject bound for freeform notes), an over-long Task is CLIPPED to this length on read rather
    /// than rejecting the whole edit — so a paste or a runaway task can never silently drop an entire
    /// document (the pre-2026-07-26 behavior where an oversized edit was refused with no feedback). The
    /// editor field also enforces this as a maxlength so the clip is rarely reached; the codec clip is the
    /// server-authoritative backstop. Tasks are meant to be short one-liners; freeform prose belongs in a
    /// Text section, which keeps the larger cap.</summary>
    public const int MaxTaskTextLength = 1000;

    public static byte[] Serialize(ScribeDocument doc)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(doc.DocId.ToByteArray()); // 16 bytes; raw not string to keep the format compact
            w.Write(doc.Blocks.Count);
            foreach (var block in doc.Blocks)
            {
                w.Write(block.TaskId.ToByteArray()); // 16 bytes, first per-block field
                w.Write((byte)block.Kind);
                w.Write(block.Done);
                w.Write(block.Depth);
                w.Write(block.AssignedToUid is not null);
                if (block.AssignedToUid is not null) w.Write(block.AssignedToUid);
                w.Write(block.Text);
            }
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes a document. Accepts the current format (v4) and the immediately prior one
    /// (v3); any other version fails safely. Signature-stable for the many callers that don't need
    /// the migration list — routes through the three-arg overload and discards it.
    /// </summary>
    public static bool TryDeserialize(byte[]? bytes, out ScribeDocument? document)
        => TryDeserialize(bytes, out document, out _);

    /// <summary>
    /// Deserializes a document and, for a prior-version (v3) payload, surfaces the identifiers of
    /// the tasks that carried the now-removed per-block `pinned` flag, so a caller can migrate them
    /// into the per-player pin store. For a current-version (v4) payload the document carries its
    /// own persisted ids and <paramref name="legacyPinnedTaskIds"/> is empty (v4 has no pin flag).
    /// The ids surfaced for a v3 payload are the freshly-generated <see cref="ScribeBlock.TaskId"/>s
    /// of the just-built document, so they match the ids the caller will see on <paramref name="document"/>.
    /// </summary>
    public static bool TryDeserialize(byte[]? bytes, out ScribeDocument? document, out IReadOnlyList<Guid> legacyPinnedTaskIds)
    {
        document = null;
        legacyPinnedTaskIds = Array.Empty<Guid>();
        if (bytes is null || bytes.Length < Magic.Length + 1) return false;

        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            var magic = r.ReadBytes(Magic.Length);
            if (!magic.AsSpan().SequenceEqual(Magic)) return false;

            byte version = r.ReadByte();
            if (version != Version && version != PriorVersion) return false;
            bool isCurrent = version == Version;

            // v4 carries a persisted DocId in the header; v3 has none (generate a fresh one).
            Guid docId = isCurrent ? new Guid(ReadExactly(r, 16)) : Guid.NewGuid();

            int blockCount = r.ReadInt32();
            // Reject a negative count, a count that can't physically fit in the buffer (a tiny
            // payload claiming billions of blocks — an allocation guard), or one over the hard
            // MaxBlocks cap on document size.
            if (blockCount < 0 || blockCount > bytes.Length || blockCount > MaxBlocks) return false;

            var blocks = new List<ScribeBlock>(blockCount);
            List<Guid>? legacyPinned = null; // lazily allocated; only v3 with pinned tasks needs it
            for (int i = 0; i < blockCount; i++)
            {
                // v4 reads a persisted TaskId first; v3 has none (a fresh id is generated below).
                Guid? taskId = isCurrent ? new Guid(ReadExactly(r, 16)) : null;

                var kind = (ScribeBlockKind)r.ReadByte();
                bool done = r.ReadBoolean();
                int depth = r.ReadInt32();
                // v3 carried a per-block `pinned` bool here; v4 dropped it. Read-and-discard on v3,
                // but remember which tasks were pinned so the caller can migrate them.
                bool pinned = !isCurrent && r.ReadBoolean();
                bool hasAssignedToUid = r.ReadBoolean();
                string? assignedToUid = hasAssignedToUid ? r.ReadString() : null;
                string text = r.ReadString();
                // Freeform Text/note sections: reject the whole payload past the hard cap (an
                // allocation/abuse guard). Task blocks: CLIP to the soft task cap instead of rejecting,
                // so an over-long task can never drop the entire document silently.
                if (kind == ScribeBlockKind.Task)
                {
                    if (text.Length > MaxTaskTextLength) text = text.Substring(0, MaxTaskTextLength);
                }
                else if (text.Length > MaxTextLength)
                {
                    return false;
                }

                var block = new ScribeBlock(kind, text, done, depth, assignedToUid, taskId);
                blocks.Add(block);
                if (pinned)
                {
                    (legacyPinned ??= new List<Guid>()).Add(block.TaskId);
                }
            }

            var doc = new ScribeDocument();
            doc.SetDocId(docId);
            doc.SetBlocks(blocks);
            document = doc;
            if (legacyPinned is not null) legacyPinnedTaskIds = legacyPinned;
            return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            // Truncated or malformed input — fail safely.
            document = null;
            legacyPinnedTaskIds = Array.Empty<Guid>();
            return false;
        }
    }

    /// <summary>
    /// True when <paramref name="bytes"/> are a readable document written in a format OLDER than the
    /// current version (today: v3). Such a document deserializes fine but had its ids freshly
    /// generated on read, so a caller that wants those ids to stick must re-save it in the current
    /// format (v4). Returns false for current-version bytes, and for null/malformed/unsupported bytes
    /// (nothing to upgrade). Lets the block entity decide to MarkDirty-on-load without reaching into
    /// the format internals.
    /// </summary>
    public static bool IsPriorVersion(byte[]? bytes)
    {
        if (bytes is null || bytes.Length < Magic.Length + 1) return false;
        for (int i = 0; i < Magic.Length; i++)
        {
            if (bytes[i] != Magic[i]) return false;
        }
        return bytes[Magic.Length] == PriorVersion;
    }

    /// <summary>Reads exactly <paramref name="count"/> bytes or throws <see cref="EndOfStreamException"/>
    /// (which the caller catches as a malformed-input failure). <see cref="BinaryReader.ReadBytes"/>
    /// can return fewer bytes than requested at end-of-stream without throwing, which would let a
    /// truncated id slip through as a wrong-but-valid Guid — this guards that.</summary>
    private static byte[] ReadExactly(BinaryReader r, int count)
    {
        var buffer = r.ReadBytes(count);
        if (buffer.Length != count) throw new EndOfStreamException();
        return buffer;
    }
}
