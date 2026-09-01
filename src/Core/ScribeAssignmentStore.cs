using System.Security.Cryptography;
using System.Text;

namespace Scribe.Core;

/// <summary>
/// Server-side owner of every player-to-player assignment record. One <see cref="ScribeBlock"/> per
/// assignment — its Kind/Text/TargetItemCode/LinkTarget fields are the assigned content (a Task,
/// Tracker, or Quest Link — the same shape any other block uses, so no separate content schema is
/// needed), and its <see cref="ScribeBlock.Assignment"/> is the lifecycle state — keyed by the block's
/// own <see cref="ScribeBlock.TaskId"/>, which doubles as the wire "AssignmentId" the send/action
/// messages reference.
///
/// <b>Append-only, one canonical object.</b> A record is never removed once created; only its
/// <see cref="ScribeAssignment.State"/> changes. The Assigner's Assignment-tab history and the
/// Assignee's Inbox are both just filtered views (<see cref="Sent"/> / <see cref="Received"/>) over
/// this same store, so the two sides can never desync into independently-mutated copies
/// (locked-on-send). Accept-time placement (moving a copy of the content into the Assignee's own
/// document — see the `assignment-state-machine` capability's placement requirement) is a SEPARATE
/// step the Mod layer performs after a successful Accept here; this store's record keeps tracking
/// lifecycle state regardless, so the Assigner's read-only view stays correct.
///
/// Game-agnostic (pure BCL) so the state-machine wiring is unit-testable without a game install; the
/// Mod layer owns the network push and save-game persistence, calling into this store the same way
/// <c>ScribeModSystem</c> already calls into <c>ScribePinStore</c>.
/// </summary>
public sealed class ScribeAssignmentStore
{
    /// <summary>Hard upper bound on the number of assignment records the whole store may ever hold — an
    /// allocation guard against a runaway/hostile client, mirroring <see cref="ScribePinCodec.MaxPlayers"/>-
    /// style caps elsewhere. Generous relative to any realistic playgroup's lifetime assignment history.</summary>
    public const int MaxAssignments = 5000;

    private readonly Dictionary<Guid, ScribeBlock> _records = new();

    // ---------------- Reads ----------------

    /// <summary>Every assignment this player SENT (their Assignment-tab history), in creation order.</summary>
    public IReadOnlyList<ScribeBlock> Sent(string playerUid)
        => _records.Values.Where(b => b.Assignment?.AssignerUid == playerUid).ToList();

    /// <summary>Every assignment this player RECEIVED (their Inbox), in creation order.</summary>
    public IReadOnlyList<ScribeBlock> Received(string playerUid)
        => _records.Values.Where(b => b.Assignment?.TargetPlayerUid == playerUid).ToList();

    /// <summary>Resolves a single record by its id (= its <see cref="ScribeBlock.TaskId"/>), or null if
    /// unknown.</summary>
    public ScribeBlock? TryGet(Guid assignmentId)
        => _records.TryGetValue(assignmentId, out var block) ? block : null;

    // ---------------- Create ----------------

    /// <summary>
    /// Creates a new assignment record, server-authoritative aside from the client-minted
    /// <paramref name="assignmentId"/> (which must be fresh — a collision is rejected rather than
    /// overwriting an existing record). Rejects blank uids and enforces <see cref="MaxAssignments"/>.
    /// Text is clipped to <see cref="ScribeDocumentCodec.MaxTaskTextLength"/>, matching every other
    /// Task-kind text path. Returns the created record via <paramref name="record"/> on success.
    ///
    /// <para><b>Trailing optional parameters (assignment-multi-item-creation, design.md D12):</b> the
    /// original Task-only shape is <paramref name="kind"/>'s default, so the single-item quick-send path
    /// (<c>OnServerReceivedSendAssignment</c>) keeps calling this with only the first six arguments,
    /// behavior-identical to before. The batch-send path (<c>OnServerReceivedSendAssignmentBatch</c>)
    /// supplies the rest to carry a Tracker/Link/Craft row's full shape. <paramref name="taskText"/> is
    /// required non-blank ONLY for a Task/Text-kind row — a Tracker/Link/Craft row's label lives on
    /// <paramref name="targetItemCode"/>/<paramref name="linkTarget"/> instead, exactly like any other
    /// <see cref="ScribeBlock"/> of that kind (its own <c>Text</c> is blank by convention).</para>
    ///
    /// <para><paramref name="batchId"/> (refine-assignment-desk-inbox-ux 12.2 root-cause fix): the
    /// caller mints ONE fresh <see cref="Guid"/> per send call and passes it for every row that call
    /// creates, so the client can group/newest-first-sort a batch by an unambiguous id instead of the
    /// coarse, collidable <paramref name="assignedDate"/> string. See <see cref="ScribeAssignment.BatchId"/>.</para>
    /// </summary>
    public bool TryCreate(Guid assignmentId, string assignerUid, string targetPlayerUid, string taskText,
        string assignedDate, out ScribeBlock? record,
        ScribeBlockKind kind = ScribeBlockKind.Task, string? targetItemCode = null, int targetQuantity = 1,
        string? linkTarget = null, string? linkLabel = null, string? linkDescription = null,
        string? recipeSignature = null, int depth = 0, Guid batchId = default)
    {
        record = null;
        if (string.IsNullOrWhiteSpace(assignerUid) || string.IsNullOrWhiteSpace(targetPlayerUid)) return false;
        bool isItemKind = kind is ScribeBlockKind.Tracker or ScribeBlockKind.Link or ScribeBlockKind.Craft;
        if (!isItemKind && string.IsNullOrWhiteSpace(taskText)) return false;
        if (_records.ContainsKey(assignmentId)) return false; // ids are client-minted; must be fresh
        if (_records.Count >= MaxAssignments) return false;

        string text = taskText.Length > ScribeDocumentCodec.MaxTaskTextLength
            ? taskText[..ScribeDocumentCodec.MaxTaskTextLength]
            : taskText;
        var assignment = new ScribeAssignment(assignerUid, assignedDate,
            ScribeAssignmentState.Unaccepted, seen: false, targetPlayerUid, batchId);
        var block = new ScribeBlock(kind, text, depth: depth, taskId: assignmentId, assignment: assignment,
            targetItemCode: targetItemCode, targetQuantity: targetQuantity, linkTarget: linkTarget,
            linkLabel: linkLabel, recipeSignature: recipeSignature, linkDescription: linkDescription);
        _records[assignmentId] = block;
        record = block;
        return true;
    }

    // ---------------- Transitions ----------------

    /// <summary>
    /// Applies a requested action, resolving the acting player's role from whichever of
    /// <see cref="ScribeAssignment.AssignerUid"/>/<see cref="ScribeAssignment.TargetPlayerUid"/> matches
    /// <paramref name="actingPlayerUid"/> (a player who assigned to themselves matches both — tried as
    /// Assignee first, then Assigner, so whichever role the action is actually legal for succeeds).
    /// Returns false for an unknown id, a player who is neither party, or an illegal transition; the
    /// record's state is untouched on any false return.
    /// </summary>
    public bool TryApplyAction(Guid assignmentId, string actingPlayerUid, ScribeAssignmentAction action)
    {
        if (!_records.TryGetValue(assignmentId, out var block) || block.Assignment is not { } assignment)
            return false;

        bool isAssignee = assignment.TargetPlayerUid == actingPlayerUid;
        bool isAssigner = assignment.AssignerUid == actingPlayerUid;
        if (!isAssignee && !isAssigner) return false;

        if (isAssignee && ScribeAssignmentTransitions.TryApply(assignment, ScribeAssignmentActor.Assignee, action))
            return true;
        if (isAssigner && ScribeAssignmentTransitions.TryApply(assignment, ScribeAssignmentActor.Assigner, action))
            return true;
        return false;
    }

    /// <summary>Marks a received assignment seen — only the actual recipient may do so. No-op (returns
    /// false) for an unknown id, a non-recipient, or one already seen.</summary>
    public bool TryMarkSeen(Guid assignmentId, string viewingPlayerUid)
    {
        if (!_records.TryGetValue(assignmentId, out var block) || block.Assignment is not { } assignment)
            return false;
        if (assignment.TargetPlayerUid != viewingPlayerUid || assignment.Seen) return false;
        assignment.MarkSeen();
        return true;
    }

    /// <summary>Marks EVERY currently-unseen assignment this player received as seen (design.md Decision 4:
    /// "opening the Inbox flips it server-side"). Returns true if anything actually changed, so the caller
    /// can skip a re-push when nothing was unseen.</summary>
    public bool MarkAllSeen(string viewingPlayerUid)
    {
        bool changed = false;
        foreach (var block in Received(viewingPlayerUid))
            changed |= TryMarkSeen(block.TaskId, viewingPlayerUid);
        return changed;
    }

    // ---------------- Persistence / network bridge ----------------

    private static readonly byte[] ListMagic = "SASN"u8.ToArray();
    private static readonly byte[] StoreMagic = "SAST"u8.ToArray();
    // v2 adds RecipeSignature (assignment-multi-item-creation D12 — a Craft row's recipe binding).
    // v3 adds BatchId (refine-assignment-desk-inbox-ux 12.2 root-cause fix — see ScribeAssignment.BatchId).
    // v4 adds AcceptedDate/DeclinedDate/CancelledDate/DiscardedDate/CompletedDate (refine-assignment-
    // desk-inbox-ux triage 2026-08-31 — per-transition history stubs, see ScribeAssignment's remarks).
    // Progressive append-only reads (matching ScribeDocumentCodec's convention): any version in
    // [MinVersion, Version] is accepted; a v1 blob simply predates Craft-kind assignments (which didn't
    // exist yet), so every one of its records is genuinely RecipeSignature-less — defaulting it to ""
    // on read is exactly correct, not a lossy guess. A pre-v3 blob predates BatchId entirely; its records
    // are synthesized a deterministic per-(assigner,target,date) id on read (DeriveLegacyBatchId) so
    // pre-existing multi-item batches keep grouping the same way they always displayed, without a real
    // minted id ever having existed for them. A pre-v4 blob predates every transition timestamp — those
    // genuinely never happened as far as the record can say, so defaulting all five to null on read is
    // exactly correct, not a lossy guess.
    private const byte Version = 4;
    private const byte MinVersion = 1;

    /// <summary>Serializes a single player's view (<see cref="Sent"/> or <see cref="Received"/>) for the
    /// server→client push.</summary>
    public static byte[] SerializeList(IReadOnlyList<ScribeBlock> records)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(ListMagic);
            w.Write(Version);
            WriteRecordList(w, records);
        }
        return ms.ToArray();
    }

    /// <summary>Deserializes a single player's pushed view.</summary>
    public static bool TryDeserializeList(byte[]? bytes, out List<ScribeBlock>? records)
    {
        records = null;
        if (bytes is null) return false;
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            int version = ReadHeader(r, ListMagic);
            if (version < MinVersion || version > Version) return false;
            if (!TryReadRecordList(r, bytes.Length, version, out var list)) return false;
            records = list;
            return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            records = null;
            return false;
        }
    }

    /// <summary>Serializes the whole store (every record, regardless of player) for the savegame blob.</summary>
    public byte[] SerializeStore()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(StoreMagic);
            w.Write(Version);
            WriteRecordList(w, _records.Values.ToList());
        }
        return ms.ToArray();
    }

    /// <summary>Replaces the in-memory store from the persisted blob (called on world load). A
    /// null/malformed blob leaves the store empty rather than throwing.</summary>
    public void LoadFrom(byte[]? bytes)
    {
        _records.Clear();
        if (bytes is null) return;
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            int version = ReadHeader(r, StoreMagic);
            if (version < MinVersion || version > Version) return;
            if (!TryReadRecordList(r, bytes.Length, version, out var list)) return;
            foreach (var block in list) _records[block.TaskId] = block;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            // Malformed — leave the store empty.
        }
    }

    private static int ReadHeader(BinaryReader r, byte[] expectedMagic)
    {
        var magic = r.ReadBytes(expectedMagic.Length);
        if (!magic.AsSpan().SequenceEqual(expectedMagic)) return -1;
        return r.ReadByte();
    }

    private static void WriteRecordList(BinaryWriter w, IReadOnlyList<ScribeBlock> records)
    {
        w.Write(records.Count);
        foreach (var block in records)
        {
            w.Write(block.TaskId.ToByteArray());
            w.Write((byte)block.Kind);
            w.Write(block.Text);
            bool hasTargetItemCode = block.TargetItemCode is not null;
            w.Write(hasTargetItemCode);
            if (hasTargetItemCode) w.Write(block.TargetItemCode!);
            w.Write(block.TargetQuantity);
            w.Write(block.CurrentQuantity);
            bool hasLinkTarget = block.LinkTarget is not null;
            w.Write(hasLinkTarget);
            if (hasLinkTarget) w.Write(block.LinkTarget!);
            bool hasLinkLabel = block.LinkLabel is not null;
            w.Write(hasLinkLabel);
            if (hasLinkLabel) w.Write(block.LinkLabel!);
            bool hasLinkDescription = block.LinkDescription is not null;
            w.Write(hasLinkDescription);
            if (hasLinkDescription) w.Write(block.LinkDescription!);
            w.Write(block.Depth);
            w.Write(block.RecipeSignature); // v2+ (never null — ScribeBlock defaults it to "")

            // Every store record carries an Assignment by construction; a defensive empty default guards
            // against a hand-built ScribeBlock without one rather than throwing.
            var assignment = block.Assignment ?? new ScribeAssignment("", "");
            w.Write(assignment.AssignerUid);
            w.Write(assignment.TargetPlayerUid);
            w.Write((byte)assignment.State);
            w.Write(assignment.AssignedDate);
            w.Write(assignment.Seen);
            w.Write(assignment.BatchId.ToByteArray()); // v3+
            WriteOptionalString(w, assignment.AcceptedDate);   // v4+
            WriteOptionalString(w, assignment.DeclinedDate);   // v4+
            WriteOptionalString(w, assignment.CancelledDate);  // v4+
            WriteOptionalString(w, assignment.DiscardedDate);  // v4+
            WriteOptionalString(w, assignment.CompletedDate);  // v4+
        }
    }

    private static bool TryReadRecordList(BinaryReader r, int totalBytes, int version, out List<ScribeBlock> records)
    {
        records = new List<ScribeBlock>();
        int count = r.ReadInt32();
        if (count < 0 || count > totalBytes || count > MaxAssignments) return false;

        var list = new List<ScribeBlock>(count);
        for (int i = 0; i < count; i++)
        {
            var taskId = new Guid(ReadExactly(r, 16));
            var kind = (ScribeBlockKind)r.ReadByte();
            string text = r.ReadString();
            if (text.Length > ScribeDocumentCodec.MaxTaskTextLength) return false;

            string? targetItemCode = r.ReadBoolean() ? r.ReadString() : null;
            int targetQuantity = r.ReadInt32();
            int currentQuantity = r.ReadInt32();
            string? linkTarget = r.ReadBoolean() ? r.ReadString() : null;
            string? linkLabel = r.ReadBoolean() ? r.ReadString() : null;
            string? linkDescription = r.ReadBoolean() ? r.ReadString() : null;
            int depth = r.ReadInt32();
            // v1 predates Craft-kind assignments, so a v1 record genuinely has no recipe binding to lose.
            string recipeSignature = version >= 2 ? r.ReadString() : "";

            string assignerUid = r.ReadString();
            string targetPlayerUid = r.ReadString();
            var state = (ScribeAssignmentState)r.ReadByte();
            string assignedDate = r.ReadString();
            bool seen = r.ReadBoolean();
            // v1/v2 predate BatchId entirely — synthesize a stable one so a pre-existing multi-item batch
            // still groups together after upgrading (see the Version-constant remarks above).
            Guid batchId = version >= 3
                ? new Guid(ReadExactly(r, 16))
                : DeriveLegacyBatchId(assignerUid, targetPlayerUid, assignedDate);
            var assignment = new ScribeAssignment(assignerUid, assignedDate, state, seen, targetPlayerUid, batchId)
            {
                AcceptedDate = ReadOptionalString(r, version),
                DeclinedDate = ReadOptionalString(r, version),
                CancelledDate = ReadOptionalString(r, version),
                DiscardedDate = ReadOptionalString(r, version),
                CompletedDate = ReadOptionalString(r, version),
            };

            list.Add(new ScribeBlock(kind, text, depth: depth, taskId: taskId,
                targetItemCode: targetItemCode, targetQuantity: targetQuantity, currentQuantity: currentQuantity,
                linkTarget: linkTarget, linkLabel: linkLabel, recipeSignature: recipeSignature,
                assignment: assignment, linkDescription: linkDescription));
        }
        records = list;
        return true;
    }

    /// <summary>Synthesizes a stable <see cref="ScribeAssignment.BatchId"/> for a pre-v3 record that never
    /// had a real one, from the same three fields the old (buggy) grouping used — so a pre-existing
    /// multi-item batch keeps grouping together after upgrading rather than exploding into one "batch"
    /// per row. Not security-sensitive (a display-grouping key only), so SHA-256 truncated to 16 bytes is
    /// just a convenient stable hash, not a cryptographic use.</summary>
    private static Guid DeriveLegacyBatchId(string assignerUid, string targetPlayerUid, string assignedDate)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{assignerUid} {targetPlayerUid} {assignedDate}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>Writes a nullable string as a has-value flag plus the string when present — the same
    /// pattern already used inline for TargetItemCode/LinkTarget/LinkLabel/LinkDescription, extracted here
    /// because v4 repeats it five times for the new per-transition timestamp fields.</summary>
    private static void WriteOptionalString(BinaryWriter w, string? value)
    {
        bool hasValue = value is not null;
        w.Write(hasValue);
        if (hasValue) w.Write(value!);
    }

    /// <summary>Reads a v4+ <see cref="WriteOptionalString"/> value, or null when this blob predates v4
    /// (no bytes were ever written for it).</summary>
    private static string? ReadOptionalString(BinaryReader r, int version) =>
        version >= 4 ? (r.ReadBoolean() ? r.ReadString() : null) : null;

    private static byte[] ReadExactly(BinaryReader r, int count)
    {
        var buffer = r.ReadBytes(count);
        if (buffer.Length != count) throw new EndOfStreamException();
        return buffer;
    }
}
