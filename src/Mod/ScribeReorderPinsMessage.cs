using System.Collections.Generic;
using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client -&gt; server: reorder the sending player's own pin list. The order is two PARALLEL lists of raw
/// 16-byte <c>Guid</c>s (<see cref="DocIds"/>[i]/<see cref="TaskIds"/>[i] is the i-th pin), avoiding
/// protobuf-net's version-fragile <c>Guid</c> handling.
///
/// The server permutes ONLY that player's list in <see cref="ScribePinStore"/> (ignoring unknown/duplicate
/// ids, preserving pins the client omits), persists, and re-pushes. It never touches a document's block
/// order or another player's pins.
/// </summary>
[ProtoContract]
public sealed class ScribeReorderPinsMessage
{
    /// <summary>Owning <c>DocId</c>s (16 raw bytes each), parallel to <see cref="TaskIds"/>.</summary>
    [ProtoMember(1)]
    public List<byte[]>? DocIds { get; set; }

    /// <summary><c>TaskId</c>s (16 raw bytes each), parallel to <see cref="DocIds"/>.</summary>
    [ProtoMember(2)]
    public List<byte[]>? TaskIds { get; set; }
}
