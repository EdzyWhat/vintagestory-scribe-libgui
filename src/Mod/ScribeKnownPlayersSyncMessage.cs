using ProtoBuf;

namespace Scribe;

/// <summary>
/// Server → client: broadcasts the FULL known-players registry to every online player
/// (persist-known-players-for-assignment) — the first genuinely shared/broadcast-shaped message on
/// this channel, as opposed to every other message's per-player-only push (see design.md Decision 3).
/// The payload is a <see cref="Scribe.Core.ScribeKnownPlayersStore.Serialize"/> blob, decoded
/// client-side with <see cref="Scribe.Core.ScribeKnownPlayersStore.LoadFrom"/>. Sent on join and
/// re-broadcast to every online player whenever a new/updated entry is upserted, so an already-open
/// Assignment Desk dialog picks up a newly-joined player without a reopen.
/// </summary>
[ProtoContract]
public sealed class ScribeKnownPlayersSyncMessage
{
    /// <summary>A <c>ScribeKnownPlayersStore.Serialize</c> blob of the full registry.</summary>
    [ProtoMember(1)]
    public byte[]? SnapshotBytes { get; set; }
}
