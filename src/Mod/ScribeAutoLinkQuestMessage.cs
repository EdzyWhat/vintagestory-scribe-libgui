using System.Collections.Generic;
using ProtoBuf;

namespace Scribe;

/// <summary>One Progression Framework quest objective's catalog definition, captured client-side (the
/// catalog is a client-only asset read — the server has no other way to know it, same reason
/// <see cref="ScribeAutoLinkQuestMessage.Title"/>/<see cref="ScribeAutoLinkQuestMessage.Description"/>
/// already travel this way) so the server can seed the Quest Link's QuestObjective children immediately
/// after adding it (add-progression-framework-quest-objective-subtasks 5.3). Trusted-but-client input,
/// same trust model as every other field on the carrying message.</summary>
[ProtoContract]
public sealed class ScribeAutoLinkObjectiveWire
{
    /// <summary>The objective's own stable code (the reconcile match key — <see cref="Scribe.Core.ScribePfObjectiveDef.Code"/>).</summary>
    [ProtoMember(1)]
    public string? Code { get; set; }

    /// <summary>The objective's resolved single-item code, or null for a non-item/multi-item objective
    /// (<see cref="Scribe.Core.ScribePfObjectiveDef.ItemCode"/>).</summary>
    [ProtoMember(2)]
    public string? ItemCode { get; set; }

    /// <summary>The objective's captured display label, used when <see cref="ItemCode"/> is null
    /// (<see cref="Scribe.Core.ScribePfObjectiveDef.Label"/>).</summary>
    [ProtoMember(3)]
    public string? Label { get; set; }

    /// <summary>The objective's required count (<see cref="Scribe.Core.ScribePfObjectiveDef.Required"/>).</summary>
    [ProtoMember(4)]
    public int Required { get; set; }

    /// <summary>The backend's currently-reported progress for this objective, cached client-side by the
    /// quest watcher at the moment of accept (0 if nothing cached yet — the next tick's progress push
    /// corrects it). Seeds the newly-created child's <c>CurrentQuantity</c> so an already-partly-progressed
    /// objective doesn't render as 0/N for one tick after linking.</summary>
    [ProtoMember(5)]
    public int CurrentProgress { get; set; }
}

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

    /// <summary>The quest's catalog objective definitions, captured client-side (see
    /// <see cref="ScribeAutoLinkObjectiveWire"/>'s remarks) — null/empty for a VS Quest link (which has no
    /// QuestObjective subtask model) or when the quest watcher had nothing cached for this quest yet.
    /// The server reconciles these into QuestObjective children immediately after adding the Link
    /// (add-progression-framework-quest-objective-subtasks 5.3).</summary>
    [ProtoMember(7)]
    public List<ScribeAutoLinkObjectiveWire>? Objectives { get; set; }

    /// <summary>The client's <see cref="Scribe.Core.ScribePlayerSettings.AutoPinOnQuestAccept"/> preference
    /// at send time (fix-quest-prompt-persistence-and-auto-pin Decision 5). When true, the server pins the
    /// newly-created Link's task right after adding it, through the same pin-add path a manual pin uses.
    /// Defaults to false for an old client that never sets it (today's behavior: link but don't pin).</summary>
    [ProtoMember(8)]
    public bool AutoPin { get; set; }

    /// <summary>The client's <see cref="Scribe.Core.ScribePlayerSettings.PinInsert"/> preference (as a
    /// byte), read only when <see cref="AutoPin"/> is true — where the newly-pinned task lands in the pin
    /// list, mirroring <c>ScribeSetPinMessage.PinInsert</c>'s own convention exactly (the player-pins
    /// requirement that an auto-pin follow "the same insertion rules" as a manual pin). Defaults to 0
    /// (<see cref="Scribe.Core.ScribePinInsert.Bottom"/>) for an old client.</summary>
    [ProtoMember(9)]
    public byte PinInsert { get; set; }
}
