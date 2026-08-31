using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client → server: create a Quest Link for a quest the client's <see cref="ScribeQuestWatcher"/> just
/// detected as accepted (add-assignment-and-quest-support §11.2, Quest Accept Policy = Always/Prompt).
/// The server resolves the DESTINATION document itself (the sending player's first carried Notebook/
/// Tablet — mirrors <c>ScribeModSystem.History.cs</c>'s <c>FindNotebookInInventory</c>) and is
/// authoritative for whether the Link is actually added: it silently no-ops if the player carries no
/// Scribe document, has no capacity, or already has a Link for this exact quest code (idempotent under
/// repeat detection — see <c>ScribeQuestWatcher</c>'s doc-comment on session-only dedup).
/// </summary>
[ProtoContract]
public sealed class ScribeAutoLinkQuestMessage
{
    /// <summary>The quest's domain-qualified id (e.g. <c>"vsquest:quest-freeghost"</c>), stored verbatim
    /// after the <c>"quest:"</c> prefix — see <see cref="Scribe.Core.ScribeLinkTarget.ForQuest"/>.</summary>
    [ProtoMember(1)]
    public string? QuestCode { get; set; }

    /// <summary>Resolved display title, captured client-side from the catalog at detection time (a quest
    /// has no Handbook page to re-derive this from later).</summary>
    [ProtoMember(2)]
    public string? Title { get; set; }

    /// <summary>Resolved description, or null if the quest has none (<see cref="ScribeQuestCatalog"/>).</summary>
    [ProtoMember(3)]
    public string? Description { get; set; }
}
