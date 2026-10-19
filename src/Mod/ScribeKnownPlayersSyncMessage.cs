using ProtoBuf;

namespace Scribe;

/// <summary>
/// Server → client: broadcasts the full known-players registry to every online player (the one
/// broadcast-shaped message on this channel). Decoded with
/// <see cref="Scribe.Core.ScribeKnownPlayersStore.LoadFrom"/>. Sent on join and re-broadcast on every upsert,
/// so an open Assignment Desk dialog sees new players.
/// </summary>
[ProtoContract]
public sealed class ScribeKnownPlayersSyncMessage
{
    /// <summary>A <c>ScribeKnownPlayersStore.Serialize</c> blob of the full registry.</summary>
    [ProtoMember(1)]
    public byte[]? SnapshotBytes { get; set; }
}
