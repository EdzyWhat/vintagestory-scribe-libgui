using ProtoBuf;

namespace Scribe;

/// <summary>Client → server: delete a Manual history entry, addressed by <see cref="EntryId"/>. Same
/// sender-identity authorization as <see cref="ScribeSetHistoryEntryTextMessage"/> — a mismatch (or
/// an unknown EntryId) is silently ignored.</summary>
[ProtoContract]
public sealed class ScribeDeleteHistoryEntryMessage
{
    /// <summary>The notebook document's <c>DocId</c> as 16 raw bytes.</summary>
    [ProtoMember(1)]
    public byte[]? DocIdBytes { get; set; }

    /// <summary>The entry being deleted, as 16 raw bytes.</summary>
    [ProtoMember(2)]
    public byte[]? EntryId { get; set; }

    /// <summary>The <c>InventoryID</c> of the slot the notebook lives in. Null on legacy clients,
    /// where the server falls back to the active-hand slot.</summary>
    [ProtoMember(3)]
    public string? TargetInventoryId { get; set; }

    /// <summary>The slot index within <see cref="TargetInventoryId"/>. Ignored when that id is null.</summary>
    [ProtoMember(4)]
    public int TargetSlotId { get; set; }
}
