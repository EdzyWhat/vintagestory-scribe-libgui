namespace Scribe.Core;

/// <summary>
/// Serializes per-player pin data to byte arrays and back, for network sync and save-game persistence.
/// Like <see cref="ScribeDocumentCodec"/> it is a hand-rolled, versioned, fail-safe binary format
/// (returns false on anything malformed rather than throwing); the server re-persists client input, so
/// every read is capped against hostile payloads.
///
/// Two blob shapes, each with a 4-byte magic and 1-byte version:
///   SPIN — one player's <see cref="ScribePinnedRef"/> list (server→client push).
///   SPST — the whole store, <c>Dictionary&lt;playerUid, List&lt;ScribePinnedRef&gt;&gt;</c> (savegame blob).
///
/// <see cref="ScribePlayerSettings"/> are NOT serialized here (client-local JSON). Guids are 16 raw bytes.
/// See <see href="../docs/CODEC-MIGRATION.md">docs/CODEC-MIGRATION.md</see> for adding a version.
///
/// Reads are PROGRESSIVE and append-only: each version only ADDS trailing per-pin fields, so the reader
/// reads the v1 base fields for every accepted version, then each later version's extra fields only when
/// the blob's version is at least that high. Versions older than <c>MinPinVersion</c> are rejected.
///
/// Per-pin fields in serialized order: OwnerDocId, TaskId, PinnedAtTotalHours, Orphaned, LastKnownDone,
/// LastKnownText (v1); Kind, LinkTarget (v2); TargetItemCode, TargetQuantity, CurrentQuantity (v3);
/// LinkLabel (v4); Depth (v5); IsAcceptedAssignment (v6); AssignerUid, AssignedDate, AcceptedDate (v7);
/// ExtraInfo (v8); AssignedTimestamp, AcceptedTimestamp (v9). Optional fields are a presence bool plus
/// the value; a missing timestamp is distinct from <c>Calendar.TotalDays == 0</c>. v1 pins are migrated
/// by <see cref="ApplyPreV2Defaults"/>.
/// </summary>
public static class ScribePinCodec
{
    private static readonly byte[] ListMagic = "SPIN"u8.ToArray();
    private static readonly byte[] StoreMagic = "SPST"u8.ToArray();

    /// <summary>Version of the pin-list blobs (SPIN/SPST); see the type summary for the per-version fields.</summary>
    private const byte PinVersion = 9;

    /// <summary>
    /// The OLDEST pin-list version still accepted; any version in <c>[MinPinVersion, PinVersion]</c> is read
    /// progressively. v1 is the shipped format and must keep loading.
    /// </summary>
    private const byte MinPinVersion = 1;

    /// <summary>Hard cap on pins per player, enforced on every read so a hostile payload cannot grow the set without limit.</summary>
    public const int MaxPinsPerPlayer = 500;

    /// <summary>Hard cap on a player-uid length in characters (allocation guard).</summary>
    public const int MaxUidLength = 256;

    // ---- SPIN: one player's pin list (network) ----

    public static byte[] SerializeList(IReadOnlyList<ScribePinnedRef> pins)
        => ScribePlayerListBlob.SerializeList(ListMagic, PinVersion, pins, WritePinList);

    public static bool TryDeserializeList(byte[]? bytes, out List<ScribePinnedRef>? pins)
        => ScribePlayerListBlob.TryDeserializeList<ScribePinnedRef>(bytes, ListMagic, MinPinVersion, PinVersion,
            TryReadPinList, out pins);

    // ---- SPST: the whole pin store (savegame) ----

    public static byte[] SerializeStore(IReadOnlyDictionary<string, List<ScribePinnedRef>> store)
        => ScribePlayerListBlob.SerializeStore(StoreMagic, PinVersion, store, WritePinList);

    public static bool TryDeserializeStore(byte[]? bytes, out Dictionary<string, List<ScribePinnedRef>>? store)
        => ScribePlayerListBlob.TryDeserializeStore<ScribePinnedRef>(bytes, StoreMagic, MinPinVersion, PinVersion,
            MaxUidLength, TryReadPinList, out store);

    // ---- shared helpers ----

    private static void WritePinList(BinaryWriter w, IReadOnlyList<ScribePinnedRef> pins)
    {
        w.Write(pins.Count);
        foreach (var pin in pins)
        {
            w.Write(pin.OwnerDocId.ToByteArray());
            w.Write(pin.TaskId.ToByteArray());
            w.Write(pin.PinnedAtTotalHours);
            w.Write(pin.Orphaned);
            w.Write(pin.LastKnownDone);
            w.Write(pin.LastKnownText);
            w.Write((byte)pin.Kind);
            bool hasLinkTarget = pin.LinkTarget != null;
            w.Write(hasLinkTarget);
            if (hasLinkTarget) w.Write(pin.LinkTarget!);
            bool hasTargetItemCode = pin.TargetItemCode != null;
            w.Write(hasTargetItemCode);
            if (hasTargetItemCode) w.Write(pin.TargetItemCode!);
            w.Write(pin.TargetQuantity);
            w.Write(pin.CurrentQuantity);
            bool hasLinkLabel = pin.LinkLabel != null;
            w.Write(hasLinkLabel);
            if (hasLinkLabel) w.Write(pin.LinkLabel!);
            w.Write(pin.Depth);
            w.Write(pin.IsAcceptedAssignment);
            w.Write(pin.AssignerUid);
            w.Write(pin.AssignedDate);
            bool hasAcceptedDate = pin.AcceptedDate != null;
            w.Write(hasAcceptedDate);
            if (hasAcceptedDate) w.Write(pin.AcceptedDate!);
            bool hasExtraInfo = pin.ExtraInfo != null;
            w.Write(hasExtraInfo);
            if (hasExtraInfo) w.Write(pin.ExtraInfo!);
            bool hasAssignedTimestamp = pin.AssignedTimestamp.HasValue;
            w.Write(hasAssignedTimestamp);
            if (hasAssignedTimestamp) w.Write(pin.AssignedTimestamp!.Value);
            bool hasAcceptedTimestamp = pin.AcceptedTimestamp.HasValue;
            w.Write(hasAcceptedTimestamp);
            if (hasAcceptedTimestamp) w.Write(pin.AcceptedTimestamp!.Value);
        }
    }

    private static bool TryReadPinList(BinaryReader r, int totalBytes, byte version, out List<ScribePinnedRef> pins)
    {
        pins = new List<ScribePinnedRef>();
        int count = r.ReadInt32();
        // Reject a negative count, one that can't fit in the buffer, or one over the per-player cap.
        if (count < 0 || count > totalBytes || count > MaxPinsPerPlayer) return false;

        var list = new List<ScribePinnedRef>(count);
        for (int i = 0; i < count; i++)
        {
            var pin = new ScribePinnedRef
            {
                OwnerDocId = new Guid(ReadExactly(r, 16)),
                TaskId = new Guid(ReadExactly(r, 16)),
                PinnedAtTotalHours = r.ReadDouble(),
                Orphaned = r.ReadBoolean(),
                LastKnownDone = r.ReadBoolean(),
            };
            string text = r.ReadString();
            if (text.Length > ScribeDocumentCodec.MaxTextLength) return false;
            pin.LastKnownText = text;

            // Progressive reads by version: v1 has none of the fields below, so the migration defaults
            // them; each block reads only when the blob is at least that version.
            ApplyPreV2Defaults(pin);
            if (version >= 2)
            {
                pin.Kind = (ScribeBlockKind)r.ReadByte();
                bool hasLinkTarget = r.ReadBoolean();
                if (hasLinkTarget)
                {
                    string linkTarget = r.ReadString();
                    if (linkTarget.Length > ScribeDocumentCodec.MaxTextLength) return false;
                    pin.LinkTarget = linkTarget;
                }
            }
            if (version >= 3)
            {
                bool hasTargetItemCode = r.ReadBoolean();
                if (hasTargetItemCode)
                {
                    string targetItemCode = r.ReadString();
                    if (targetItemCode.Length > ScribeDocumentCodec.MaxTextLength) return false;
                    pin.TargetItemCode = targetItemCode;
                }
                pin.TargetQuantity = r.ReadInt32();
                pin.CurrentQuantity = r.ReadInt32();
            }
            if (version >= 4)
            {
                bool hasLinkLabel = r.ReadBoolean();
                if (hasLinkLabel)
                {
                    string linkLabel = r.ReadString();
                    if (linkLabel.Length > ScribeDocumentCodec.MaxTextLength) return false;
                    pin.LinkLabel = linkLabel;
                }
            }
            if (version >= 5)
            {
                // Clamp to the one-level subtask contract (ScribeBlock.Depth) so a hostile blob can't smuggle in a depth-2+ pin.
                pin.Depth = Math.Clamp(r.ReadInt32(), 0, 1);
            }
            if (version >= 6)
            {
                pin.IsAcceptedAssignment = r.ReadBoolean();
            }
            if (version >= 7)
            {
                string assignerUid = r.ReadString();
                if (assignerUid.Length > MaxUidLength) return false;
                pin.AssignerUid = assignerUid;

                string assignedDate = r.ReadString();
                if (assignedDate.Length > ScribeDocumentCodec.MaxTextLength) return false;
                pin.AssignedDate = assignedDate;

                bool hasAcceptedDate = r.ReadBoolean();
                if (hasAcceptedDate)
                {
                    string acceptedDate = r.ReadString();
                    if (acceptedDate.Length > ScribeDocumentCodec.MaxTextLength) return false;
                    pin.AcceptedDate = acceptedDate;
                }
            }
            if (version >= 8)
            {
                bool hasExtraInfo = r.ReadBoolean();
                if (hasExtraInfo)
                {
                    string extraInfo = r.ReadString();
                    if (extraInfo.Length > ScribeDocumentCodec.MaxTaskTextLength) return false;
                    pin.ExtraInfo = extraInfo;
                }
            }
            if (version >= 9)
            {
                if (r.ReadBoolean()) pin.AssignedTimestamp = r.ReadDouble();
                if (r.ReadBoolean()) pin.AcceptedTimestamp = r.ReadDouble();
            }

            list.Add(pin);
        }
        pins = list;
        return true;
    }

    /// <summary>
    /// Seeds the defaults every pre-v2 pin needs before the progressive read layers on later fields. Applied
    /// unconditionally so a v2 blob (which overwrites Kind/LinkTarget) still gets the v3/v4 defaults.
    /// </summary>
    private static void ApplyPreV2Defaults(ScribePinnedRef pin)
    {
        pin.Kind = ScribeBlockKind.Task;
        pin.LinkTarget = null;
        pin.TargetItemCode = null;
        pin.TargetQuantity = 1;
        pin.CurrentQuantity = 0;
        pin.LinkLabel = null;
        pin.Depth = 0;
    }

    /// <summary>Reads exactly <paramref name="count"/> bytes or throws <see cref="EndOfStreamException"/>;
    /// <see cref="BinaryReader.ReadBytes"/> alone would return a short buffer and misread a truncated Guid.</summary>
    private static byte[] ReadExactly(BinaryReader r, int count)
    {
        var buffer = r.ReadBytes(count);
        if (buffer.Length != count) throw new EndOfStreamException();
        return buffer;
    }
}
