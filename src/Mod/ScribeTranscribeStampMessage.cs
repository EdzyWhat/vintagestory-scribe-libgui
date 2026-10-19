using ProtoBuf;

namespace Scribe;

/// <summary>
/// Server → client: play the Transcribe "stamp" flourish over a Scriptorium slot for players OTHER than the
/// actor, who stamps locally on button press. Sent only after a copy or import actually writes and marks the
/// block dirty, so every other viewer of the shared block sees the same imprint.
///
/// <para>Addressed by block <b>position</b> (X/Y/Z): the client matches an open
/// <c>GuiDialogScribeScriptorium</c> there and replays the stamp on <see cref="Slot"/>. Purely a visual cue;
/// inventory resync updates the item independently. Export never broadcasts (it changes nothing on the block).</para>
/// </summary>
[ProtoContract]
public sealed class ScribeTranscribeStampMessage
{
    /// <summary>The Scriptorium's <c>BlockPos</c> coordinates.</summary>
    [ProtoMember(1)] public int X { get; set; }
    [ProtoMember(2)] public int Y { get; set; }
    [ProtoMember(3)] public int Z { get; set; }

    /// <summary>Inventory slot index the flourish stamps (the copy's Duplicate slot, or the Import/Export slot).</summary>
    [ProtoMember(4)] public int Slot { get; set; }

    /// <summary>Which word to imprint: <c>false</c> = COPIED (a copy), <c>true</c> = IMPORTED (an import).</summary>
    [ProtoMember(5)] public bool Imported { get; set; }
}
