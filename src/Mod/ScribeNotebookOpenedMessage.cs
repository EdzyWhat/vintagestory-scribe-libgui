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

    /// <summary>The <c>InventoryID</c> of the slot the opened item lives in, so the server records the
    /// PickedUp entry on the EXACT opened book rather than the player's active-hand item — the Handbook
    /// flow can open a carried book that is not in hand (add-tracker-link-tasks 7.16). Null on legacy
    /// clients, where the server falls back to the active-hand slot.</summary>
    [ProtoMember(2)]
    public string? TargetInventoryId { get; set; }

    /// <summary>The slot index within <see cref="TargetInventoryId"/>. Ignored when that id is null.</summary>
    [ProtoMember(3)]
    public int TargetSlotId { get; set; }
}
