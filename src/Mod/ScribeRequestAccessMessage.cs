using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client → server: request to switch a currently-open dialog between the lock-free read view and
/// the lock-holding editor view, sent by the in-GUI mode-toggle button. The initial open does not
/// use this message — it rides the implicit block-interaction sync instead. Server replies via
/// <see cref="ScribeEditDocumentMessage"/>. Addressed by <see cref="DocIdBytes"/> (replacing the
/// former PosX/Y/Z fields) so the same message works for both the Lectern and the Notebook.
/// </summary>
[ProtoContract]
public sealed class ScribeRequestAccessMessage
{
    /// <summary>The owning document's <c>DocId</c> as 16 raw bytes.</summary>
    [ProtoMember(1)]
    public byte[]? DocIdBytes { get; set; }

    /// <summary>True to request the editor view (takes the lock); false to switch to read view.</summary>
    [ProtoMember(4)]
    public bool WantEditor { get; set; }
}
