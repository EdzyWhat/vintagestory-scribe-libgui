using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client -&gt; server: update a QuestObjective subtask's live <c>CurrentQuantity</c>, addressed by the
/// task's stable identity <c>(DocId, TaskId)</c> — never a block position. Mirrors
/// <see cref="ScribeSetTrackerQuantityMessage"/>'s shape exactly, but is deliberately a SEPARATE message
/// (rather than reusing that one) so a QuestObjective's write-through never shares a path with the
/// Tracker/Craft carried-inventory engine (add-progression-framework-quest-objective-subtasks design.md
/// Decision — the two Mod-layer hosts already gate their Tracker write-through inconsistently
/// (<c>IsTracker</c> vs. the broader <c>IsCarriedCountTracked</c>); widening either risked an unintended
/// behavior change to Tracker/Craft, so this is a few lines of duplication in exchange for zero risk).
///
/// <see cref="DocId"/>/<see cref="TaskId"/> are the raw 16-byte forms of the <c>Guid</c>s (protobuf-net's
/// own <c>Guid</c> handling is version-fragile, so raw byte arrays are used, matching the sibling task
/// messages). Best-effort write-through, lock-free (reuses
/// <see cref="IScribeDocumentHost.SetQuestObjectiveProgressFromReader"/>): when the owning document
/// resolves, the value is clamped into <c>[0, TargetQuantity]</c> and written without acquiring the editor
/// lock; a non-QuestObjective / unknown id / no-op change writes nothing.
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

    /// <summary>The backend's freshly-reported progress count. Clamped into <c>[0, TargetQuantity]</c>
    /// server-side.</summary>
    [ProtoMember(3)]
    public int Quantity { get; set; }
}
