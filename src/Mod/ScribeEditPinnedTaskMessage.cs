using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client -&gt; server: change a pinned task's text, addressed by the task's stable identity
/// <c>(DocId, TaskId)</c> — never a block position — so the Pin Tab can edit a task's text without
/// knowing its position or block coordinates. <see cref="DocId"/>/<see cref="TaskId"/> are the raw
/// 16-byte forms of the document's and task's <c>Guid</c>s (protobuf-net's own <c>Guid</c> handling is
/// version-fragile, so raw byte arrays are used instead), mirroring
/// <see cref="ScribeCompleteTaskMessage"/> / <see cref="ScribeSetPinMessage"/>.
///
/// Best-effort write-through, lock-free (mirrors <see cref="BlockEntityScribeLectern.SetTaskTextFromReader"/>):
/// when the owning document resolves, the new text is written into the authoritative document without
/// acquiring the editor lock; regardless of whether the source resolves, the acting player's pin text
/// snapshot is refreshed and re-synced. Blank/whitespace-only text is rejected by the document model, so
/// a pin edit can never blank a task out.
/// </summary>
[ProtoContract]
public sealed class ScribeEditPinnedTaskMessage
{
    /// <summary>The owning document's <c>DocId</c> as 16 raw bytes.</summary>
    [ProtoMember(1)]
    public byte[]? DocId { get; set; }

    /// <summary>The task's <c>TaskId</c> as 16 raw bytes.</summary>
    [ProtoMember(2)]
    public byte[]? TaskId { get; set; }

    /// <summary>The task's new text.</summary>
    [ProtoMember(3)]
    public string? Text { get; set; }
}
