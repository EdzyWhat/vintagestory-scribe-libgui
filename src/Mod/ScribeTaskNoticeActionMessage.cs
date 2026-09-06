using System.ComponentModel;
using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client → server: Accept or Decline a held, sealed Task Notice (`task-notice-item` capability).
/// Addressed by the notice's own slot identity (like <see cref="ScribeNotebookOpenedMessage"/> and
/// <see cref="ScribeAssignmentActionMessage"/>'s target-slot fields) rather than any DocId/AssignmentId
/// — an unaccepted notice has no store record for either of those to name.
/// </summary>
[ProtoContract]
public sealed class ScribeTaskNoticeActionMessage
{
    /// <summary>The held Task Notice's own inventory + slot identity — the item this action consumes.</summary>
    [ProtoMember(1)]
    public string? SourceInventoryId { get; set; }

    /// <summary>See <see cref="ScribeAssignmentActionMessage.TargetSlotId"/>'s remarks: the
    /// <see cref="DefaultValue"/> attribute is load-bearing — without it protobuf-net's implicit-default
    /// wire skip makes a legitimate slot 0 indistinguishable from "unset" and it silently reverts to -1 on
    /// the receiving end.</summary>
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

    /// <summary>Ignored for Decline. Defaults to -1 (unresolved); see <see cref="SourceSlotId"/>'s remarks
    /// on why <see cref="DefaultValue"/> is load-bearing here too.</summary>
    [ProtoMember(5)]
    [DefaultValue(-1)]
    public int TargetSlotId { get; set; } = -1;

    /// <summary>The accepting player's own New Task Insert preference (mirrors
    /// <see cref="ScribeAssignmentActionMessage.NewTaskInsert"/>). Ignored for Decline.</summary>
    [ProtoMember(6)]
    public byte NewTaskInsert { get; set; }
}
