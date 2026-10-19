using System.ComponentModel;
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

    /// <summary>Accept-time placement target: the <c>InventoryID</c> of the slot the client resolved to receive
    /// the accepted task. Ignored except for Accept. Null falls back to the active hand, as
    /// <c>ResolveItemPacketSlot</c> does.</summary>
    [ProtoMember(3)]
    public string? TargetInventoryId { get; set; }

    /// <summary>Slot index within <see cref="TargetInventoryId"/>. Ignored except for Accept. Defaults to -1
    /// (unresolved) so an absent value never aliases slot 0. <see cref="DefaultValue"/> is load-bearing:
    /// protobuf-net omits a scalar equal to the CLR default (0), so without it a real slot 0 reads back as -1.</summary>
    [ProtoMember(4)]
    [DefaultValue(-1)]
    public int TargetSlotId { get; set; } = -1;

    /// <summary>The accepting player's own New Task Insert preference (<c>ScribePlayerSettings.NewTaskInsert</c>),
    /// a client-local preference the server cannot otherwise see, so Accept-time placement can honor it.
    /// Ignored except for Accept.</summary>
    [ProtoMember(5)]
    public byte NewTaskInsert { get; set; }
}
