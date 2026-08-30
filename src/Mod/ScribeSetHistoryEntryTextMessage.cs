using ProtoBuf;

namespace Scribe;

/// <summary>Client → server: update the text of an EXISTING Manual history entry, addressed by
/// <see cref="EntryId"/>. The server authorizes by matching the packet sender's name to the entry's
/// stored author name (mirroring <see cref="ScribeEditGuestbookNoteMessage"/>'s sender-identity
/// check) — a mismatch is silently ignored, never an error.</summary>
[ProtoContract]
public sealed class ScribeSetHistoryEntryTextMessage
{
    /// <summary>The notebook document's <c>DocId</c> as 16 raw bytes.</summary>
    [ProtoMember(1)]
    public byte[]? DocIdBytes { get; set; }

    /// <summary>The entry being edited, as 16 raw bytes.</summary>
    [ProtoMember(2)]
    public byte[]? EntryId { get; set; }

    /// <summary>The entry's new text (trimmed, max <see cref="Scribe.Core.ScribeDocumentCodec.MaxTaskTextLength"/> chars).</summary>
    [ProtoMember(3)]
    public string? Text { get; set; }

    /// <summary>The <c>InventoryID</c> of the slot the notebook lives in. Null on legacy clients,
    /// where the server falls back to the active-hand slot.</summary>
    [ProtoMember(4)]
    public string? TargetInventoryId { get; set; }

    /// <summary>The slot index within <see cref="TargetInventoryId"/>. Ignored when that id is null.</summary>
    [ProtoMember(5)]
    public int TargetSlotId { get; set; }
}
