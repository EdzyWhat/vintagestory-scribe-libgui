using ProtoBuf;

namespace Scribe;

/// <summary>Client → server: the player just opened a Notebook's dialog. The server resolves the
/// held notebook by <see cref="DocIdBytes"/> and attaches server context, which records the
/// one-time <c>PickedUp</c> history entry for this player (deduplicated per actor in
/// <c>HistoryStore.TryAddEntry</c>). Opening the dialog is otherwise a purely client-side action, so
/// without this signal the server never sees it and no PickedUp entry is ever recorded.</summary>
[ProtoContract]
public sealed class ScribeNotebookOpenedMessage
{
    /// <summary>The opened notebook document's <c>DocId</c> as 16 raw bytes.</summary>
    [ProtoMember(1)]
    public byte[]? DocIdBytes { get; set; }
}
