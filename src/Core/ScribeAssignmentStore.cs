using System.Security.Cryptography;
using System.Text;

namespace Scribe.Core;

/// <summary>
/// Server-side owner of every player-to-player assignment record. One <see cref="ScribeBlock"/> per
/// assignment: its content fields are the assigned content and its <see cref="ScribeBlock.Assignment"/> is
/// the lifecycle state, keyed by <see cref="ScribeBlock.TaskId"/> (the wire "AssignmentId").
///
/// Records are only removed via <see cref="TryDelete"/>: each side of a terminal record hides its own view
/// independently, and the record is removed once both have. Every other mutation changes only
/// <see cref="ScribeAssignment.State"/>, so <see cref="Sent"/> and <see cref="Received"/> are filtered views
/// of one canonical object and cannot desync. Accept-time placement into the Assignee's document is a
/// separate step the Mod layer performs after a successful Accept here.
///
/// Game-agnostic (pure BCL) so it is unit-testable without a game install; the Mod layer owns the network
/// push and save-game persistence.
/// </summary>
public sealed class ScribeAssignmentStore
{
    /// <summary>Upper bound on records the store may hold — an allocation guard against a runaway or
    /// hostile client.</summary>
    public const int MaxAssignments = 5000;

    private readonly Dictionary<Guid, ScribeBlock> _records = new();

    // ---------------- Reads ----------------

    /// <summary>Assignments this player SENT, in creation order, excluding records whose assigner side was
    /// deleted (<see cref="ScribeAssignment.HiddenFromAssigner"/>).</summary>
    public IReadOnlyList<ScribeBlock> Sent(string playerUid)
        => _records.Values.Where(b => b.Assignment?.AssignerUid == playerUid && !b.Assignment.HiddenFromAssigner).ToList();

    /// <summary>Assignments this player RECEIVED, in creation order, excluding records whose assignee side
    /// was deleted and Task Notice records still in <see cref="ScribeAssignmentState.Sent"/> (not yet
    /// received; see <see cref="TryMarkReceived"/>).</summary>
    public IReadOnlyList<ScribeBlock> Received(string playerUid)
        => _records.Values.Where(b => b.Assignment?.TargetPlayerUid == playerUid && !b.Assignment.HiddenFromAssignee
            && b.Assignment.State != ScribeAssignmentState.Sent).ToList();

    /// <summary>Resolves a record by id (= <see cref="ScribeBlock.TaskId"/>), or null if unknown.</summary>
    public ScribeBlock? TryGet(Guid assignmentId)
        => _records.TryGetValue(assignmentId, out var block) ? block : null;

    // ---------------- Create ----------------

    /// <summary>
    /// Creates a new assignment record. <paramref name="assignmentId"/> is client-minted and must be fresh
    /// (a collision is rejected, never overwritten). Rejects blank uids and enforces
    /// <see cref="MaxAssignments"/>. Text is clipped to <see cref="ScribeDocumentCodec.MaxTaskTextLength"/>.
    ///
    /// <para><paramref name="taskText"/> must be non-blank only for Task/Text-kind rows; a Tracker/Link/Craft
    /// row's label lives on <paramref name="targetItemCode"/>/<paramref name="linkTarget"/> and its own
    /// Text is blank.</para>
    ///
    /// <para><paramref name="batchId"/>: one fresh <see cref="Guid"/> per send call, shared by every row that
    /// call creates, so clients can group a batch unambiguously. See <see cref="ScribeAssignment.BatchId"/>.</para>
    /// </summary>
    public bool TryCreate(Guid assignmentId, string assignerUid, string targetPlayerUid, string taskText,
        string assignedDate, out ScribeBlock? record,
        ScribeBlockKind kind = ScribeBlockKind.Task, string? targetItemCode = null, int targetQuantity = 1,
        string? linkTarget = null, string? linkLabel = null, string? linkDescription = null,
        string? recipeSignature = null, int depth = 0, Guid batchId = default,
        double? assignedTimestamp = null)
        => TryCreateCore(assignmentId, assignerUid, targetPlayerUid, taskText, assignedDate,
            ScribeAssignmentState.Unaccepted, seen: false, acceptedDate: null, out record,
            kind, targetItemCode, targetQuantity, linkTarget, linkLabel, linkDescription,
            recipeSignature, depth, batchId, assignedTimestamp, acceptedTimestamp: null);

    /// <summary>
    /// Accept-time counterpart of <see cref="TryCreate"/> for Task Notices: creates a record already
    /// <see cref="ScribeAssignmentState.Accepted"/> (the physical notice carried the pending decision, not a
    /// store record). <paramref name="acceptedDate"/> is stamped immediately because
    /// <see cref="TryApplyAction"/> never runs for this transition. The record starts
    /// <see cref="ScribeAssignment.Seen"/>.
    /// </summary>
    public bool TryCreateAccepted(Guid assignmentId, string assignerUid, string targetPlayerUid, string taskText,
        string assignedDate, string acceptedDate, out ScribeBlock? record,
        ScribeBlockKind kind = ScribeBlockKind.Task, string? targetItemCode = null, int targetQuantity = 1,
        string? linkTarget = null, string? linkLabel = null, string? linkDescription = null,
        string? recipeSignature = null, int depth = 0, Guid batchId = default,
        double? assignedTimestamp = null, double? acceptedTimestamp = null)
        => TryCreateCore(assignmentId, assignerUid, targetPlayerUid, taskText, assignedDate,
            ScribeAssignmentState.Accepted, seen: true, acceptedDate, out record,
            kind, targetItemCode, targetQuantity, linkTarget, linkLabel, linkDescription,
            recipeSignature, depth, batchId, assignedTimestamp, acceptedTimestamp);

    /// <summary>
    /// Send-time counterpart of <see cref="TryCreate"/> for Task Notices: creates a record in the
    /// <see cref="ScribeAssignmentState.Sent"/> state using the assignment id embedded in the sealed notice.
    /// Visible in the Assigner's <see cref="Sent"/> view at once; hidden from the Assignee's
    /// <see cref="Received"/> view until <see cref="TryMarkReceived"/>.
    /// </summary>
    public bool TryCreateSent(Guid assignmentId, string assignerUid, string targetPlayerUid, string taskText,
        string assignedDate, out ScribeBlock? record,
        ScribeBlockKind kind = ScribeBlockKind.Task, string? targetItemCode = null, int targetQuantity = 1,
        string? linkTarget = null, string? linkLabel = null, string? linkDescription = null,
        string? recipeSignature = null, int depth = 0, Guid batchId = default,
        double? assignedTimestamp = null)
        => TryCreateCore(assignmentId, assignerUid, targetPlayerUid, taskText, assignedDate,
            ScribeAssignmentState.Sent, seen: false, acceptedDate: null, out record,
            kind, targetItemCode, targetQuantity, linkTarget, linkLabel, linkDescription,
            recipeSignature, depth, batchId, assignedTimestamp, acceptedTimestamp: null);

    private bool TryCreateCore(Guid assignmentId, string assignerUid, string targetPlayerUid, string taskText,
        string assignedDate, ScribeAssignmentState state, bool seen, string? acceptedDate, out ScribeBlock? record,
        ScribeBlockKind kind, string? targetItemCode, int targetQuantity,
        string? linkTarget, string? linkLabel, string? linkDescription,
        string? recipeSignature, int depth, Guid batchId,
        double? assignedTimestamp, double? acceptedTimestamp)
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
        var assignment = new ScribeAssignment(assignerUid, assignedDate, state, seen, targetPlayerUid, batchId)
        {
            AcceptedDate = acceptedDate,
            AssignedTimestamp = assignedTimestamp,
            AcceptedTimestamp = acceptedTimestamp,
        };
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
    /// (a self-assignment matches both: Assignee is tried first, then Assigner). Returns false, with the
    /// state untouched, for an unknown id, a non-party player, or an illegal transition.
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

    /// <summary>
    /// Transitions a <see cref="ScribeAssignmentState.Sent"/> Task Notice record to Unaccepted when the
    /// sealed notice reaches the Assignee's inventory, stamping <see cref="ScribeAssignment.ReceivedDate"/>.
    /// Returns false, record unchanged, for an unknown id or a record not in the Sent state.
    /// </summary>
    public bool TryMarkReceived(Guid assignmentId, string receivedDate, double? receivedTimestamp = null)
    {
        if (!_records.TryGetValue(assignmentId, out var block) || block.Assignment is not { } assignment)
            return false;
        if (assignment.State != ScribeAssignmentState.Sent) return false;
        assignment.State = ScribeAssignmentState.Unaccepted;
        assignment.ReceivedDate = receivedDate;
        assignment.ReceivedTimestamp = receivedTimestamp;
        return true;
    }

    /// <summary>
    /// Rewrites an Unaccepted record's <see cref="ScribeAssignment.TargetPlayerUid"/> to a different player,
    /// for a non-recipient holder confirming a sealed Task Notice, just before the Accept call. Stamps
    /// <see cref="ScribeAssignment.RedirectedFromUid"/> and <see cref="ScribeAssignment.RedirectedDate"/>.
    /// Returns false, record unchanged, if the target is unchanged, the id is unknown, or the record is not
    /// Unaccepted.
    /// </summary>
    public bool TryRedirectTarget(Guid assignmentId, string newTargetPlayerUid, string redirectedDate,
        double? redirectedTimestamp = null)
    {
        if (!_records.TryGetValue(assignmentId, out var block) || block.Assignment is not { } assignment)
            return false;
        if (assignment.State != ScribeAssignmentState.Unaccepted) return false;
        if (assignment.TargetPlayerUid == newTargetPlayerUid) return false;

        assignment.RedirectedFromUid = assignment.TargetPlayerUid;
        assignment.TargetPlayerUid = newTargetPlayerUid;
        assignment.RedirectedDate = redirectedDate;
        assignment.RedirectedTimestamp = redirectedTimestamp;
        return true;
    }

    /// <summary>
    /// Deletes a terminal-state record (Declined, Cancelled, Discarded, Completed) from the requesting
    /// <paramref name="side"/>'s view only, by setting <see cref="ScribeAssignment.HiddenFromAssignee"/> or
    /// <see cref="ScribeAssignment.HiddenFromAssigner"/>. <paramref name="actingPlayerUid"/> must hold the
    /// claimed side; a self-assignment still deletes only the ONE named side per request. When BOTH sides
    /// have deleted, the record is removed from the store. Returns false, record unchanged, for an unknown
    /// id, a non-terminal state, or a player who doesn't hold the side.
    /// </summary>
    public bool TryDelete(Guid assignmentId, string actingPlayerUid, ScribeAssignmentActor side)
    {
        if (!_records.TryGetValue(assignmentId, out var block) || block.Assignment is not { } assignment)
            return false;
        if (!assignment.State.IsTerminal()) return false;

        bool actorHoldsSide = side switch
        {
            ScribeAssignmentActor.Assignee => assignment.TargetPlayerUid == actingPlayerUid,
            ScribeAssignmentActor.Assigner => assignment.AssignerUid == actingPlayerUid,
            _ => false,
        };
        if (!actorHoldsSide) return false;

        if (side == ScribeAssignmentActor.Assignee) assignment.HiddenFromAssignee = true;
        else assignment.HiddenFromAssigner = true;

        if (assignment.HiddenFromAssignee && assignment.HiddenFromAssigner) _records.Remove(assignmentId);
        return true;
    }

    /// <summary>Marks a received assignment seen; only the recipient may. Returns false for an unknown id,
    /// a non-recipient, or one already seen.</summary>
    public bool TryMarkSeen(Guid assignmentId, string viewingPlayerUid)
    {
        if (!_records.TryGetValue(assignmentId, out var block) || block.Assignment is not { } assignment)
            return false;
        if (assignment.TargetPlayerUid != viewingPlayerUid || assignment.Seen) return false;
        assignment.MarkSeen();
        return true;
    }

    /// <summary>Marks every unseen assignment this player received as seen. Returns true if anything
    /// changed, so the caller can skip a re-push otherwise.</summary>
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
    // Format versions (each appends fields; see the "vN+" tags in WriteRecordList):
    // v2 RecipeSignature, v3 BatchId, v4 per-transition date strings, v5 AcceptedIntoLabel,
    // v6 HiddenFromAssignee/Assigner, v7 ReceivedDate, v8 RedirectedFromUid/Date,
    // v9 eight optional numeric timestamps (bool has + double, so null is distinct from day 0).
    // Reads accept any version in [MinVersion, Version]. A field absent from an older blob defaults to
    // null/false/"" because it genuinely did not exist then; only BatchId (pre-v3) is synthesized, by
    // DeriveLegacyBatchId, so old multi-item batches keep grouping.
    private const byte Version = 9;
    private const byte MinVersion = 1;

    /// <summary>Serializes one player's view (<see cref="Sent"/> or <see cref="Received"/>) for the
    /// server-to-client push.</summary>
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

    /// <summary>Deserializes a pushed per-player view.</summary>
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

    /// <summary>Serializes the whole store for the savegame blob.</summary>
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

    /// <summary>Replaces the in-memory store from the persisted blob (world load). A null or malformed
    /// blob leaves the store empty.</summary>
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

            // Every store record carries an Assignment; the empty default guards a hand-built block.
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
            WriteOptionalString(w, assignment.AcceptedIntoLabel); // v5+
            w.Write(assignment.HiddenFromAssignee); // v6+
            w.Write(assignment.HiddenFromAssigner); // v6+
            WriteOptionalString(w, assignment.ReceivedDate); // v7+
            WriteOptionalString(w, assignment.RedirectedFromUid); // v8+
            WriteOptionalString(w, assignment.RedirectedDate);    // v8+
            WriteOptionalDouble(w, assignment.AssignedTimestamp);   // v9+
            WriteOptionalDouble(w, assignment.AcceptedTimestamp);   // v9+
            WriteOptionalDouble(w, assignment.DeclinedTimestamp);   // v9+
            WriteOptionalDouble(w, assignment.CancelledTimestamp);  // v9+
            WriteOptionalDouble(w, assignment.DiscardedTimestamp);  // v9+
            WriteOptionalDouble(w, assignment.CompletedTimestamp);  // v9+
            WriteOptionalDouble(w, assignment.ReceivedTimestamp);   // v9+
            WriteOptionalDouble(w, assignment.RedirectedTimestamp); // v9+
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
            // v1 predates Craft-kind assignments, so there is no recipe binding to lose.
            string recipeSignature = version >= 2 ? r.ReadString() : "";

            string assignerUid = r.ReadString();
            string targetPlayerUid = r.ReadString();
            var state = (ScribeAssignmentState)r.ReadByte();
            string assignedDate = r.ReadString();
            bool seen = r.ReadBoolean();
            // Pre-v3 records have no BatchId; synthesize a stable one so old batches still group.
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
                AcceptedIntoLabel = ReadOptionalString(r, version, minVersion: 5),
                // v1-v5 predate the hidden-flags; false is the correct default.
                HiddenFromAssignee = version >= 6 && r.ReadBoolean(),
                HiddenFromAssigner = version >= 6 && r.ReadBoolean(),
                ReceivedDate = ReadOptionalString(r, version, minVersion: 7),
                RedirectedFromUid = ReadOptionalString(r, version, minVersion: 8),
                RedirectedDate = ReadOptionalString(r, version, minVersion: 8),
                AssignedTimestamp = ReadOptionalDouble(r, version),
                AcceptedTimestamp = ReadOptionalDouble(r, version),
                DeclinedTimestamp = ReadOptionalDouble(r, version),
                CancelledTimestamp = ReadOptionalDouble(r, version),
                DiscardedTimestamp = ReadOptionalDouble(r, version),
                CompletedTimestamp = ReadOptionalDouble(r, version),
                ReceivedTimestamp = ReadOptionalDouble(r, version),
                RedirectedTimestamp = ReadOptionalDouble(r, version),
            };

            list.Add(new ScribeBlock(kind, text, depth: depth, taskId: taskId,
                targetItemCode: targetItemCode, targetQuantity: targetQuantity, currentQuantity: currentQuantity,
                linkTarget: linkTarget, linkLabel: linkLabel, recipeSignature: recipeSignature,
                assignment: assignment, linkDescription: linkDescription));
        }
        records = list;
        return true;
    }

    /// <summary>Synthesizes a stable <see cref="ScribeAssignment.BatchId"/> for a pre-v3 record from
    /// (assigner, target, date), so old multi-item batches keep grouping. A display-grouping key only, so
    /// truncated SHA-256 is just a convenient stable hash.</summary>
    private static Guid DeriveLegacyBatchId(string assignerUid, string targetPlayerUid, string assignedDate)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{assignerUid} {targetPlayerUid} {assignedDate}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>Writes a nullable string as a has-value flag plus the string when present.</summary>
    private static void WriteOptionalString(BinaryWriter w, string? value)
    {
        bool hasValue = value is not null;
        w.Write(hasValue);
        if (hasValue) w.Write(value!);
    }

    /// <summary>Reads a <see cref="WriteOptionalString"/> value written from <paramref name="minVersion"/>
    /// on, or null when the blob predates it.</summary>
    private static string? ReadOptionalString(BinaryReader r, int version, int minVersion = 4) =>
        version >= minVersion ? (r.ReadBoolean() ? r.ReadString() : null) : null;

    /// <summary>Writes a nullable double as a has-value flag plus the value, so null stays distinct from a
    /// real <c>Calendar.TotalDays</c> of 0.</summary>
    private static void WriteOptionalDouble(BinaryWriter w, double? value)
    {
        bool hasValue = value.HasValue;
        w.Write(hasValue);
        if (hasValue) w.Write(value!.Value);
    }

    /// <summary>Reads a <see cref="WriteOptionalDouble"/> value (written from v9), or null when the blob
    /// predates timestamps.</summary>
    private static double? ReadOptionalDouble(BinaryReader r, int version, int minVersion = 9) =>
        version >= minVersion ? (r.ReadBoolean() ? r.ReadDouble() : null) : null;

    private static byte[] ReadExactly(BinaryReader r, int count)
    {
        var buffer = r.ReadBytes(count);
        if (buffer.Length != count) throw new EndOfStreamException();
        return buffer;
    }
}
