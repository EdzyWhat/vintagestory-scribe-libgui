using ProtoBuf;
using Scribe.Core;

namespace Scribe;

/// <summary>
/// Client → server: request one of the state-machine actions for an assignment. The server derives
/// the actor from the authenticated sender, verifies the sender's role and current state, and applies
/// <see cref="Action"/> through <see cref="ScribeAssignmentTransitions"/>; the action byte is never
/// trusted as proof of authorization.
/// </summary>
[ProtoContract]
public sealed class ScribeAssignmentActionMessage
{
    /// <summary>Stable id of the assignment being acted on.</summary>
    [ProtoMember(1)]
    public byte[]? AssignmentId { get; set; }

    /// <summary>
    /// Requested action: Accept, Decline, Cancel, or Discard. Completed is intentionally not a valid
    /// wire action because completion is derived from the underlying task's done flag.
    /// </summary>
    [ProtoMember(2)]
    public byte Action { get; set; }

    /// <summary>Accept-time placement target (assignment-state-machine's placement requirement) — the
    /// <c>InventoryID</c> of the slot the client resolved to receive the accepted task (the currently
    /// held document, or the single/chosen inventory candidate). Ignored for every action other than
    /// Accept. Null falls back to the active hand, mirroring <c>ResolveItemPacketSlot</c>'s existing
    /// convention.</summary>
    [ProtoMember(3)]
    public string? TargetInventoryId { get; set; }

    /// <summary>Slot index within <see cref="TargetInventoryId"/>. Ignored for every action other than
    /// Accept. Defaults to -1 (unresolved) so an absent value never aliases slot 0.</summary>
    [ProtoMember(4)]
    public int TargetSlotId { get; set; } = -1;
}
