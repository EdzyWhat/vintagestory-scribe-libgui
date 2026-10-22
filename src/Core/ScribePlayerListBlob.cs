using System.Text;

namespace Scribe.Core;

/// <summary>
/// The framing shared by the per-player list codecs (<see cref="ScribePinCodec"/>,
/// <see cref="ScribeQuestDecisionCodec"/>). Each codec produces two blobs, a single player's list and a whole
/// store keyed by player uid. Both start with a 4-byte magic and a 1-byte version; the store adds a player
/// count and then, per player, a uid followed by that player's list.
///
/// Reading is fail-safe: a wrong magic, an unsupported version, an out-of-range count, an over-long uid or a
/// truncated stream returns false and never throws or over-allocates. What a list holds is the codec's own
/// business, supplied as the read and write delegates.
/// </summary>
internal static class ScribePlayerListBlob
{
    /// <summary>Upper bound on players in a store blob (allocation guard).</summary>
    public const int MaxPlayers = 10_000;

    /// <summary>Reads one player's list. <paramref name="totalBytes"/> bounds element counts, and
    /// <paramref name="version"/> is the blob's version for codecs whose element layout depends on it.</summary>
    public delegate bool ListReader<T>(BinaryReader r, int totalBytes, byte version, out List<T> list);

    public static byte[] SerializeList<T>(byte[] magic, byte version, IReadOnlyList<T> items,
        Action<BinaryWriter, IReadOnlyList<T>> writeList)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(magic);
            w.Write(version);
            writeList(w, items);
        }
        return ms.ToArray();
    }

    public static bool TryDeserializeList<T>(byte[]? bytes, byte[] magic, byte minVersion, byte maxVersion,
        ListReader<T> readList, out List<T>? items)
    {
        items = null;
        if (bytes is null) return false;
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            int version = ReadHeader(r, magic);
            if (version < minVersion || version > maxVersion) return false;
            if (!readList(r, bytes.Length, (byte)version, out var list)) return false;
            items = list;
            return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            items = null;
            return false;
        }
    }

    public static byte[] SerializeStore<T>(byte[] magic, byte version, IReadOnlyDictionary<string, List<T>> store,
        Action<BinaryWriter, IReadOnlyList<T>> writeList)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(magic);
            w.Write(version);
            w.Write(store.Count);
            foreach (var (uid, items) in store)
            {
                w.Write(uid);
                writeList(w, items);
            }
        }
        return ms.ToArray();
    }

    public static bool TryDeserializeStore<T>(byte[]? bytes, byte[] magic, byte minVersion, byte maxVersion,
        int maxUidLength, ListReader<T> readList, out Dictionary<string, List<T>>? store)
    {
        store = null;
        if (bytes is null) return false;
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            int version = ReadHeader(r, magic);
            if (version < minVersion || version > maxVersion) return false;

            int playerCount = r.ReadInt32();
            if (playerCount < 0 || playerCount > bytes.Length || playerCount > MaxPlayers) return false;

            var result = new Dictionary<string, List<T>>(playerCount);
            for (int i = 0; i < playerCount; i++)
            {
                string uid = r.ReadString();
                if (uid.Length > maxUidLength) return false;
                if (!readList(r, bytes.Length, (byte)version, out var list)) return false;
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

    /// <summary>Reads the 4-byte magic + 1-byte version. Returns the version, or -1 on a magic mismatch.</summary>
    private static int ReadHeader(BinaryReader r, byte[] expectedMagic)
    {
        var magic = r.ReadBytes(expectedMagic.Length);
        if (!magic.AsSpan().SequenceEqual(expectedMagic)) return -1;
        return r.ReadByte();
    }
}
