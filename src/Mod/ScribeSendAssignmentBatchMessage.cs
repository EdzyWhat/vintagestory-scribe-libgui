using System.Collections.Generic;
using ProtoBuf;

namespace Scribe;

/// <summary>
/// One selected row of a multi-item assignment batch (assignment-multi-item-creation design.md D12/D13).
/// Carries the same full <c>ScribeBlock</c> shape <see cref="Scribe.Core.ScribeAssignmentStore.TryCreate"/>'s
/// broadened signature accepts, so the server can build a Task/Tracker/Link/Craft assignment from it with
/// no per-kind special-casing on the wire.
/// </summary>
[ProtoContract]
public sealed class ScribeAssignmentBatchRow
{
    /// <summary>Fresh id for the NEW assignment this row becomes — client-minted, distinct from
    /// <see cref="SourceTaskId"/> (this row's identity in the staged source document). Reusing the source
    /// TaskId here would collide if the same staged row is ever sent again in a later batch (an
    /// assignment id must be fresh — see <c>ScribeAssignmentStore.TryCreate</c>).</summary>
    [ProtoMember(1)]
    public byte[]? AssignmentId { get; set; }

    /// <summary>This row's TaskId in the staged source document — used only when the batch's
    /// <see cref="ScribeSendAssignmentBatchMessage.DeleteFromSource"/> is true, to find and remove the
    /// exact row from the staged item's document. Null/absent is harmless when the flag is false.</summary>
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
/// Client → server: send a multi-item assignment batch from the Create Assignments tab's staging slot
/// (assignment-multi-item-creation). Addressed by the Assignment Desk's block position + staging slot
/// index — like <see cref="ScribeTranscribeCopyMessage"/>, this operates on a whole item sitting in a
/// specific block's inventory, not a DocId. Supersedes the earlier single-freeform-task
/// <c>ScribeSendAssignmentMessage</c>, removed once the Create Assignments tab fully migrated to the
/// staging-and-select flow (tasks.md 9.5).
///
/// The server creates one independent <see cref="Scribe.Core.ScribeAssignment"/> record per
/// <see cref="Rows"/> entry, all addressed to <see cref="TargetPlayerUid"/> — no bundling identifier, per
/// the proposal's "separate assignment per item" decision. When <see cref="DeleteFromSource"/> is true,
/// every successfully-created row is also removed from the staged document afterward (a move, not a
/// copy); when false (the default), the staged document is left untouched.
/// </summary>
[ProtoContract]
public sealed class ScribeSendAssignmentBatchMessage
{
    /// <summary>The Assignment Desk block position — the three coordinates of its <c>BlockPos</c>.</summary>
    [ProtoMember(1)] public int X { get; set; }
    [ProtoMember(2)] public int Y { get; set; }
    [ProtoMember(3)] public int Z { get; set; }

    /// <summary>Inventory slot index of the staged item on that Assignment Desk (design.md D8).</summary>
    [ProtoMember(4)]
    public int StagingSlot { get; set; }

    /// <summary>UID of the single recipient for every row in this batch.</summary>
    [ProtoMember(5)]
    public string? TargetPlayerUid { get; set; }

    /// <summary>Move-not-copy: remove every successfully-sent row from the staged document afterward.
    /// UI-only session state client-side, defaulting false and reset every send (design.md D13) — never a
    /// saved preference.</summary>
    [ProtoMember(6)]
    public bool DeleteFromSource { get; set; }

    [ProtoMember(7)]
    public List<ScribeAssignmentBatchRow>? Rows { get; set; }

    /// <summary>Which document <see cref="DeleteFromSource"/> should remove rows from
    /// (add-assignment-desk-own-tasks design.md D6): false (default) — the staging slot's ItemStack-
    /// embedded document, via the existing <c>TryRemoveStagedRows</c> path; true — the Assignment Desk
    /// block entity's own persisted document (the Desk's Read/Editor tabs), via the new
    /// <c>TryRemoveDeskOwnRows</c> path. Ignored entirely when <see cref="DeleteFromSource"/> is false.</summary>
    [ProtoMember(8)]
    public bool SourceIsDeskDocument { get; set; }

    /// <summary>The delivery-mode toggle's position at send time (`assignment-delivery-mode` /
    /// `assignment-desk-block` capabilities): <see cref="Scribe.Core.ScribeDeliveryChoice.LocalInboxes"/>
    /// (0, default) or <see cref="Scribe.Core.ScribeDeliveryChoice.SendNotice"/> (1). The server never
    /// trusts this alone — it re-derives whether a notice is actually required via
    /// <see cref="Scribe.Core.ScribeDeliveryPolicy.RequiresNotice"/> against its OWN current
    /// `DeliveryMode`, so a stale/spoofed client value can't bypass an `AlwaysPhysical` server or force a
    /// notice on an `AlwaysInstant` one.</summary>
    [ProtoMember(9)]
    public byte DeliveryChoice { get; set; }
}
