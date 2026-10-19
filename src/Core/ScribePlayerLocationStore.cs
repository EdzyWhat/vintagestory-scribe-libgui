using System.Text;

namespace Scribe.Core;

/// <summary>
/// Server-side owner of every player's last-known world position, captured on logout; the Hybrid range
/// check's only position source for an offline target. Overwritten on each logout, never updated while
/// online. Pure BCL; the Mod layer's <c>OnPlayerDisconnect</c> hook calls it and owns persistence,
/// mirroring <see cref="ScribeAssignmentStore"/>/<c>ScribePinStore</c>.
/// </summary>
public sealed class ScribePlayerLocationStore
{
    private readonly Dictionary<string, ScribeWorldPosition> _lastKnown = new();

    /// <summary>Records the player's position as last-known, overwriting any prior value. No-op for a blank uid.</summary>
    public void SetLastKnown(string playerUid, ScribeWorldPosition position)
    {
        if (string.IsNullOrWhiteSpace(playerUid)) return;
        _lastKnown[playerUid] = position;
    }

    /// <summary>The player's last-captured position, or false if never recorded.</summary>
    public bool TryGetLastKnown(string playerUid, out ScribeWorldPosition position)
        => _lastKnown.TryGetValue(playerUid, out position);

    // ---------------- Persistence ----------------

    private static readonly byte[] Magic = "SPLC"u8.ToArray();
    private const byte Version = 1;

    /// <summary>Serializes every tracked position for the savegame blob.</summary>
    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(_lastKnown.Count);
            foreach (var (uid, pos) in _lastKnown)
            {
                w.Write(uid);
                w.Write(pos.X);
                w.Write(pos.Y);
                w.Write(pos.Z);
            }
        }
        return ms.ToArray();
    }

    /// <summary>Replaces the store from the persisted blob; a null or malformed blob leaves it empty.</summary>
    public void LoadFrom(byte[]? bytes)
    {
        _lastKnown.Clear();
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
                double x = r.ReadDouble(), y = r.ReadDouble(), z = r.ReadDouble();
                _lastKnown[uid] = new ScribeWorldPosition(x, y, z);
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            // Malformed — leave the store empty.
            _lastKnown.Clear();
        }
    }
}
