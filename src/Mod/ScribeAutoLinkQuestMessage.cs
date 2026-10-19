using System.Collections.Generic;
using System.ComponentModel;
using ProtoBuf;

namespace Scribe;

/// <summary>One quest objective's catalog definition, captured client-side (the catalog is a client-only
/// read) so the server can seed the Quest Link's QuestObjective children right after adding it. Progression
/// Framework supplies live progress; VS Quest a one-shot static criteria snapshot. Trusted-but-client input.</summary>
[ProtoContract]
public sealed class ScribeAutoLinkObjectiveWire
{
    /// <summary>The objective's stable code (the reconcile match key, <see cref="Scribe.Core.ScribePfObjectiveDef.Code"/>).</summary>
    [ProtoMember(1)]
    public string? Code { get; set; }

    /// <summary>The objective's resolved single-item code, or null for a non-item/multi-item objective.</summary>
    [ProtoMember(2)]
    public string? ItemCode { get; set; }

    /// <summary>The captured display label, used when <see cref="ItemCode"/> is null.</summary>
    [ProtoMember(3)]
    public string? Label { get; set; }

    /// <summary>The objective's required count.</summary>
    [ProtoMember(4)]
    public int Required { get; set; }

    /// <summary>Progression Framework's reported progress at accept time. Ignored for VS Quest.</summary>
    [ProtoMember(5)]
    public int CurrentProgress { get; set; }
}

/// <summary>
/// Client → server: create a Quest Link for a quest the client's <see cref="ScribeQuestWatcher"/> just
/// detected as accepted (Quest Accept Policy = Always/Prompt). The server re-validates the destination at
/// <see cref="TargetInventoryId"/>/<see cref="TargetSlotId"/> (writeable, has capacity) like
/// <c>TryPlaceAcceptedAssignment</c>, falling back to the sender's first carried Notebook/Tablet
/// (<c>FindNotebookInInventory</c>) only when no target was sent. It silently no-ops if the player carries no
/// Scribe document, has no capacity, or already has a Link for this (source, quest code), so repeat detection is idempotent.
/// </summary>
[ProtoContract]
public sealed class ScribeAutoLinkQuestMessage
{
    /// <summary>Which backend mod the quest came from (<see cref="Scribe.Core.ScribeQuestSource"/>).
    /// Null falls back to <see cref="Scribe.Core.ScribeQuestSource.VsQuest"/> server-side.</summary>
    [ProtoMember(4)]
    public string? Source { get; set; }

    /// <summary>The quest's domain-qualified id (e.g. <c>"vsquest:quest-freeghost"</c>), stored verbatim
    /// after the <c>"quest:{source}/"</c> prefix.</summary>
    [ProtoMember(1)]
    public string? QuestCode { get; set; }

    /// <summary>Display title captured client-side at detection (a quest has no Handbook page to re-derive it from).</summary>
    [ProtoMember(2)]
    public string? Title { get; set; }

    /// <summary>Resolved description, or null if the quest has none.</summary>
    [ProtoMember(3)]
    public string? Description { get; set; }

    /// <summary>Accept-time placement target: the <c>InventoryID</c> of the slot the client resolved to receive
    /// the quest. Null falls back to server-side resolution.</summary>
    [ProtoMember(5)]
    public string? TargetInventoryId { get; set; }

    /// <summary>Slot index within <see cref="TargetInventoryId"/>; defaults to -1 (unresolved). See
    /// <see cref="ScribeAssignmentActionMessage.TargetSlotId"/> for why <see cref="DefaultValue"/> is load-bearing.</summary>
    [ProtoMember(6)]
    [DefaultValue(-1)]
    public int TargetSlotId { get; set; } = -1;

    /// <summary>The quest's catalog objective definitions, reconciled by the server into QuestObjective
    /// children after adding the Link. Null/empty when the watcher has no catalog entry.</summary>
    [ProtoMember(7)]
    public List<ScribeAutoLinkObjectiveWire>? Objectives { get; set; }

    /// <summary>The client's <see cref="Scribe.Core.ScribePlayerSettings.AutoPinOnQuestAccept"/> preference at
    /// send time. When true the server pins the new Link's task through the manual pin-add path.</summary>
    [ProtoMember(8)]
    public bool AutoPin { get; set; }

    /// <summary>The client's <see cref="Scribe.Core.ScribePlayerSettings.PinInsert"/> preference (as a byte),
    /// read only when <see cref="AutoPin"/> is true; follows the same insertion rules as a manual pin.
    /// Defaults to 0 (<see cref="Scribe.Core.ScribePinInsert.Bottom"/>).</summary>
    [ProtoMember(9)]
    public byte PinInsert { get; set; }
}
