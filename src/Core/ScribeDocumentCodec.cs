using System.Text;

namespace Scribe.Core;

/// <summary>
/// Serializes a <see cref="ScribeDocument"/> to a byte array and back. The same bytes are
/// used for both world persistence and network sync, so the round-trip is exact and any
/// malformed input fails safely (returns false) rather than throwing.
///
/// Current format (v8, little-endian via <see cref="BinaryWriter"/>):
///   [4 bytes magic "SCRB"][1 byte version][16 bytes DocId][int blockCount]
///   [per block: 16 bytes TaskId, byte kind, bool done, int depth, bool hasAssignedToUid,
///    string assignedToUid (only if hasAssignedToUid), string text,
///    bool hasTargetItemCode, string targetItemCode (only if hasTargetItemCode),
///    int targetQuantity, int currentQuantity,
///    bool hasLinkTarget, string linkTarget (only if hasLinkTarget),
///    bool hasLinkLabel, string linkLabel (only if hasLinkLabel),
///    string recipeSignature (v8+; empty string when none)]
///   [string title]
///
/// Accepted-version window (PROGRESSIVE append-only reads, NOT a two-version window):
///   Reads any version in [<see cref="MinVersion"/>=5, <see cref="Version"/>=8]; older/newer → fail-safe false.
///   Each version-group's trailing fields are read only behind a <c>version &gt;=</c> threshold, so an
///   older blob simply stops reading before the fields it never wrote. Missing fields default via
///   <see cref="ApplyPreV6Defaults"/>. This departs from the strict two-version window because v5 docs
///   are SHIPPED and live in real saves (v1.1.1), while v6 lived only on the WIP branch — a naive
///   {v7, v6} window would silently drop every shipped v5 document. Same rationale as ScribePinCodec's
///   v2→v3 switch; see <see href="../docs/CODEC-MIGRATION.md">docs/CODEC-MIGRATION.md</see>.
///
/// APPEND-ONLY VERSION DISCIPLINE: <see cref="Version"/> is a single global counter and new
/// fields append in version order (never interleave; never two "v7"s). See
/// docs/specs/README.md convention #1. When adding v8, append its fields after v7's layout behind a
/// new <c>version &gt;= 8</c> read gate — do not reorder existing fields.
///
/// Field history:
///   v4 — added a 16-byte document id (after the version byte) and a 16-byte per-block id
///        (first field of each block); dropped the v3 per-block `pinned` bool (pinning moved
///        to a per-player store).
///   v5 — appended a document title string after the block list.
///   v6 — appended four per-block fields after text (TargetItemCode?, TargetQuantity,
///        CurrentQuantity, LinkTarget?) for the Tracker and Link task kinds.
///   v7 — appended one per-block field after LinkTarget (LinkLabel?), the display title of a
///        guide-page Link (a "page:"-prefixed LinkTarget has no item to resolve a name from).
///        Switched to progressive reads so shipped v5 docs stay readable (see the window note above).
///   v8 — appended one per-block field after LinkLabel (RecipeSignature, a plain string; empty when
///        none), the grid-recipe binding of a Craft task (kind 4). Written for every block (empty for
///        non-Craft); a pre-v8 blob simply stops before it and defaults to empty.
///
/// A hand-rolled format keeps Core free of any external dependency. The version byte lets
/// us evolve the format while still reading every prior save layout back to <see cref="MinVersion"/>.
/// </summary>
public static class ScribeDocumentCodec
{
    private static readonly byte[] Magic = "SCRB"u8.ToArray();
    private const byte Version = 8; // see the "Field history" above; v8 adds the per-block RecipeSignature field.

    /// <summary>Oldest format version the reader still accepts. Progressive append-only reads accept
    /// any version in [<see cref="MinVersion"/>, <see cref="Version"/>]; v5 is the oldest because it is
    /// the last SHIPPED layout (v1.1.1) still live in player saves. Replaces the former two-version
    /// <c>PriorVersion</c> gate — see the class doc-comment's window note.</summary>
    private const byte MinVersion = 5;

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

    /// <summary>Upper bound on a freeform Text/note block's text length, in characters. See
    /// <see cref="MaxBlocks"/>. An over-long note is CLIPPED to this length on read (the same backstop Task
    /// blocks get via <see cref="MaxTaskTextLength"/>); the editor field also enforces it live as a
    /// maxlength, so the clip is rarely reached. Larger than the Task cap because notes hold freeform prose,
    /// not one-liners.</summary>
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
                // v6: Tracker/Link fields, appended after text (see the format table above).
                w.Write(block.TargetItemCode is not null);
                if (block.TargetItemCode is not null) w.Write(block.TargetItemCode);
                w.Write(block.TargetQuantity);
                w.Write(block.CurrentQuantity);
                w.Write(block.LinkTarget is not null);
                if (block.LinkTarget is not null) w.Write(block.LinkTarget);
                // v7: LinkLabel, appended after LinkTarget — the display title of a guide-page Link.
                w.Write(block.LinkLabel is not null);
                if (block.LinkLabel is not null) w.Write(block.LinkLabel);
                // v8: RecipeSignature, appended after LinkLabel — the grid-recipe binding of a Craft
                // task. Always written (empty string for non-Craft blocks), so it is a plain string,
                // not a has/value pair.
                w.Write(block.RecipeSignature);
            }
            w.Write(doc.Title); // v5+: document title appended after block list
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes a document. Accepts any version in [MinVersion=5, Version=8] via progressive reads;
    /// any other version fails safely. Signature-stable for callers that don't need the legacy-pin out-param — routes through the
    /// three-arg overload and discards it.
    /// </summary>
    public static bool TryDeserialize(byte[]? bytes, out ScribeDocument? document)
        => TryDeserialize(bytes, out document, out _);

    /// <summary>
    /// Deserializes a document. <paramref name="legacyPinnedTaskIds"/> is always empty (v3, which
    /// carried per-block pin flags, is no longer accepted). Kept for call-site compatibility.
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

            // Progressive append-only reads: accept any version in [MinVersion, Version]; each
            // version-group's trailing fields are read behind a `version >=` gate below. See the
            // class doc-comment's window note and docs/CODEC-MIGRATION.md.
            byte version = r.ReadByte();
            if (version < MinVersion || version > Version) return false;

            Guid docId = new Guid(ReadExactly(r, 16));

            int blockCount = r.ReadInt32();
            // Reject a negative count, a count that can't physically fit in the buffer (a tiny
            // payload claiming billions of blocks — an allocation guard), or one over the hard
            // MaxBlocks cap on document size.
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
                // Length backstop: CLIP an over-long row to its kind's cap rather than rejecting, so a
                // single over-long row can never silently drop the entire document. Tasks clip to the soft
                // task cap; freeform Text/note sections clip to the larger note cap. (The editor field also
                // enforces these live as a maxlength, so this server-side clip is rarely reached.) This
                // replaced an earlier reject-whole-document guard on the Text path, which became a
                // data-loss trap once notes were user-creatable via the kind picker.
                if (kind == ScribeBlockKind.Task)
                {
                    if (text.Length > MaxTaskTextLength) text = text.Substring(0, MaxTaskTextLength);
                }
                else if (text.Length > MaxTextLength)
                {
                    text = text.Substring(0, MaxTextLength);
                }

                // Tracker/Link fields were added across v6 (item/quantity/linkTarget) and v7 (linkLabel).
                // Seed all of them to defaults, then read each version-group behind a `version >=` gate,
                // so an older blob simply stops reading before the fields it never wrote (progressive
                // append-only reads — see the class doc-comment's window note).
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
                    recipeSignature = r.ReadString(); // always written from v8; empty when none
                }

                // The block's setters clamp TargetQuantity (≥ 1), CurrentQuantity ([0, target]),
                // and Depth ([0, 1]).
                blocks.Add(new ScribeBlock(kind, text, done, depth, assignedToUid, taskId,
                    targetItemCode, targetQuantity, currentQuantity, linkTarget, linkLabel, recipeSignature));
            }

            // Both accepted versions (v6 and v5) append a document title after the block list.
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
            // Truncated or malformed input — fail safely.
            document = null;
            legacyPinnedTaskIds = Array.Empty<Guid>();
            return false;
        }
    }

    /// <summary>
    /// True when <paramref name="bytes"/> are a readable document written in an OLDER-than-current but
    /// still-accepted format version (any version in [<see cref="MinVersion"/>, <see cref="Version"/>)).
    /// Such a document deserializes fine but lacks fields the current layout carries, so the block
    /// entity can choose to re-save it immediately to upgrade it forward. Returns false for
    /// current-version bytes (nothing to upgrade), and for null/malformed/unsupported bytes.
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
    /// Seeds the per-block Tracker/Link fields to their defaults before the version-gated progressive
    /// reads overwrite whatever a given blob actually carried: no target/link code
    /// (<paramref name="targetItemCode"/> / <paramref name="linkTarget"/> / <paramref name="linkLabel"/> = null),
    /// <paramref name="targetQuantity"/> = 1 (satisfies the <see cref="ScribeBlock"/> ≥ 1 invariant),
    /// <paramref name="currentQuantity"/> = 0, and <paramref name="recipeSignature"/> = "" (empty; the
    /// v8-only Craft recipe binding). These are the values a pre-v6 block (always Task or Text, for which
    /// they're meaningless) reads with, and also the baseline a v6/v7 block gets for the later
    /// LinkLabel/RecipeSignature fields. This is the single documented home for the "fields a pre-current block lacks"
    /// defaulting; the naming mirrors ScribePinCodec's <c>ApplyPreV2Defaults</c>. See docs/CODEC-MIGRATION.md.
    /// </summary>
    private static void ApplyPreV6Defaults(out string? targetItemCode, out int targetQuantity,
        out int currentQuantity, out string? linkTarget, out string? linkLabel, out string recipeSignature)
    {
        targetItemCode = null;
        targetQuantity = 1;
        currentQuantity = 0;
        linkTarget = null;
        linkLabel = null;
        recipeSignature = ""; // v8+ field; a pre-v8 block (never a Craft) reads with no recipe binding
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
