using ProtoBuf;

namespace Scribe;

/// <summary>What a <see cref="ScribeSetTaskDateMessage"/> asks the server to do to one task's date.</summary>
public enum ScribeDateOperation : byte
{
    /// <summary>Set the date from <c>Year</c>, <c>Month</c> and <c>Day</c>; the task's Repeat yearly flag is kept.</summary>
    SetDate = 0,

    /// <summary>Remove the date and the Repeat yearly flag.</summary>
    ClearDate = 1,

    /// <summary>Set the Repeat yearly flag to <c>Repeat</c>; ignored on an undated task.</summary>
    SetRepeat = 2,
}

/// <summary>
/// Client -&gt; server: change one task's date from a read view, addressed by stable identity
/// <c>(DocId, TaskId)</c>. One operation per message, not the whole date state, so a client with a stale view
/// cannot overwrite another viewer's change. The server refuses while another player holds the document's editor
/// lock, and for a document, task or date it cannot apply; every refusal re-sends the authoritative document to
/// the sender so the read view's optimistic copy reverts.
/// </summary>
[ProtoContract]
public sealed class ScribeSetTaskDateMessage
{
    /// <summary>The owning document's <c>DocId</c> as 16 raw bytes.</summary>
    [ProtoMember(1)]
    public byte[]? DocId { get; set; }

    /// <summary>The task's <c>TaskId</c> as 16 raw bytes.</summary>
    [ProtoMember(2)]
    public byte[]? TaskId { get; set; }

    /// <summary>The <see cref="ScribeDateOperation"/> as its raw byte; an unknown value is ignored.</summary>
    [ProtoMember(3)]
    public byte Operation { get; set; }

    /// <summary>The date's year for <see cref="ScribeDateOperation.SetDate"/>.</summary>
    [ProtoMember(4)]
    public int Year { get; set; }

    /// <summary>The date's month (1..12) for <see cref="ScribeDateOperation.SetDate"/>.</summary>
    [ProtoMember(5)]
    public int Month { get; set; }

    /// <summary>The date's day for <see cref="ScribeDateOperation.SetDate"/>.</summary>
    [ProtoMember(6)]
    public int Day { get; set; }

    /// <summary>The flag for <see cref="ScribeDateOperation.SetRepeat"/>.</summary>
    [ProtoMember(7)]
    public bool Repeat { get; set; }
}
