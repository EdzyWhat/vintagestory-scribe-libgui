using ProtoBuf;

namespace Scribe;

/// <summary>
/// Sent between client and server over the "scribe" channel. Client → server: submits an edited
/// document to apply (server-authoritative), addressed by the document's stable <see cref="DocIdBytes"/>.
/// Server → client: the authoritative result — either the current document (open granted) or a
/// refusal reason (e.g. the single-editor lock is held by someone else).
/// </summary>
[ProtoContract]
public sealed class ScribeEditDocumentMessage
{
    /// <summary>The owning document's <c>DocId</c> as 16 raw bytes. Used to route the message to the
    /// correct host (lectern or notebook) via the host registry, replacing the former PosX/Y/Z fields.</summary>
    [ProtoMember(1)]
    public byte[]? DocIdBytes { get; set; }

    /// <summary>Core-serialized <c>ScribeDocument</c> bytes. Null/empty for a pure open request.</summary>
    [ProtoMember(4)]
    public byte[]? DocumentBytes { get; set; }

    /// <summary>Server → client only: whether the request was granted.</summary>
    [ProtoMember(5)]
    public bool Granted { get; set; } = true;

    /// <summary>Server → client only: shown to the player when <see cref="Granted"/> is false.</summary>
    [ProtoMember(6)]
    public string? RefusalReason { get; set; }

    /// <summary>
    /// Server → client only: whether this reply is for the editor view (true) or the
    /// read view (false). Meaningless when this message is a client → server edit submission.
    /// </summary>
    [ProtoMember(7)]
    public bool EditorMode { get; set; }

    /// <summary>
    /// Server → client only: whether the requesting interaction was the quick-add gesture
    /// (Shift+right-click). When true, the client — after entering the editor view — inserts a
    /// fresh empty task at the top of the document and focuses its caret (add-unified-quick-add-
    /// interaction). Threaded through the open round-trip because the client dialog does not exist
    /// yet at block-interaction time. Meaningless on a read-view reply or an edit submission.
    /// </summary>
    [ProtoMember(8)]
    public bool QuickAdd { get; set; }
}
