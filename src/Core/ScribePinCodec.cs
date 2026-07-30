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
/// </summary>
public static class ScribePinCodec
{
    private static readonly byte[] ListMagic = "SPIN"u8.ToArray();
    private static readonly byte[] StoreMagic = "SPST"u8.ToArray();

    /// <summary>Version of the pin-list blobs (SPIN/SPST). Unchanged — pins didn't change shape.</summary>
    private const byte PinVersion = 1;

    /// <summary>
    /// The immediately prior pin-list version. Equal to <see cref="PinVersion"/> until a pin-format
    /// change occurs — at that point bump <see cref="PinVersion"/>, set this to the old value, and
    /// add upgrade logic to <see cref="ApplyPinMigrations"/>. See docs/CODEC-MIGRATION.md.
    /// </summary>
    private const byte PriorPinVersion = PinVersion;

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
            if (!TryReadPinList(r, bytes.Length, out var list)) return false;
            ApplyPinMigrations((byte)version, ref list);
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
                if (!TryReadPinList(r, bytes.Length, out var list)) return false;
                ApplyPinMigrations((byte)version, ref list);
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
        }
    }

    private static bool TryReadPinList(BinaryReader r, int totalBytes, out List<ScribePinnedRef> pins)
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
            list.Add(pin);
        }
        pins = list;
        return true;
    }

    /// <summary>
    /// Migration steps for pin-list bytes written in the immediately prior version. Currently a
    /// no-op because <see cref="PriorPinVersion"/> equals <see cref="PinVersion"/> — no pin-format
    /// fields have changed yet. When the first pin-format change arrives: bump
    /// <see cref="PinVersion"/>, set <see cref="PriorPinVersion"/> to the old value, and add the
    /// upgrade logic here. See docs/CODEC-MIGRATION.md for the pattern.
    /// </summary>
    private static void ApplyPinMigrations(byte version, ref List<ScribePinnedRef> pins)
    {
        // No migration needed while PriorPinVersion == PinVersion.
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
