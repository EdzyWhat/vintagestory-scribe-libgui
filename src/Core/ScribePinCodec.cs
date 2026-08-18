using System;
using System.Text;

namespace Scribe.Core;

/// <summary>
/// Serializes the per-player pin data to byte arrays and back, for both network sync and save-game
/// persistence. Like <see cref="ScribeDocumentCodec"/> it is a hand-rolled, versioned, fail-safe
/// binary format (returns false on anything malformed rather than throwing), so Core needs no
/// external dependency and the same bytes are trusted-but-client input the server re-persists.
///
/// Two blob shapes, each with its own 4-byte magic and 1-byte version:
///   SPIN — one player's <see cref="ScribePinnedRef"/> list (the server→client per-player push).
///   SPST — the whole pin store, <c>Dictionary&lt;playerUid, List&lt;ScribePinnedRef&gt;&gt;</c> (savegame blob).
///
/// Per-player <see cref="ScribePlayerSettings"/> are NOT serialized here: they are client-local
/// display/behavior preferences persisted as JSON via the mod's client config (never server-synced),
/// so the former SPSE/SPSS settings blobs were removed with the server settings layer.
///
/// Guids are written as 16 raw bytes (protobuf-agnostic and compact). Caps bound every read so a
/// malformed or hostile payload can't allocate without limit.
/// See <see href="../docs/CODEC-MIGRATION.md">docs/CODEC-MIGRATION.md</see> for the migration-step pattern and how to add a new version.
///
/// Accepted-version window (append-only, read PROGRESSIVELY by version rather than as a two-version
/// window): each version only ADDS trailing per-pin fields, so a reader for the current version reads
/// the base fields for every accepted version and then reads each later version's extra fields only when
/// the blob's version is at least that high. This lets shipped v1 pins keep loading unchanged when v2
/// (WIP-only, never released) and v3 add fields — a naive "current + immediately-prior" window would
/// have dropped v1 pins (data loss) once v3 landed.
///   Current : v5 — appended per-pin <see cref="ScribePinnedRef.Depth"/> (int), the pinned task's subtask
///                  depth, so the HUD and Pin Tab indent a pinned subtask like the other surfaces
///                  (add-crafting-tasks / task-subtasks 5.1).
///   v4 — appended per-pin <see cref="ScribePinnedRef.LinkLabel"/> (bool + optional string), the
///                  display title of a guide-page Link, so the HUD and Pin Tab can render a pinned
///                  guide-page Link (a "page:"-prefixed LinkTarget has no item to resolve a name from)
///                  (add-tracker-link-tasks 7.6).
///   v3 — appended per-pin <see cref="ScribePinnedRef.TargetItemCode"/> (bool + optional string),
///                  <see cref="ScribePinnedRef.TargetQuantity"/> (int) and
///                  <see cref="ScribePinnedRef.CurrentQuantity"/> (int), so the HUD and Pin Tab can render a
///                  pinned Tracker's icon + name + have/need counter (add-tracker-link-tasks 7.8).
///   v2 — appended per-pin <see cref="ScribePinnedRef.Kind"/> (1 byte) and
///                  <see cref="ScribePinnedRef.LinkTarget"/> (bool + optional string), for pinned Links
///                  (add-tracker-link-tasks 5.5). Never shipped (WIP branch only).
///   Older   : v1 — no per-pin Kind/LinkTarget/Tracker/LinkLabel fields; migrated by <see cref="ApplyPreV2Defaults"/>
///                  (Kind→Task, everything else null/default). Still accepted (this is the shipped format).
///   Older still : rejected.
///
/// Per-pin field history (in serialized order): OwnerDocId, TaskId, PinnedAtTotalHours, Orphaned,
/// LastKnownDone, LastKnownText (v1); Kind, LinkTarget (added v2); TargetItemCode, TargetQuantity,
/// CurrentQuantity (added v3); LinkLabel (added v4); Depth (added v5).
/// </summary>
public static class ScribePinCodec
{
    private static readonly byte[] ListMagic = "SPIN"u8.ToArray();
    private static readonly byte[] StoreMagic = "SPST"u8.ToArray();

    /// <summary>Version of the pin-list blobs (SPIN/SPST). Bumped to 5 for the appended per-pin
    /// <see cref="ScribePinnedRef.Depth"/> subtask depth (add-crafting-tasks / task-subtasks 5.1);
    /// v4 added the <see cref="ScribePinnedRef.LinkLabel"/> guide-page Link title.</summary>
    private const byte PinVersion = 5;

    /// <summary>
    /// The OLDEST pin-list version the reader still accepts. Reads are progressive (append-only): any
    /// version in <c>[MinPinVersion, PinVersion]</c> is accepted, and each later version's trailing fields
    /// are read only when the blob's version is at least that high. v1 is the shipped format and must keep
    /// loading; v2 was WIP-branch-only. See docs/CODEC-MIGRATION.md.
    /// </summary>
    private const byte MinPinVersion = 1;

    /// <summary>Hard upper bound on the number of pins a single player may hold, enforced on every
    /// list/store read so a malformed or hostile payload cannot grow a persisted/synced set without
    /// limit. Generous relative to any realistic hand-curated pin set.</summary>
    public const int MaxPinsPerPlayer = 500;

    /// <summary>Hard upper bound on the number of players in a persisted store blob — an allocation
    /// guard for the save-game read path.</summary>
    public const int MaxPlayers = 10_000;

    /// <summary>Hard upper bound on a player-uid string length, in characters (allocation guard).</summary>
    public const int MaxUidLength = 256;

    // ---- SPIN: one player's pin list (network) ----

    public static byte[] SerializeList(IReadOnlyList<ScribePinnedRef> pins)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(ListMagic);
            w.Write(PinVersion);
            WritePinList(w, pins);
        }
        return ms.ToArray();
    }

    public static bool TryDeserializeList(byte[]? bytes, out List<ScribePinnedRef>? pins)
    {
        pins = null;
        if (bytes is null) return false;
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            // See docs/CODEC-MIGRATION.md for the migration-step pattern.
            int version = ReadHeader(r, ListMagic);
            if (version < MinPinVersion || version > PinVersion) return false;
            if (!TryReadPinList(r, bytes.Length, (byte)version, out var list)) return false;
            pins = list;
            return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            pins = null;
            return false;
        }
    }

    // ---- SPST: the whole pin store (savegame) ----

    public static byte[] SerializeStore(IReadOnlyDictionary<string, List<ScribePinnedRef>> store)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(StoreMagic);
            w.Write(PinVersion);
            w.Write(store.Count);
            foreach (var (uid, pins) in store)
            {
                w.Write(uid);
                WritePinList(w, pins);
            }
        }
        return ms.ToArray();
    }

    public static bool TryDeserializeStore(byte[]? bytes, out Dictionary<string, List<ScribePinnedRef>>? store)
    {
        store = null;
        if (bytes is null) return false;
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            // See docs/CODEC-MIGRATION.md for the migration-step pattern.
            int version = ReadHeader(r, StoreMagic);
            if (version < MinPinVersion || version > PinVersion) return false;

            int playerCount = r.ReadInt32();
            if (playerCount < 0 || playerCount > bytes.Length || playerCount > MaxPlayers) return false;

            var result = new Dictionary<string, List<ScribePinnedRef>>(playerCount);
            for (int i = 0; i < playerCount; i++)
            {
                string uid = r.ReadString();
                if (uid.Length > MaxUidLength) return false;
                if (!TryReadPinList(r, bytes.Length, (byte)version, out var list)) return false;
                result[uid] = list;
            }
            store = result;
            return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            store = null;
            return false;
        }
    }

    // ---- shared helpers ----

    /// <summary>Reads the 4-byte magic + 1-byte version. Returns the version, or -1 if the magic
    /// doesn't match (caller then decides which versions it accepts).</summary>
    private static int ReadHeader(BinaryReader r, byte[] expectedMagic)
    {
        var magic = r.ReadBytes(expectedMagic.Length);
        if (!magic.AsSpan().SequenceEqual(expectedMagic)) return -1;
        return r.ReadByte();
    }

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
            // v2 appended fields (add-tracker-link-tasks 5.5): the pin's kind, and — only for a Link — its
            // link target (a nullable string, written as a presence bool + the value when present).
            w.Write((byte)pin.Kind);
            bool hasLinkTarget = pin.LinkTarget != null;
            w.Write(hasLinkTarget);
            if (hasLinkTarget) w.Write(pin.LinkTarget!);
            // v3 appended fields (add-tracker-link-tasks 7.8): a Tracker's target item code (nullable string,
            // presence bool + value) plus its target/current quantities so a pinned Tracker renders its
            // icon + name + have/need counter without the source document loaded.
            bool hasTargetItemCode = pin.TargetItemCode != null;
            w.Write(hasTargetItemCode);
            if (hasTargetItemCode) w.Write(pin.TargetItemCode!);
            w.Write(pin.TargetQuantity);
            w.Write(pin.CurrentQuantity);
            // v4 appended field (add-tracker-link-tasks 7.6): a guide-page Link's display title (nullable
            // string, presence bool + value) so a pinned guide-page Link renders its name without an item.
            bool hasLinkLabel = pin.LinkLabel != null;
            w.Write(hasLinkLabel);
            if (hasLinkLabel) w.Write(pin.LinkLabel!);
            // v5 appended field (add-crafting-tasks / task-subtasks 5.1): the pinned task's subtask depth,
            // so a pinned subtask indents on the HUD/Pin Tab like the other surfaces.
            w.Write(pin.Depth);
        }
    }

    private static bool TryReadPinList(BinaryReader r, int totalBytes, byte version, out List<ScribePinnedRef> pins)
    {
        pins = new List<ScribePinnedRef>();
        int count = r.ReadInt32();
        // Reject a negative count, one that can't physically fit in the buffer (allocation guard),
        // or one over the per-player cap.
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

            // Progressive, append-only reads by version. v1 (the shipped format) has none of the fields
            // below, so the named migration defaults them (Kind→Task, everything else null/default). v2
            // added Kind + LinkTarget; v3 added the Tracker fields. Each block reads only when the blob is
            // at least that version, so a v1/v2 blob stops before the fields it never wrote. See
            // docs/CODEC-MIGRATION.md.
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
                // Clamp on read to the one-level subtask contract, matching ScribeBlock.Depth, so a
                // malformed/hostile blob can't smuggle a depth-2+ pin past the reader.
                pin.Depth = Math.Clamp(r.ReadInt32(), 0, 1);
            }

            list.Add(pin);
        }
        pins = list;
        return true;
    }

    /// <summary>
    /// Migration step for pin-list bytes older than the field being read: seeds the defaults every pre-v2
    /// pin needs before the progressive read layers on any version-specific fields. v1 (the shipped format)
    /// has no per-pin <see cref="ScribePinnedRef.Kind"/>, <see cref="ScribePinnedRef.LinkTarget"/>, or the
    /// v3 Tracker fields, so it reads as an ordinary <see cref="ScribeBlockKind.Task"/> with no link/tracker
    /// data — exactly correct (Tracker/Link pins only exist from v2/v3 on). Applied unconditionally so a v2
    /// blob (which then overwrites Kind/LinkTarget) still gets the v3 Tracker defaults and the v4 LinkLabel
    /// default. See docs/CODEC-MIGRATION.md for the pattern.
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

    /// <summary>Reads exactly <paramref name="count"/> bytes or throws <see cref="EndOfStreamException"/>
    /// (caught as a malformed-input failure). Guards against <see cref="BinaryReader.ReadBytes"/>
    /// returning a short buffer at end-of-stream, which would misread a truncated Guid.</summary>
    private static byte[] ReadExactly(BinaryReader r, int count)
    {
        var buffer = r.ReadBytes(count);
        if (buffer.Length != count) throw new EndOfStreamException();
        return buffer;
    }
}
