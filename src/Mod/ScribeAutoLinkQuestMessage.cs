using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client → server: create a Quest Link for a quest the client's <see cref="ScribeQuestWatcher"/> just
/// detected as accepted (add-assignment-and-quest-support §11.2, Quest Accept Policy = Always/Prompt).
/// The server resolves the DESTINATION at <see cref="TargetInventoryId"/>/<see cref="TargetSlotId"/> and
/// re-validates it (writeable, has capacity) exactly like <c>TryPlaceAcceptedAssignment</c> does — never
/// trusting the client's choice as proof of eligibility (add-progression-framework-quest-support Decision
/// 3) — falling back to the sending player's first carried Notebook/Tablet
/// (<c>ScribeModSystem.History.cs</c>'s <c>FindNotebookInInventory</c>) only when no target was sent, for
/// backward compatibility with any in-flight message shape. Is authoritative for whether the Link is
/// actually added: it silently no-ops if the player carries no Scribe document, has no capacity, or
/// already has a Link for this exact (source, quest code) (idempotent under repeat detection — see
/// <c>ScribeQuestWatcher</c>'s doc-comment on session-only dedup).
/// </summary>
[ProtoContract]
public sealed class ScribeAutoLinkQuestMessage
{
    /// <summary>Which backend mod this quest came from (<see cref="Scribe.Core.ScribeQuestSource"/> —
    /// add-progression-framework-quest-support Decision 1). Null/absent defensively falls back to
    /// <see cref="Scribe.Core.ScribeQuestSource.VsQuest"/> server-side, the only backend that could have
    /// created an in-flight message predating this field.</summary>
    [ProtoMember(4)]
    public string? Source { get; set; }

    /// <summary>The quest's domain-qualified id (e.g. <c>"vsquest:quest-freeghost"</c>), stored verbatim
    /// after the <c>"quest:{source}/"</c> prefix — see <see cref="Scribe.Core.ScribeLinkTarget.ForQuest"/>.</summary>
    [ProtoMember(1)]
    public string? QuestCode { get; set; }

    /// <summary>Resolved display title, captured client-side from the catalog at detection time (a quest
    /// has no Handbook page to re-derive this from later).</summary>
    [ProtoMember(2)]
    public string? Title { get; set; }

    /// <summary>Resolved description, or null if the quest has none (<see cref="ScribeQuestCatalog"/>).</summary>
    [ProtoMember(3)]
    public string? Description { get; set; }

    /// <summary>Accept-time placement target (assignment-state-machine's placement requirement, extended
    /// to Quest auto-link — Decision 3) — the <c>InventoryID</c> of the slot the client resolved to receive
    /// the linked quest (the sole eligible carried Scribe document, or the player's picker choice among
    /// 2+). Null falls back to server-side resolution (<c>FindNotebookInInventory</c>), mirroring
    /// <see cref="ScribeAssignmentActionMessage.TargetInventoryId"/>'s existing convention.</summary>
    [ProtoMember(5)]
    public string? TargetInventoryId { get; set; }

    /// <summary>Slot index within <see cref="TargetInventoryId"/>. Defaults to -1 (unresolved) so an
    /// absent value never aliases slot 0.</summary>
    [ProtoMember(6)]
    public int TargetSlotId { get; set; } = -1;
}
