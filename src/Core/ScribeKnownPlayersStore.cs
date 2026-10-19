using System.Text;

namespace Scribe.Core;

/// <summary>
/// Server-side registry of every player who has ever connected, keyed by UID with their most-recently-
/// observed display name. Backs the Assignment Desk's target picker so a disconnected player stays
/// selectable across sessions, unlike the live-only <c>capi.World.AllOnlinePlayers</c>. Pure BCL; the Mod
/// layer's <c>OnPlayerNowPlaying</c> hook upserts into it and owns persistence, mirroring
/// <see cref="ScribePlayerLocationStore"/>. Growth is unbounded; pruning is out of scope.
/// </summary>
public sealed class ScribeKnownPlayersStore
{
    private readonly Dictionary<string, string> _knownPlayers = new();

    /// <summary>Records <paramref name="displayName"/> as the uid's latest name, overwriting any prior value. No-op for a blank uid.</summary>
    public void Upsert(string playerUid, string displayName)
    {
        if (string.IsNullOrWhiteSpace(playerUid)) return;
        _knownPlayers[playerUid] = displayName;
    }

    /// <summary>Every known player as (uid, last-seen display name), unordered; callers sort at read time.</summary>
    public List<(string Uid, string Name)> Snapshot() =>
        _knownPlayers.Select(kv => (kv.Key, kv.Value)).ToList();

    /// <summary>O(1) membership check that avoids allocating a full <see cref="Snapshot"/>.</summary>
    public bool Contains(string uid) => _knownPlayers.ContainsKey(uid);

    // ---------------- Persistence ----------------

    private static readonly byte[] Magic = "SKPS"u8.ToArray();
    private const byte Version = 1;

    /// <summary>Serializes every known player for the savegame blob (also the client sync payload).</summary>
    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(_knownPlayers.Count);
            foreach (var (uid, name) in _knownPlayers)
            {
                w.Write(uid);
                w.Write(name);
            }
        }
        return ms.ToArray();
    }

    /// <summary>Replaces the registry from a persisted/synced blob; a null or malformed blob leaves it empty.</summary>
    public void LoadFrom(byte[]? bytes)
    {
        _knownPlayers.Clear();
        if (bytes is null) return;
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            var magic = r.ReadBytes(Magic.Length);
            if (!magic.AsSpan().SequenceEqual(Magic)) return;
            byte version = r.ReadByte();
            if (version != Version) return;

            int count = r.ReadInt32();
            if (count < 0 || count > 100_000) return;
            for (int i = 0; i < count; i++)
            {
                string uid = r.ReadString();
                string name = r.ReadString();
                _knownPlayers[uid] = name;
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            // Malformed — leave the registry empty.
            _knownPlayers.Clear();
        }
    }
}
