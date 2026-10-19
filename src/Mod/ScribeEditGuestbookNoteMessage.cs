using ProtoBuf;

namespace Scribe;

/// <summary>Client -&gt; server: update the note on the sender's own guestbook entry. The server authorizes by matching the sender's
/// display name to PlayerName and addresses the entry by <see cref="InGameDate"/> (one entry per in-game day visited).
/// Addressed by <see cref="DocIdBytes"/>.</summary>
[ProtoContract]
public sealed class ScribeEditGuestbookNoteMessage
{
    /// <summary>The lectern's document <c>DocId</c> as 16 raw bytes.</summary>
    [ProtoMember(1)] public byte[]? DocIdBytes { get; set; }

    /// <summary>The note text (trimmed, max <see cref="Scribe.Core.GuestbookStore.MaxNoteLength"/> chars).</summary>
    [ProtoMember(4)] public string? Note { get; set; }

    /// <summary>The in-game date of the entry being edited — the second half of the natural key
    /// <c>(PlayerName, InGameDate)</c> that uniquely identifies one of the sender's own entries.</summary>
    [ProtoMember(5)] public string? InGameDate { get; set; }
}
