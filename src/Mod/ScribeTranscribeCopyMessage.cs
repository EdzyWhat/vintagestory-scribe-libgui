using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client → server: copy the <see cref="Scribe.Core.ScribeDocument"/> on the Scriptorium's Original slot
/// onto its Duplicate slot. Addressed by block <b>position</b> (X/Y/Z), not a DocId; the server resolves the
/// <see cref="BlockEntityScriptorium"/> there and reads/writes its slots directly.
///
/// <para>Server-authoritative: the server clones the source with a FRESH identity
/// (<see cref="Scribe.Core.ScribeDocument.CloneWithNewIdentity"/>) so the two items never share
/// <c>DocId</c>/<c>TaskId</c>s, writes it via <see cref="ScribeDocumentAttributes.WriteTo"/>, marks dirty,
/// and lets inventory sync propagate.</para>
///
/// <para><see cref="AllowOverwrite"/> gates replacing a non-empty target; the client sets it only on the
/// confirming press and the server re-checks it, so a stale/hostile packet can't clobber contents.</para>
/// </summary>
[ProtoContract]
public sealed class ScribeTranscribeCopyMessage
{
    /// <summary>The Scriptorium's <c>BlockPos</c> coordinates.</summary>
    [ProtoMember(1)] public int X { get; set; }
    [ProtoMember(2)] public int Y { get; set; }
    [ProtoMember(3)] public int Z { get; set; }

    /// <summary>Inventory slot index of the Original (source) item.</summary>
    [ProtoMember(4)] public int SourceSlot { get; set; }

    /// <summary>Inventory slot index of the Duplicate (target) item.</summary>
    [ProtoMember(5)] public int TargetSlot { get; set; }

    /// <summary>True only on the confirming press: permits overwriting a non-empty target (otherwise left
    /// untouched). Ignored when <see cref="Append"/> is set, since appending is non-destructive.</summary>
    [ProtoMember(6)] public bool AllowOverwrite { get; set; }

    /// <summary>Copy mode. False (default) REPLACES the target (guarded by <see cref="AllowOverwrite"/>); true
    /// APPENDS the source's tasks to the target's document. The server re-checks capacity either way.</summary>
    [ProtoMember(7)] public bool Append { get; set; }
}
