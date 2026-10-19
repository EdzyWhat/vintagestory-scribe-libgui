using System.Collections.Generic;
using ProtoBuf;

namespace Scribe;

/// <summary>
/// One selected row of a multi-item assignment batch. Carries the full <c>ScribeBlock</c> shape that
/// <see cref="Scribe.Core.ScribeAssignmentStore.TryCreate"/> accepts, so the server builds any kind
/// without per-kind special-casing on the wire.
/// </summary>
[ProtoContract]
public sealed class ScribeAssignmentBatchRow
{
    /// <summary>Fresh, client-minted id for the NEW assignment this row becomes. Distinct from
    /// <see cref="SourceTaskId"/>: reusing the source id would collide if the same staged row is sent
    /// again (an assignment id must be fresh, see <c>ScribeAssignmentStore.TryCreate</c>).</summary>
    [ProtoMember(1)]
    public byte[]? AssignmentId { get; set; }

    /// <summary>This row's TaskId in the staged source document; used only when
    /// <see cref="ScribeSendAssignmentBatchMessage.DeleteFromSource"/> is true, to remove the exact row.</summary>
    [ProtoMember(2)]
    public byte[]? SourceTaskId { get; set; }

    [ProtoMember(3)]
    public byte Kind { get; set; }

    [ProtoMember(4)]
    public string? Text { get; set; }

    [ProtoMember(5)]
    public string? TargetItemCode { get; set; }

    [ProtoMember(6)]
    public int TargetQuantity { get; set; } = 1;

    [ProtoMember(7)]
    public string? LinkTarget { get; set; }

    [ProtoMember(8)]
    public string? LinkLabel { get; set; }

    [ProtoMember(9)]
    public string? LinkDescription { get; set; }

    [ProtoMember(10)]
    public string? RecipeSignature { get; set; }

    [ProtoMember(11)]
    public int Depth { get; set; }
}

/// <summary>
/// Client → server: send a multi-item assignment batch from the Create Assignments tab's staging slot.
/// Addressed by the Assignment Desk's block position + staging slot index (like
/// <see cref="ScribeTranscribeCopyMessage"/>, it operates on a whole item in a block's inventory, not a DocId).
///
/// The server creates one independent <see cref="Scribe.Core.ScribeAssignment"/> per <see cref="Rows"/>
/// entry, all addressed to <see cref="TargetPlayerUid"/>. When <see cref="DeleteFromSource"/> is true each
/// successfully-created row is also removed from the staged document (a move, not a copy).
/// </summary>
[ProtoContract]
public sealed class ScribeSendAssignmentBatchMessage
{
    /// <summary>The Assignment Desk block position (<c>BlockPos</c> coordinates).</summary>
    [ProtoMember(1)] public int X { get; set; }
    [ProtoMember(2)] public int Y { get; set; }
    [ProtoMember(3)] public int Z { get; set; }

    /// <summary>Inventory slot index of the staged item on that Desk.</summary>
    [ProtoMember(4)]
    public int StagingSlot { get; set; }

    /// <summary>UID of the single recipient for every row in this batch.</summary>
    [ProtoMember(5)]
    public string? TargetPlayerUid { get; set; }

    /// <summary>Move-not-copy: remove every successfully-sent row from the staged document. Client-side
    /// session state that resets every send, never a saved preference.</summary>
    [ProtoMember(6)]
    public bool DeleteFromSource { get; set; }

    [ProtoMember(7)]
    public List<ScribeAssignmentBatchRow>? Rows { get; set; }

    /// <summary>Which document <see cref="DeleteFromSource"/> removes rows from: false (default) is the
    /// staging slot's ItemStack-embedded document (<c>TryRemoveStagedRows</c>); true is the Desk block
    /// entity's own document (<c>TryRemoveDeskOwnRows</c>). Ignored when <see cref="DeleteFromSource"/> is false.</summary>
    [ProtoMember(8)]
    public bool SourceIsDeskDocument { get; set; }

    /// <summary>The delivery-mode toggle at send time: <see cref="Scribe.Core.ScribeDeliveryChoice.LocalInboxes"/>
    /// (0, default) or <see cref="Scribe.Core.ScribeDeliveryChoice.SendNotice"/> (1). The server never
    /// trusts it alone: it re-derives whether a notice is required via
    /// <see cref="Scribe.Core.ScribeDeliveryPolicy.RequiresNotice"/> against its OWN `DeliveryMode`, so a
    /// stale or spoofed value cannot bypass `AlwaysPhysical` or force a notice on `AlwaysInstant`.</summary>
    [ProtoMember(9)]
    public byte DeliveryChoice { get; set; }
}
