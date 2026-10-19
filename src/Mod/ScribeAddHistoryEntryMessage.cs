using ProtoBuf;

namespace Scribe;

/// <summary>Client → server: create a new Manual history entry on a Notebook/Clockmaker's Notebook.
/// Sent only when a pending "Add Entry" draft is first committed non-empty. <see cref="EntryId"/> is minted
/// client-side so the draft can be tracked before the round-trip. The server authors the entry under the
/// SENDER's name and supplies the in-game date.</summary>
[ProtoContract]
public sealed class ScribeAddHistoryEntryMessage
{
    [ProtoMember(1)]
    public byte[]? DocIdBytes { get; set; }

    [ProtoMember(2)]
    public byte[]? EntryId { get; set; }

    [ProtoMember(3)]
    public string? Text { get; set; }

    /// <summary>The <c>InventoryID</c> of the slot holding the notebook, so the server targets the EXACT open
    /// book. Null on legacy clients (server falls back to the active-hand slot).</summary>
    [ProtoMember(4)]
    public string? TargetInventoryId { get; set; }

    [ProtoMember(5)]
    public int TargetSlotId { get; set; }
}
