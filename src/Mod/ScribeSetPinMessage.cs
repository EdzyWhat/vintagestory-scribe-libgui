using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client -&gt; server: pin or unpin a task for the sending player, addressed by the task's stable
/// identity — never a block position. <see cref="DocId"/>/<see cref="TaskId"/> are the raw 16-byte
/// forms of the document's and task's <c>Guid</c>s (protobuf-net's own <c>Guid</c> handling is
/// version-fragile, so raw byte arrays are used instead).
///
/// Because a pin is identity-addressed, an <b>unpin</b> needs no block resolution at all — the
/// server removes it straight from the per-player store — so it works even when the owning lectern
/// has been broken or its chunk is unloaded. A <b>pin</b> resolves the owning block (via the store's
/// live DocId→position index) only to capture a text/done snapshot. Lock-free either way: pinning
/// never touches the document, its edit lock, or autosave (see
/// <see cref="BlockEntityScribeLectern.SetTaskDoneFromReader"/> for the sibling lock-free path).
/// </summary>
[ProtoContract]
public sealed class ScribeSetPinMessage
{
    /// <summary>The owning document's <c>DocId</c> as 16 raw bytes.</summary>
    [ProtoMember(1)]
    public byte[]? DocId { get; set; }

    /// <summary>The task's <c>TaskId</c> as 16 raw bytes.</summary>
    [ProtoMember(2)]
    public byte[]? TaskId { get; set; }

    /// <summary>True to pin, false to unpin.</summary>
    [ProtoMember(3)]
    public bool Pinned { get; set; }
}
