using System.Text;

namespace Scribe.Core;

/// <summary>
/// Serializes a <see cref="ScribeDocument"/> to bytes and back. The same bytes serve world persistence and
/// network sync; malformed input fails safely (returns false) rather than throwing.
///
/// Current format (v13, little-endian via <see cref="BinaryWriter"/>):
///   [4 bytes magic "SCRB"][1 byte version][16 bytes DocId][int blockCount]
///   [per block: 16 bytes TaskId, byte kind, bool done, int depth, bool hasAssignedToUid,
///    string assignedToUid (only if hasAssignedToUid), string text,
///    bool hasTargetItemCode, string targetItemCode (only if hasTargetItemCode),
///    int targetQuantity, int currentQuantity,
///    bool hasLinkTarget, string linkTarget (only if hasLinkTarget),
///    bool hasLinkLabel, string linkLabel (only if hasLinkLabel),
///    string recipeSignature (v8+; empty string when none),
///    bool hasAssignment, string assignerUid, byte state, string assignedDate, bool seen (v9+),
///    string targetPlayerUid (only when hasAssignment; v10+),
///    bool hasLinkDescription, string linkDescription (only if hasLinkDescription; v11+),
///    bool hasExtraInfo, string extraInfo (only if hasExtraInfo; v12+),
///    bool hasAssignedTimestamp, double assignedTimestamp (only if hasAssignedTimestamp; v13+)]
///   [string title]
///
/// Accepted-version window: progressive append-only reads of any version in
/// [<see cref="MinVersion"/>, <see cref="Version"/>]; anything else fails safe. Each version-group's fields
/// are read behind a <c>version &gt;=</c> gate, so an older blob stops before fields it never wrote, and
/// missing fields default via <see cref="ApplyPreV6Defaults"/>. v5 is the floor because it is the last
/// shipped layout still live in saves; see <see href="../docs/CODEC-MIGRATION.md">docs/CODEC-MIGRATION.md</see>.
///
/// APPEND-ONLY: <see cref="Version"/> is a single global counter; new fields append after the previous
/// version's layout behind a new <c>version &gt;= N</c> gate. Never reorder or interleave. See
/// docs/specs/README.md convention #1.
///
/// Per-version additions:
///   v4 document id and per-block id; v5 document title after the block list; v6 TargetItemCode,
///   TargetQuantity, CurrentQuantity, LinkTarget; v7 LinkLabel; v8 RecipeSignature (written for every
///   block, empty for non-Craft); v9 optional assignment state; v10 assignment TargetPlayerUid;
///   v11 LinkDescription; v12 ExtraInfo (external-mod hover string); v13 assigned timestamp (cannot sit
///   inside the v9 assignment sub-blob because v11/v12 already follow it; a missing timestamp is distinct
///   from Calendar.TotalDays == 0).
/// </summary>
public static class ScribeDocumentCodec
{
    private static readonly byte[] Magic = "SCRB"u8.ToArray();
    private const byte Version = 13;

    /// <summary>Oldest accepted format version: the last SHIPPED layout still live in player saves.</summary>
    private const byte MinVersion = 5;

    /// <summary>
    /// Upper bound on blocks in one document. Documents arrive as raw bytes from a client, so this caps
    /// what a malformed or hostile payload can bloat in a block entity's saved attributes and every client's
    /// re-sync. Enforced in <see cref="TryDeserialize"/>, the single chokepoint for both the network path
    /// and world-persistence load; exceeding it fails the whole payload.
    /// </summary>
    public const int MaxBlocks = 1000;

    /// <summary>Upper bound on a freeform Text/note block's length in characters. An over-long note is
    /// CLIPPED on read (the backstop Task blocks get via <see cref="MaxTaskTextLength"/>); the editor also
    /// enforces it live. Larger than the Task cap because notes hold prose.</summary>
    public const int MaxTextLength = 10_000;

    /// <summary>Soft length limit for a Task's text. An over-long Task is CLIPPED on read rather than
    /// rejecting the document, so a paste can never silently drop a whole document. The editor also enforces
    /// it live; the codec clip is the server-authoritative backstop.</summary>
    public const int MaxTaskTextLength = 1000;

    public static byte[] Serialize(ScribeDocument doc)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(doc.DocId.ToByteArray()); // raw 16 bytes, not a string, to keep the format compact
            w.Write(doc.Blocks.Count);
            foreach (var block in doc.Blocks)
            {
                w.Write(block.TaskId.ToByteArray());
                w.Write((byte)block.Kind);
                w.Write(block.Done);
                w.Write(block.Depth);
                // Legacy slot kept in the layout for v5-v8 readers; never populated by new assignments.
                w.Write(false);
                w.Write(block.Text);
                // v6: Tracker/Link fields.
                w.Write(block.TargetItemCode is not null);
                if (block.TargetItemCode is not null) w.Write(block.TargetItemCode);
                w.Write(block.TargetQuantity);
                w.Write(block.CurrentQuantity);
                w.Write(block.LinkTarget is not null);
                if (block.LinkTarget is not null) w.Write(block.LinkTarget);
                // v7: LinkLabel.
                w.Write(block.LinkLabel is not null);
                if (block.LinkLabel is not null) w.Write(block.LinkLabel);
                // v8: RecipeSignature; always written, so a plain string rather than a has/value pair.
                w.Write(block.RecipeSignature);
                // v9: optional assignment state.
                w.Write(block.Assignment is not null);
                if (block.Assignment is { } assignment)
                {
                    w.Write(assignment.AssignerUid);
                    w.Write((byte)assignment.State);
                    w.Write(assignment.AssignedDate);
                    w.Write(assignment.Seen);
                    // v10: recipient uid.
                    w.Write(assignment.TargetPlayerUid);
                }
                // v11: quest Link description.
                w.Write(block.LinkDescription is not null);
                if (block.LinkDescription is not null) w.Write(block.LinkDescription);
                // v12: external mod's ExtraInfo hover string.
                w.Write(block.ExtraInfo is not null);
                if (block.ExtraInfo is not null) w.Write(block.ExtraInfo);
                // v13: assigned timestamp, outside the v9 assignment sub-blob because v11/v12 follow it.
                bool hasAssignedTimestamp = block.Assignment?.AssignedTimestamp is not null;
                w.Write(hasAssignedTimestamp);
                if (hasAssignedTimestamp) w.Write(block.Assignment!.AssignedTimestamp!.Value);
            }
            w.Write(doc.Title); // v5+
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes a document, accepting any version in [MinVersion, Version] via progressive reads and
    /// failing safely otherwise. Routes through the three-arg overload and discards its out-param.
    /// </summary>
    public static bool TryDeserialize(byte[]? bytes, out ScribeDocument? document)
        => TryDeserialize(bytes, out document, out _);

    /// <summary>
    /// Deserializes a document. <paramref name="legacyPinnedTaskIds"/> is always empty (the pin-flag v3
    /// format is not accepted); kept for call-site compatibility.
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

            // Progressive append-only reads; see the class doc-comment's window note.
            byte version = r.ReadByte();
            if (version < MinVersion || version > Version) return false;

            Guid docId = new Guid(ReadExactly(r, 16));

            int blockCount = r.ReadInt32();
            // Reject a negative count, one that can't physically fit in the buffer (allocation guard),
            // or one over MaxBlocks.
            if (blockCount < 0 || blockCount > bytes.Length || blockCount > MaxBlocks) return false;

            var blocks = new List<ScribeBlock>(blockCount);
            for (int i = 0; i < blockCount; i++)
            {
                Guid taskId = new Guid(ReadExactly(r, 16));

                var kind = (ScribeBlockKind)r.ReadByte();
                bool done = r.ReadBoolean();
                int depth = r.ReadInt32();
                bool hasAssignedToUid = r.ReadBoolean();
                string? assignedToUid = hasAssignedToUid ? r.ReadString() : null;
                string text = r.ReadString();
                // Length backstop: CLIP an over-long row to its kind's cap rather than rejecting, so one
                // row can never silently drop the whole document.
                if (kind == ScribeBlockKind.Task)
                {
                    if (text.Length > MaxTaskTextLength) text = text.Substring(0, MaxTaskTextLength);
                }
                else if (text.Length > MaxTextLength)
                {
                    text = text.Substring(0, MaxTextLength);
                }

                // Seed Tracker/Link fields to defaults, then read each version-group behind a gate.
                ApplyPreV6Defaults(out string? targetItemCode, out int targetQuantity,
                    out int currentQuantity, out string? linkTarget, out string? linkLabel,
                    out string recipeSignature);
                if (version >= 6)
                {
                    bool hasTargetItemCode = r.ReadBoolean();
                    targetItemCode = hasTargetItemCode ? r.ReadString() : null;
                    targetQuantity = r.ReadInt32();
                    currentQuantity = r.ReadInt32();
                    bool hasLinkTarget = r.ReadBoolean();
                    linkTarget = hasLinkTarget ? r.ReadString() : null;
                }
                if (version >= 7)
                {
                    bool hasLinkLabel = r.ReadBoolean();
                    linkLabel = hasLinkLabel ? r.ReadString() : null;
                }
                if (version >= 8)
                {
                    recipeSignature = r.ReadString();
                }
                ScribeAssignment? assignment = null;
                if (version >= 9 && r.ReadBoolean())
                {
                    string assignerUid = r.ReadString();
                    var state = (ScribeAssignmentState)r.ReadByte();
                    string assignedDate = r.ReadString();
                    bool seen = r.ReadBoolean();
                    // v10: TargetPlayerUid. A v9 blob lacks it; v9 never shipped, so "" is not a migration gap.
                    string targetPlayerUid = version >= 10 ? r.ReadString() : "";
                    assignment = new ScribeAssignment(assignerUid, assignedDate, state, seen, targetPlayerUid);
                }
                if (version >= 9) assignedToUid = null;

                string? linkDescription = null;
                if (version >= 11 && r.ReadBoolean()) linkDescription = r.ReadString();

                string? extraInfo = null;
                if (version >= 12 && r.ReadBoolean()) extraInfo = r.ReadString();

                if (version >= 13)
                {
                    bool hasAssignedTimestamp = r.ReadBoolean();
                    if (hasAssignedTimestamp)
                    {
                        double ts = r.ReadDouble();
                        if (assignment is not null) assignment.AssignedTimestamp = ts;
                    }
                }

                // The block's setters clamp TargetQuantity (≥ 1), CurrentQuantity (≥ 0), and Depth ([0, 1]).
                blocks.Add(new ScribeBlock(kind, text, done, depth, assignedToUid, taskId,
                    targetItemCode, targetQuantity, currentQuantity, linkTarget, linkLabel, recipeSignature,
                    assignment, linkDescription, extraInfo));
            }

            // Every accepted version appends the title after the block list.
            string title = r.ReadString();
            if (string.IsNullOrWhiteSpace(title)) title = ScribeDocument.DefaultTitle;
            if (title.Length > ScribeDocument.MaxTitleLength) title = title[..ScribeDocument.MaxTitleLength];

            var doc = new ScribeDocument();
            doc.SetDocId(docId);
            doc.SetBlocks(blocks);
            doc.Title = title;
            document = doc;
            return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            // Truncated or malformed input.
            document = null;
            legacyPinnedTaskIds = Array.Empty<Guid>();
            return false;
        }
    }

    /// <summary>
    /// True when <paramref name="bytes"/> are a readable document in an OLDER but still-accepted version
    /// (in [<see cref="MinVersion"/>, <see cref="Version"/>)), so the block entity can re-save it to upgrade
    /// it. False for current-version, null, malformed or unsupported bytes.
    /// </summary>
    public static bool IsPriorVersion(byte[]? bytes)
    {
        if (bytes is null || bytes.Length < Magic.Length + 1) return false;
        for (int i = 0; i < Magic.Length; i++)
        {
            if (bytes[i] != Magic[i]) return false;
        }
        byte version = bytes[Magic.Length];
        return version >= MinVersion && version < Version;
    }

    /// <summary>
    /// Seeds the per-block Tracker/Link fields to defaults before the version-gated reads overwrite what a
    /// blob actually carried: null codes/label, <paramref name="targetQuantity"/> = 1 (the
    /// <see cref="ScribeBlock"/> ≥ 1 invariant), <paramref name="currentQuantity"/> = 0,
    /// <paramref name="recipeSignature"/> = "". Mirrors ScribePinCodec's <c>ApplyPreV2Defaults</c>. See
    /// docs/CODEC-MIGRATION.md.
    /// </summary>
    private static void ApplyPreV6Defaults(out string? targetItemCode, out int targetQuantity,
        out int currentQuantity, out string? linkTarget, out string? linkLabel, out string recipeSignature)
    {
        targetItemCode = null;
        targetQuantity = 1;
        currentQuantity = 0;
        linkTarget = null;
        linkLabel = null;
        recipeSignature = "";
    }

    /// <summary>Reads exactly <paramref name="count"/> bytes or throws <see cref="EndOfStreamException"/>.
    /// <see cref="BinaryReader.ReadBytes"/> can return fewer bytes at end-of-stream without throwing, which
    /// would let a truncated id pass as a wrong-but-valid Guid.</summary>
    private static byte[] ReadExactly(BinaryReader r, int count)
    {
        var buffer = r.ReadBytes(count);
        if (buffer.Length != count) throw new EndOfStreamException();
        return buffer;
    }
}
