using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client → server: saves an edited Notebook document. Addressed by <see cref="DocIdBytes"/>
/// (the notebook's stable DocId) so the server can locate the player's held item and write back.
/// Server → client: echo reply confirming the save was applied (the updated document bytes, so the
/// client can refresh its cache).
/// </summary>
[ProtoContract]
public sealed class ScribeNotebookSaveMessage
{
    /// <summary>The notebook document's <c>DocId</c> as 16 raw bytes.</summary>
    [ProtoMember(1)]
    public byte[]? DocIdBytes { get; set; }

    /// <summary>Core-serialized <c>ScribeDocument</c> bytes.</summary>
    [ProtoMember(2)]
    public byte[]? DocumentBytes { get; set; }

    /// <summary>Core-serialized <c>HistoryStore</c> bytes. Null on older packets or when history
    /// is unchanged; the client treats null as "no update to history".</summary>
    [ProtoMember(3)]
    public byte[]? HistoryBytes { get; set; }
}
