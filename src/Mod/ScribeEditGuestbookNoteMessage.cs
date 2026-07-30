using ProtoBuf;

namespace Scribe;

/// <summary>Client → server: update the note on the sender's own guestbook entry for this lectern.
/// The server authorizes by matching the packet sender's display name to the entry's PlayerName.
/// Addressed by <see cref="DocIdBytes"/> (replacing the former PosX/Y/Z fields).</summary>
[ProtoContract]
public sealed class ScribeEditGuestbookNoteMessage
{
    /// <summary>The lectern's document <c>DocId</c> as 16 raw bytes.</summary>
    [ProtoMember(1)] public byte[]? DocIdBytes { get; set; }

    /// <summary>The note text (trimmed, max <see cref="Scribe.Core.GuestbookStore.MaxNoteLength"/> chars).</summary>
    [ProtoMember(4)] public string? Note { get; set; }
}
