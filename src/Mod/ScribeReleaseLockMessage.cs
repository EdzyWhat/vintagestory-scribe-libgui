using ProtoBuf;

namespace Scribe;

/// <summary>Client → server: release the single-editor lock on a document host (sent on GUI close).</summary>
[ProtoContract]
public sealed class ScribeReleaseLockMessage
{
    /// <summary>The owning document's <c>DocId</c> as 16 raw bytes.</summary>
    [ProtoMember(1)]
    public byte[]? DocIdBytes { get; set; }
}
