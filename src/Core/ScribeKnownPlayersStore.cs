using System.Text;

namespace Scribe.Core;

/// <summary>
/// Server-side registry of every player who has ever connected to the world, keyed by player UID and
/// their most-recently-observed display name (persist-known-players-for-assignment). Backs the
/// Assignment Desk's target-player picker so a player who has disconnected — regardless of how they
/// connected (direct join, singleplayer opened to LAN, or a dedicated server) — remains a selectable
/// target across sessions, unlike the live-only <c>capi.World.AllOnlinePlayers</c> the picker used to
/// build its list from alone.
///
/// Game-agnostic (pure BCL) so it's unit-testable without a game install; the Mod layer's
/// <c>OnPlayerNowPlaying</c> hook upserts into it and owns its savegame persistence, mirroring
/// <see cref="ScribePlayerLocationStore"/>'s pattern exactly. Unbounded growth is an accepted trade-off
/// (see that class's own precedent) — pruning is out of scope.
/// </summary>
public sealed class ScribeKnownPlayersStore
{
    private readonly Dictionary<string, string> _knownPlayers = new();

    /// <summary>Records <paramref name="displayName"/> as <paramref name="playerUid"/>'s most-recently-
    /// observed name, overwriting any prior value for that uid rather than duplicating the entry. A no-op
    /// for a blank uid.</summary>
    public void Upsert(string playerUid, string displayName)
    {
        if (string.IsNullOrWhiteSpace(playerUid)) return;
        _knownPlayers[playerUid] = displayName;
    }

    /// <summary>Every known player as (uid, last-seen display name), in no particular order — callers
    /// that need a stable order (the picker) sort it themselves at read time.</summary>
    public List<(string Uid, string Name)> Snapshot() =>
        _knownPlayers.Select(kv => (kv.Key, kv.Value)).ToList();

    /// <summary>O(1) membership check against the backing dictionary — for a per-send validation guard
    /// that doesn't need (and shouldn't allocate) a full <see cref="Snapshot"/>.</summary>
    public bool Contains(string uid) => _knownPlayers.ContainsKey(uid);

    // ---------------- Persistence ----------------

    private static readonly byte[] Magic = "SKPS"u8.ToArray();
    private const byte Version = 1;

    /// <summary>Serializes every known player for the savegame blob (and, doubling as the sync message
    /// payload, for pushing the registry to clients).</summary>
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

    /// <summary>Replaces the in-memory registry from the persisted/synced blob. A null/malformed blob
    /// leaves the registry empty rather than throwing.</summary>
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
