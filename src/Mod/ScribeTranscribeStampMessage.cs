using ProtoBuf;

namespace Scribe;

/// <summary>
/// Server → client (watcher-stamp-sync): play the Transcribe "stamp" flourish over a Scriptorium slot for
/// players OTHER than the one who performed the copy/import. The acting client stamps locally the instant it
/// presses the button (snappy, no round-trip); the server sends this broadcast — ONLY after a copy or import
/// actually writes and marks the block dirty — to every OTHER player, so anyone with the same shared block's
/// dialog open sees the identical IMPRINT land.
///
/// <para>Addressed by block <b>position</b> (X/Y/Z), like its <see cref="ScribeTranscribeCopyMessage"/>
/// sibling: the client matches an open <c>GuiDialogScribeScriptorium</c> at that position and replays the
/// stamp on <see cref="Slot"/>. This carries no document — the standard inventory resync (from the same
/// <c>MarkDirty</c> that triggered this) updates the slot item independently; this message is purely the
/// visual cue. Export never broadcasts: it changes nothing on the block, so no watcher needs telling.</para>
/// </summary>
[ProtoContract]
public sealed class ScribeTranscribeStampMessage
{
    /// <summary>The Scriptorium block position — the three coordinates of its <c>BlockPos</c>.</summary>
    [ProtoMember(1)] public int X { get; set; }
    [ProtoMember(2)] public int Y { get; set; }
    [ProtoMember(3)] public int Z { get; set; }

    /// <summary>Inventory slot index the flourish stamps (the copy's Duplicate slot, or the Import/Export slot).</summary>
    [ProtoMember(4)] public int Slot { get; set; }

    /// <summary>Which word to imprint: <c>false</c> = COPIED (a copy), <c>true</c> = IMPORTED (an import).</summary>
    [ProtoMember(5)] public bool Imported { get; set; }
}
