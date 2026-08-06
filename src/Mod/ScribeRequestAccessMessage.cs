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

    /// <summary>True when this editor request is the quick-add gesture (Shift+right-click): the server
    /// echoes it back on the grant so the client inserts a fresh empty task at the top and focuses it
    /// (add-unified-quick-add-interaction). Only meaningful together with <see cref="WantEditor"/>.</summary>
    [ProtoMember(5)]
    public bool QuickAdd { get; set; }
}
