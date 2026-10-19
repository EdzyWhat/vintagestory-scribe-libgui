using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client -&gt; server: update a QuestObjective subtask's live <c>CurrentQuantity</c>, addressed by
/// <c>(DocId, TaskId)</c>, never a block position. Mirrors <see cref="ScribeSetTrackerQuantityMessage"/>
/// but is deliberately SEPARATE, so QuestObjective write-through never shares a path with the
/// Tracker/Craft carried-inventory engine (the two hosts gate Tracker write-through inconsistently, and
/// widening either risked changing Tracker/Craft behavior).
///
/// <see cref="DocId"/>/<see cref="TaskId"/> are raw 16-byte <c>Guid</c>s (protobuf-net's own <c>Guid</c>
/// handling is version-fragile). Best-effort and lock-free (see
/// <see cref="IScribeDocumentHost.SetQuestObjectiveProgressFromReader"/>): when the document resolves the
/// value is clamped into <c>[0, TargetQuantity]</c> and written without the editor lock; a
/// non-QuestObjective, unknown id or no-op change writes nothing.
/// </summary>
[ProtoContract]
public sealed class ScribeSetQuestObjectiveProgressMessage
{
    /// <summary>The owning document's <c>DocId</c> as 16 raw bytes.</summary>
    [ProtoMember(1)]
    public byte[]? DocId { get; set; }

    /// <summary>The QuestObjective task's <c>TaskId</c> as 16 raw bytes.</summary>
    [ProtoMember(2)]
    public byte[]? TaskId { get; set; }

    /// <summary>The backend's freshly-reported progress count, clamped into <c>[0, TargetQuantity]</c> server-side.</summary>
    [ProtoMember(3)]
    public int Quantity { get; set; }
}
