using ProtoBuf;

namespace Scribe;

/// <summary>Client → server: update the note on the sender's own guestbook entry for this lectern.
/// The server authorizes by matching the packet sender's display name to the entry's PlayerName.</summary>
[ProtoContract]
public sealed class ScribeEditGuestbookNoteMessage
{
    [ProtoMember(1)] public int PosX { get; set; }
    [ProtoMember(2)] public int PosY { get; set; }
    [ProtoMember(3)] public int PosZ { get; set; }

    /// <summary>The note text (trimmed, max <see cref="Scribe.Core.GuestbookStore.MaxNoteLength"/> chars).</summary>
    [ProtoMember(4)] public string? Note { get; set; }
}
