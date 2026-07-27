using System.Collections.Generic;
using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client -&gt; server: reorder the sending player's own pin list into a client-supplied order, addressed
/// by pin identity. The new order is carried as two PARALLEL lists of raw 16-byte <c>Guid</c> forms —
/// <see cref="DocIds"/>[i]/<see cref="TaskIds"/>[i] is the i-th pin in the desired order (raw byte arrays
/// rather than protobuf-net's version-fragile <c>Guid</c> handling, matching the sibling pin messages).
///
/// The server permutes ONLY that player's per-player pin list in <see cref="ScribePinStore"/> to match
/// this order (ignoring unknown/duplicate ids and preserving any pins the client omits), persists it
/// (already saved under <c>scribe:pins:v1</c> — no format change), and re-pushes. It does NOT touch any
/// document's block order and does not affect any other player's pins.
/// </summary>
[ProtoContract]
public sealed class ScribeReorderPinsMessage
{
    /// <summary>The owning <c>DocId</c>s (each 16 raw bytes), parallel to <see cref="TaskIds"/>, in the
    /// desired pin-list order.</summary>
    [ProtoMember(1)]
    public List<byte[]>? DocIds { get; set; }

    /// <summary>The <c>TaskId</c>s (each 16 raw bytes), parallel to <see cref="DocIds"/>, in the desired
    /// pin-list order.</summary>
    [ProtoMember(2)]
    public List<byte[]>? TaskIds { get; set; }
}
