using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client → server: import a <see cref="Scribe.Core.ScribeDocument"/> — parsed from the player's clipboard
/// (JSON or TSV) and validated against the running game on the client — onto the Scriptorium's Import/Export
/// slot (add-scriptorium-import-export D6). The sibling of <see cref="ScribeTranscribeCopyMessage"/>: same
/// Scriptorium-block addressing (X/Y/Z) and same Overwrite/Append + AllowOverwrite gating, but instead of a
/// source SLOT it carries the already-parsed document as a <b>JSON payload string</b> (the client serializes
/// the validated document with <see cref="Scribe.Core.ScribeDocumentJsonCodec"/>).
///
/// <para>Server-authoritative, exactly like the copy: the client only requests the import. The server
/// re-deserializes the payload with the JSON codec (whose caps + fresh-identity rules are the real guard),
/// mints a FRESH <c>TaskId</c> per block (and a fresh <c>DocId</c> on overwrite), re-checks the target's
/// capacity/writeability, writes onto the slot's item, and lets inventory sync propagate. Because every id is
/// fresh and pins live in a separate <c>(DocId, TaskId)</c> store, an import can never create or resurrect a
/// pin (D6).</para>
///
/// <para><see cref="AllowOverwrite"/> gates replacing a non-empty target (the client's confirm UX sets it, the
/// server re-checks it); <see cref="Append"/> selects non-destructive append mode, which needs no overwrite
/// confirm — identical semantics to the copy message.</para>
/// </summary>
[ProtoContract]
public sealed class ScribeTranscribeImportMessage
{
    /// <summary>The Scriptorium block position — the three coordinates of its <c>BlockPos</c>.</summary>
    [ProtoMember(1)] public int X { get; set; }
    [ProtoMember(2)] public int Y { get; set; }
    [ProtoMember(3)] public int Z { get; set; }

    /// <summary>Inventory slot index of the Import/Export (target) item the document is written onto.</summary>
    [ProtoMember(4)] public int TargetSlot { get; set; }

    /// <summary>The document to import, serialized as JSON by <see cref="Scribe.Core.ScribeDocumentJsonCodec"/>.
    /// TSV clipboards are parsed to a document on the client and re-serialized to JSON here, so the wire format
    /// is always the one lossless codec and the server has a single parse path.</summary>
    [ProtoMember(5)] public string DocumentJson { get; set; } = "";

    /// <summary>True only on the confirming press: permits overwriting a target that already has contents. When
    /// false, a non-empty target is left untouched (server-side defensive gate). Ignored when <see cref="Append"/>
    /// is set — appending is non-destructive, so there is nothing to overwrite-gate.</summary>
    [ProtoMember(6)] public bool AllowOverwrite { get; set; }

    /// <summary>Import MODE. When false (default) the import REPLACES the target document (guarded by
    /// <see cref="AllowOverwrite"/>). When true the imported tasks are APPENDED onto the target's existing
    /// document — non-destructive, so it needs no overwrite confirm. The server re-checks the target's capacity
    /// against target-count + incoming-count either way.</summary>
    [ProtoMember(7)] public bool Append { get; set; }
}
