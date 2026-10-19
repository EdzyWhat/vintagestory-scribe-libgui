using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client → server: import a clipboard-parsed (JSON or TSV), client-validated
/// <see cref="Scribe.Core.ScribeDocument"/> onto the Scriptorium's Import/Export slot. Sibling of
/// <see cref="ScribeTranscribeCopyMessage"/> (same block addressing and Overwrite/Append gating), but it
/// carries the document as a <b>JSON payload string</b> (<see cref="Scribe.Core.ScribeDocumentJsonCodec"/>).
///
/// <para>Server-authoritative: the server re-deserializes the payload (the codec's caps + fresh-identity
/// rules are the real guard), mints a FRESH <c>TaskId</c> per block (and a fresh <c>DocId</c> on overwrite),
/// re-checks capacity/writeability, and writes the slot's item. Since every id is fresh and pins live in a
/// separate <c>(DocId, TaskId)</c> store, an import can never create or resurrect a pin.</para>
///
/// <para><see cref="AllowOverwrite"/> and <see cref="Append"/> have the same semantics as in the copy message.</para>
/// </summary>
[ProtoContract]
public sealed class ScribeTranscribeImportMessage
{
    /// <summary>The Scriptorium's <c>BlockPos</c> coordinates.</summary>
    [ProtoMember(1)] public int X { get; set; }
    [ProtoMember(2)] public int Y { get; set; }
    [ProtoMember(3)] public int Z { get; set; }

    /// <summary>Inventory slot index of the Import/Export (target) item the document is written onto.</summary>
    [ProtoMember(4)] public int TargetSlot { get; set; }

    /// <summary>The document as JSON from <see cref="Scribe.Core.ScribeDocumentJsonCodec"/>. TSV clipboards
    /// are re-serialized to JSON on the client, so the server has a single parse path.</summary>
    [ProtoMember(5)] public string DocumentJson { get; set; } = "";

    /// <summary>True only on the confirming press: permits overwriting a non-empty target (otherwise left
    /// untouched). Ignored when <see cref="Append"/> is set, since appending is non-destructive.</summary>
    [ProtoMember(6)] public bool AllowOverwrite { get; set; }

    /// <summary>Import mode. False (default) REPLACES the target (guarded by <see cref="AllowOverwrite"/>);
    /// true APPENDS the imported tasks to the target's document. The server re-checks capacity either way.</summary>
    [ProtoMember(7)] public bool Append { get; set; }
}
