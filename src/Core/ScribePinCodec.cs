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
/// Accepted-version window:
///   Current : v2 — appended per-pin <see cref="ScribePinnedRef.Kind"/> (1 byte) and
///                  <see cref="ScribePinnedRef.LinkTarget"/> (bool + optional string), so the HUD can
///                  treat a pinned Link as a Handbook hyperlink (add-tracker-link-tasks 5.5).
///   Prior   : v1 — migrated by <see cref="ApplyV1ToV2Migrations"/> (Kind→Task, LinkTarget→null).
///   Older   : rejected.
///
/// Per-pin field history (in serialized order): OwnerDocId, TaskId, PinnedAtTotalHours, Orphaned,
/// LastKnownDone, LastKnownText (v1); Kind, LinkTarget (added v2).
/// </summary>
public static class ScribePinCodec
{
    private static readonly byte[] ListMagic = "SPIN"u8.ToArray();
    private static readonly byte[] StoreMagic = "SPST"u8.ToArray();

    /// <summary>Version of the pin-list blobs (SPIN/SPST). Bumped to 2 for the appended per-pin
    /// <see cref="ScribePinnedRef.Kind"/> + <see cref="ScribePinnedRef.LinkTarget"/> fields
    /// (add-tracker-link-tasks 5.5).</summary>
    private const byte PinVersion = 2;

    /// <summary>
    /// The immediately prior pin-list version the reader still accepts (and migrates). Bumped alongside
    /// <see cref="PinVersion"/> to 1 → 2; v1 blobs are upgraded by <see cref="ApplyV1ToV2Migrations"/>.
    /// See docs/CODEC-MIGRATION.md.
    /// </summary>
    private const byte PriorPinVersion = 1;

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
            if (version != PinVersion && version != PriorPinVersion) return false;
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
            if (version != PinVersion && version != PriorPinVersion) return false;

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

            // v2 appended per-pin Kind + LinkTarget (add-tracker-link-tasks 5.5); v1 blobs have neither, so
            // the named migration defaults them (Kind→Task, LinkTarget→null). See docs/CODEC-MIGRATION.md.
            if (version == PinVersion)
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
            else
            {
                ApplyV1ToV2Migrations(out var kind, out var linkTarget);
                pin.Kind = kind;
                pin.LinkTarget = linkTarget;
            }

            list.Add(pin);
        }
        pins = list;
        return true;
    }

    /// <summary>
    /// Migration step for pin-list bytes written in v1 (the prior version): v1 has no per-pin
    /// <see cref="ScribePinnedRef.Kind"/> or <see cref="ScribePinnedRef.LinkTarget"/>, so a v1 pin is
    /// upgraded by defaulting them — every pre-v2 pin reads as an ordinary <see cref="ScribeBlockKind.Task"/>
    /// with no link target, which is exactly correct (Tracker/Link pins only exist from v2 on). See
    /// docs/CODEC-MIGRATION.md for the pattern.
    /// </summary>
    private static void ApplyV1ToV2Migrations(out ScribeBlockKind kind, out string? linkTarget)
    {
        kind = ScribeBlockKind.Task;
        linkTarget = null;
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
