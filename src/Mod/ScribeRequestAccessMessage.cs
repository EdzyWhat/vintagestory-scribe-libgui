using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client → server: switch an open dialog between the lock-free read view and the lock-holding editor
/// view (the in-GUI mode toggle). The initial open does not use this message. Server replies via
/// <see cref="ScribeEditDocumentMessage"/>. Works for both the Lectern and the Notebook.
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

    /// <summary>True for the quick-add gesture (Shift+right-click): the server echoes it back on the grant so
    /// the client inserts and focuses a fresh empty task at the top. Only meaningful with <see cref="WantEditor"/>.</summary>
    [ProtoMember(5)]
    public bool QuickAdd { get; set; }
}
