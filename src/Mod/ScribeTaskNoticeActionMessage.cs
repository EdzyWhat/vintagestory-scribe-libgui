using System.ComponentModel;
using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client → server: Accept or Decline a held, sealed Task Notice. Addressed by the notice's own slot
/// identity, since an unaccepted notice has no DocId/AssignmentId.
/// </summary>
[ProtoContract]
public sealed class ScribeTaskNoticeActionMessage
{
    /// <summary>The held Task Notice's inventory + slot identity (the item this action consumes).</summary>
    [ProtoMember(1)]
    public string? SourceInventoryId { get; set; }

    /// <summary>The <see cref="DefaultValue"/> attribute is load-bearing: without it protobuf-net skips the
    /// implicit default on the wire, so a legitimate slot 0 silently reverts to -1 on the receiver.</summary>
    [ProtoMember(2)]
    [DefaultValue(-1)]
    public int SourceSlotId { get; set; } = -1;

    /// <summary>Only <see cref="Scribe.Core.ScribeAssignmentAction.Accept"/> or
    /// <see cref="Scribe.Core.ScribeAssignmentAction.Decline"/> are meaningful here.</summary>
    [ProtoMember(3)]
    public byte Action { get; set; }

    /// <summary>Accept-time placement target (mirrors <see cref="ScribeAssignmentActionMessage.TargetInventoryId"/>).
    /// Ignored for Decline.</summary>
    [ProtoMember(4)]
    public string? TargetInventoryId { get; set; }

    /// <summary>Ignored for Decline. Defaults to -1 (unresolved); <see cref="DefaultValue"/> is load-bearing
    /// here too (see <see cref="SourceSlotId"/>).</summary>
    [ProtoMember(5)]
    [DefaultValue(-1)]
    public int TargetSlotId { get; set; } = -1;

    /// <summary>The accepting player's own New Task Insert preference (mirrors
    /// <see cref="ScribeAssignmentActionMessage.NewTaskInsert"/>). Ignored for Decline.</summary>
    [ProtoMember(6)]
    public byte NewTaskInsert { get; set; }
}
