using System.Text;

namespace Scribe.Core;

/// <summary>
/// Serializes a <see cref="ScribeDocument"/> to a byte array and back. The same bytes are
/// used for both world persistence and network sync, so the round-trip is exact and any
/// malformed input fails safely (returns false) rather than throwing.
///
/// Format (little-endian via <see cref="BinaryWriter"/>):
///   [4 bytes magic "SCRB"][1 byte version][int blockCount]
///   [per block: byte kind, bool done, int depth, bool pinned, bool hasAssignedToUid,
///    string assignedToUid (only if hasAssignedToUid), string text]
/// A hand-rolled format keeps Core free of any external dependency. The version byte lets
/// us evolve the format later while still reading older saves.
/// </summary>
public static class ScribeDocumentCodec
{
    private static readonly byte[] Magic = "SCRB"u8.ToArray();
    private const byte Version = 3; // v1 was flat tasks + a single note; v2 is ordered blocks; v3 adds Pinned/AssignedToUid.

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

    /// <summary>Hard upper bound on a single block's text length, in characters. See <see cref="MaxBlocks"/>.</summary>
    public const int MaxTextLength = 10_000;

    public static byte[] Serialize(ScribeDocument doc)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(doc.Blocks.Count);
            foreach (var block in doc.Blocks)
            {
                w.Write((byte)block.Kind);
                w.Write(block.Done);
                w.Write(block.Depth);
                w.Write(block.Pinned);
                w.Write(block.AssignedToUid is not null);
                if (block.AssignedToUid is not null) w.Write(block.AssignedToUid);
                w.Write(block.Text);
            }
        }
        return ms.ToArray();
    }

    public static bool TryDeserialize(byte[]? bytes, out ScribeDocument? document)
    {
        document = null;
        if (bytes is null || bytes.Length < Magic.Length + 1) return false;

        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            var magic = r.ReadBytes(Magic.Length);
            if (!magic.AsSpan().SequenceEqual(Magic)) return false;

            byte version = r.ReadByte();
            if (version != Version) return false;

            int blockCount = r.ReadInt32();
            // Reject a negative count, a count that can't physically fit in the buffer (a tiny
            // payload claiming billions of blocks — an allocation guard), or one over the hard
            // MaxBlocks cap on document size.
            if (blockCount < 0 || blockCount > bytes.Length || blockCount > MaxBlocks) return false;

            var blocks = new List<ScribeBlock>(blockCount);
            for (int i = 0; i < blockCount; i++)
            {
                var kind = (ScribeBlockKind)r.ReadByte();
                bool done = r.ReadBoolean();
                int depth = r.ReadInt32();
                bool pinned = r.ReadBoolean();
                bool hasAssignedToUid = r.ReadBoolean();
                string? assignedToUid = hasAssignedToUid ? r.ReadString() : null;
                string text = r.ReadString();
                if (text.Length > MaxTextLength) return false; // per-block text-length cap
                blocks.Add(new ScribeBlock(kind, text, done, depth, pinned, assignedToUid));
            }

            var doc = new ScribeDocument();
            doc.SetBlocks(blocks);
            document = doc;
            return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            // Truncated or malformed input — fail safely.
            document = null;
            return false;
        }
    }
}
