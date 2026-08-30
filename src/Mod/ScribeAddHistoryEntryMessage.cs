using ProtoBuf;

namespace Scribe;

/// <summary>Client → server: create a new Manual history entry on a Notebook/Clockmaker's Notebook.
/// Sent only the FIRST time a pending "Add Entry" draft's text is committed non-empty — a draft that
/// never receives text is discarded client-side and never generates this message (see
/// <c>add-custom-history-entries</c> design.md). <see cref="EntryId"/> is minted client-side when
/// "Add Entry" is clicked (mirroring <c>ScribeCompleteTaskMessage</c>'s client-generated TaskId
/// pattern), so the client can track the draft locally before this round-trip completes. The server
/// authors the entry under the SENDER's own name — never a client-claimed identity — and supplies the
/// in-game date itself.</summary>
[ProtoContract]
public sealed class ScribeAddHistoryEntryMessage
{
    /// <summary>The notebook document's <c>DocId</c> as 16 raw bytes.</summary>
    [ProtoMember(1)]
    public byte[]? DocIdBytes { get; set; }

    /// <summary>The client-generated identifier for the new entry, as 16 raw bytes.</summary>
    [ProtoMember(2)]
    public byte[]? EntryId { get; set; }

    /// <summary>The entry's text (trimmed, max <see cref="Scribe.Core.ScribeDocumentCodec.MaxTaskTextLength"/> chars).</summary>
    [ProtoMember(3)]
    public string? Text { get; set; }

    /// <summary>The <c>InventoryID</c> of the slot the notebook lives in, so the server targets the
    /// EXACT open book rather than the active-hand item (matching <see cref="ScribeNotebookSaveMessage"/>'s
    /// addressing). Null on legacy clients, where the server falls back to the active-hand slot.</summary>
    [ProtoMember(4)]
    public string? TargetInventoryId { get; set; }

    /// <summary>The slot index within <see cref="TargetInventoryId"/>. Ignored when that id is null.</summary>
    [ProtoMember(5)]
    public int TargetSlotId { get; set; }
}
