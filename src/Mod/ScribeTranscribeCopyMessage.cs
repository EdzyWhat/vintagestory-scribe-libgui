using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client → server: copy the <see cref="Scribe.Core.ScribeDocument"/> stored on the Scriptorium's
/// Original (source) inventory slot onto its Duplicate (target) slot — the Transcribe view's
/// "stamp to copy" gesture (add-transcribe-copy-paste D2). Addressed by the Scriptorium's block
/// <b>position</b> (X/Y/Z), not a DocId: the copy operates on whole items in a specific block's
/// inventory, so the server resolves the <see cref="BlockEntityScriptorium"/> at that position and
/// reads/writes its slots directly.
///
/// <para>The copy is server-authoritative: the client only requests it. The server clones the source
/// document with a FRESH identity (<see cref="Scribe.Core.ScribeDocument.CloneWithNewIdentity"/>) so
/// the two items never share <c>DocId</c>/<c>TaskId</c>s, writes it onto the target item via
/// <see cref="ScribeDocumentAttributes.WriteTo"/>, marks the slot/BE dirty, and lets the standard
/// inventory sync propagate the result back to every viewer.</para>
///
/// <para><see cref="AllowOverwrite"/> gates replacing a non-empty target: the client's two-press
/// confirm UX sets it true only on the confirming press, and the server re-checks it defensively
/// (an overwrite of a non-empty target with the flag false is a no-op) so a stale/hostile packet can
/// never silently clobber contents.</para>
/// </summary>
[ProtoContract]
public sealed class ScribeTranscribeCopyMessage
{
    /// <summary>The Scriptorium block position — the three coordinates of its <c>BlockPos</c>.</summary>
    [ProtoMember(1)] public int X { get; set; }
    [ProtoMember(2)] public int Y { get; set; }
    [ProtoMember(3)] public int Z { get; set; }

    /// <summary>Inventory slot index of the Original (source) item.</summary>
    [ProtoMember(4)] public int SourceSlot { get; set; }

    /// <summary>Inventory slot index of the Duplicate (target) item.</summary>
    [ProtoMember(5)] public int TargetSlot { get; set; }

    /// <summary>True only on the confirming press: permits overwriting a target that already has
    /// contents. When false, a non-empty target is left untouched (server-side defensive gate).</summary>
    [ProtoMember(6)] public bool AllowOverwrite { get; set; }
}
