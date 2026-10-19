using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client -&gt; server: change a pinned task's text, addressed by stable <c>(DocId, TaskId)</c> rather than block position. Both are
/// raw 16-byte Guid forms (protobuf-net's own Guid handling is version-fragile), like <see cref="ScribeCompleteTaskMessage"/> /
/// <see cref="ScribeSetPinMessage"/>.
/// Best-effort and lock-free (like <see cref="BlockEntityScribeLectern.SetTaskTextFromReader"/>): if the document resolves, the text
/// is written into it; either way the player's pin text snapshot is refreshed and re-synced. Blank text is rejected by the document model.
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
