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

    /// <summary>Client → server only: the <c>InventoryID</c> of the slot the dialog was editing, so the
    /// server writes to that exact slot rather than the active hand (the Handbook "Add to Scribe" flow can
    /// open a carried book that is not in hand). Null on echoes and legacy clients, where the server falls
    /// back to the active-hand slot.</summary>
    [ProtoMember(4)]
    public string? TargetInventoryId { get; set; }

    /// <summary>Client → server only: the slot index within <see cref="TargetInventoryId"/>. Ignored when
    /// that id is null.</summary>
    [ProtoMember(5)]
    public int TargetSlotId { get; set; }
}
